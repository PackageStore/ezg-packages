// Đọc metadata nói về nguồn của ảnh: PNG (tEXt / zTXt / iTXt, chunk C2PA), JPEG (EXIF IFD0, XMP, IPTC, JUMBF C2PA),
// PSD (IPTC, XMP trong image resources). Chỉ đọc header và metadata, không đọc dữ liệu ảnh.
// Kết quả: tác giả, bản quyền, credit, nguồn, phần mềm, mô tả, URL quyền, DigitalSourceType (IPTC, đánh dấu ảnh AI),
// tham số sinh ảnh (Stable Diffusion WebUI ghi "parameters", ComfyUI ghi "prompt" / "workflow"), có manifest C2PA không.
import zlib from "node:zlib";
import { readAt, withFile } from "./fsutil.mjs";

const XMP_FIELDS = {
  creator: ["dc:creator"],
  rights: ["dc:rights"],
  credit: ["photoshop:Credit"],
  source: ["photoshop:Source", "dc:source"],
  software: ["xmp:CreatorTool"],
  description: ["dc:description"],
  webStatement: ["xmpRights:WebStatement"],
  digitalSourceType: ["Iptc4xmpExt:DigitalSourceType"],
};

function xmpValue(xmp, name) {
  const esc = name.replace(":", "\\:");
  const attr = xmp.match(new RegExp(`${esc}="([^"]*)"`));
  if (attr) return attr[1].trim();
  const el = xmp.match(new RegExp(`<${esc}[^>]*?(?:/>|>([\\s\\S]*?)</${esc}>)`));
  if (el && el[1]) {
    const res = el[1].match(/rdf:resource="([^"]*)"/);
    if (res) return res[1];
    return el[1].replace(/<[^>]+>/g, " ").replace(/\s+/g, " ").trim();
  }
  return null;
}

function addXmp(meta, xmp) {
  if (!xmp) return;
  for (const [k, names] of Object.entries(XMP_FIELDS)) {
    if (meta[k]) continue;
    for (const n of names) {
      const v = xmpValue(xmp, n);
      if (v) {
        meta[k] = v;
        break;
      }
    }
  }
  if (/c2pa|contentauth/i.test(xmp)) meta.c2pa = true;
}

function addIptc(meta, b) {
  const map = { 80: "creator", 110: "credit", 115: "source", 116: "rights", 120: "description" };
  for (let p = 0; p + 5 <= b.length; ) {
    if (b[p] !== 0x1c) {
      p++;
      continue;
    }
    const rec = b[p + 1];
    const ds = b[p + 2];
    const len = b.readUInt16BE(p + 3);
    if (rec === 2 && map[ds] && !meta[map[ds]]) meta[map[ds]] = b.toString("utf8", p + 5, p + 5 + len).trim();
    p += 5 + len;
  }
}

function addExif(meta, tiff) {
  if (tiff.length < 8) return;
  const le = tiff.toString("latin1", 0, 2) === "II";
  const u16 = (o) => (le ? tiff.readUInt16LE(o) : tiff.readUInt16BE(o));
  const u32 = (o) => (le ? tiff.readUInt32LE(o) : tiff.readUInt32BE(o));
  const ifd = u32(4);
  if (ifd + 2 > tiff.length) return;
  const n = u16(ifd);
  const tags = { 0x010e: "description", 0x0131: "software", 0x013b: "creator", 0x8298: "rights" };
  for (let i = 0; i < n; i++) {
    const e = ifd + 2 + i * 12;
    if (e + 12 > tiff.length) break;
    const tag = u16(e);
    const type = u16(e + 2);
    const count = u32(e + 4);
    if (!tags[tag] || type !== 2) continue;
    const off = count <= 4 ? e + 8 : u32(e + 8);
    const v = tiff.toString("latin1", off, off + count).replace(/\0+/g, " ").trim();
    if (v && !meta[tags[tag]]) meta[tags[tag]] = v;
  }
}

function readPng(fd, size) {
  const meta = {};
  let p = 8;
  for (let guard = 0; p + 8 <= size && guard < 10000; guard++) {
    const h = readAt(fd, p, 8);
    const len = h.readUInt32BE(0);
    const type = h.toString("latin1", 4, 8);
    if (type === "IEND") break;
    if (type === "caBX") meta.c2pa = true;
    if ((type === "tEXt" || type === "zTXt" || type === "iTXt") && len < 4 * 1024 * 1024) {
      const d = readAt(fd, p + 8, len);
      const z = d.indexOf(0);
      const key = d.toString("latin1", 0, z);
      let text = "";
      try {
        if (type === "tEXt") text = d.toString("latin1", z + 1);
        else if (type === "zTXt") text = zlib.inflateSync(d.subarray(z + 2)).toString("latin1");
        else {
          const compressed = d[z + 1] === 1;
          let q = z + 3;
          q = d.indexOf(0, q) + 1; // language tag
          q = d.indexOf(0, q) + 1; // translated keyword
          const body = d.subarray(q);
          text = (compressed ? zlib.inflateSync(body) : body).toString("utf8");
        }
      } catch {
        text = "";
      }
      const k = key.toLowerCase();
      if (k === "xml:com.adobe.xmp") addXmp(meta, text);
      else if (k === "parameters" || k === "prompt" || k === "workflow" || k === "invokeai_metadata" || k === "sd-metadata") meta.aiParams = text.slice(0, 300);
      else if (k === "author" && !meta.creator) meta.creator = text.trim();
      else if (k === "copyright" && !meta.rights) meta.rights = text.trim();
      else if (k === "source" && !meta.source) meta.source = text.trim();
      else if (k === "software" && !meta.software) meta.software = text.trim();
      else if ((k === "description" || k === "comment" || k === "title") && !meta.description) meta.description = text.trim().slice(0, 300);
    }
    p += 12 + len;
  }
  return meta;
}

function readJpeg(fd, size) {
  const meta = {};
  let p = 2;
  for (let guard = 0; p + 4 <= size && guard < 1000; guard++) {
    const h = readAt(fd, p, 4);
    if (h[0] !== 0xff) break;
    const marker = h[1];
    if (marker === 0xd9 || marker === 0xda) break; // EOI, SOS: hết metadata
    if (marker >= 0xd0 && marker <= 0xd7) {
      p += 2;
      continue;
    }
    const len = h.readUInt16BE(2);
    if ([0xe1, 0xeb, 0xed].includes(marker)) {
      const d = readAt(fd, p + 4, len - 2);
      if (marker === 0xe1 && d.toString("latin1", 0, 6) === "Exif\0\0") addExif(meta, d.subarray(6));
      else if (marker === 0xe1 && d.toString("latin1", 0, 28) === "http://ns.adobe.com/xap/1.0/") addXmp(meta, d.toString("utf8", 29));
      else if (marker === 0xeb && /c2pa|jumb/i.test(d.toString("latin1", 0, Math.min(d.length, 64)))) meta.c2pa = true;
      else if (marker === 0xed) {
        const at = d.indexOf(Buffer.from("8BIM\x04\x04", "latin1"));
        if (at >= 0) addIptc(meta, d.subarray(at));
      }
    }
    p += 2 + len;
  }
  return meta;
}

function readPsd(fd) {
  const meta = {};
  const h = readAt(fd, 0, 30);
  if (h.toString("latin1", 0, 4) !== "8BPS") return meta;
  const cmLen = h.readUInt32BE(26);
  const resStart = 30 + cmLen;
  const resLen = readAt(fd, resStart, 4).readUInt32BE(0);
  const b = readAt(fd, resStart + 4, Math.min(resLen, 8 * 1024 * 1024));
  let p = 0;
  while (p + 12 <= b.length && b.toString("latin1", p, p + 4) === "8BIM") {
    const id = b.readUInt16BE(p + 4);
    const nameLen = b[p + 6];
    let q = p + 6 + 1 + nameLen;
    if ((1 + nameLen) % 2) q++;
    const len = b.readUInt32BE(q);
    const data = b.subarray(q + 4, q + 4 + len);
    if (id === 0x0404) addIptc(meta, data);
    else if (id === 0x0424) addXmp(meta, data.toString("utf8"));
    p = q + 4 + len + (len % 2);
  }
  return meta;
}

/** Metadata nguồn của ảnh; {} khi không có; null khi không đọc được. */
export function readImageMeta(file) {
  return withFile(file, (fd, size) => {
    const sig = readAt(fd, 0, 8);
    if (sig.length >= 8 && sig.readUInt32BE(0) === 0x89504e47) return readPng(fd, size);
    if (sig[0] === 0xff && sig[1] === 0xd8) return readJpeg(fd, size);
    if (sig.toString("latin1", 0, 4) === "8BPS") return readPsd(fd);
    return {};
  });
}

/** Ảnh có dấu hiệu do AI tạo (DigitalSourceType của IPTC, tham số sinh ảnh, C2PA ghi công cụ AI). */
export function aiSignal(meta) {
  if (!meta) return null;
  if (meta.digitalSourceType && /trainedAlgorithmicMedia|algorithmicMedia/i.test(meta.digitalSourceType)) return `IPTC DigitalSourceType: ${meta.digitalSourceType.split("/").pop()}`;
  if (meta.aiParams) return "tham số sinh ảnh trong PNG";
  if (meta.software && /firefly|dall.?e|midjourney|stable.?diffusion|comfyui|imagen|gemini|leonardo|novelai|chatgpt|openai/i.test(meta.software)) return `phần mềm: ${meta.software}`;
  if (meta.c2pa) return "có manifest C2PA (thường do công cụ AI hoặc máy ảnh ghi; đọc bằng công cụ C2PA để biết)";
  return null;
}
