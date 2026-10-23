// Đọc thông tin license trong file font TrueType / OpenType (.ttf, .otf, .ttc): bảng `name` và `OS/2.fsType`.
// name ID: 0 bản quyền, 1 họ, 4 tên đầy đủ, 7 nhãn hiệu, 8 nhà cung cấp, 9 người thiết kế, 13 mô tả license, 14 URL license.
// fsType (OpenType spec, OS/2): 0 Installable, 2 Restricted, 4 Preview & Print, 8 Editable; 0x0100 No subsetting, 0x0200 Bitmap only.
import { readAt, withFile } from "./fsutil.mjs";

const NAME_IDS = { 0: "copyright", 1: "family", 4: "fullName", 7: "trademark", 8: "vendor", 9: "designer", 13: "license", 14: "licenseUrl" };

function decodeName(raw, platform) {
  if (platform === 0 || platform === 3) {
    let s = "";
    for (let k = 0; k + 1 < raw.length; k += 2) s += String.fromCharCode(raw.readUInt16BE(k));
    return s;
  }
  return raw.toString("latin1");
}

function readFace(fd, size, offset) {
  const head = readAt(fd, offset, 12);
  if (head.length < 12) return null;
  const numTables = head.readUInt16BE(4);
  const dir = readAt(fd, offset + 12, numTables * 16);
  const tables = {};
  for (let i = 0; i < numTables && (i + 1) * 16 <= dir.length; i++) {
    const o = i * 16;
    tables[dir.toString("latin1", o, o + 4)] = { off: dir.readUInt32BE(o + 8), len: dir.readUInt32BE(o + 12) };
  }
  const info = { fsType: null };
  const os2 = tables["OS/2"];
  if (os2 && os2.len >= 10) info.fsType = readAt(fd, os2.off + 8, 2).readUInt16BE(0);
  const nt = tables.name;
  if (nt && nt.off + nt.len <= size) {
    const b = readAt(fd, nt.off, nt.len);
    const count = b.readUInt16BE(2);
    const strOff = b.readUInt16BE(4);
    // Ưu tiên bản Windows (platform 3) tiếng Anh, rồi bất kỳ.
    const best = {};
    for (let i = 0; i < count && 6 + (i + 1) * 12 <= b.length; i++) {
      const r = 6 + i * 12;
      const platform = b.readUInt16BE(r);
      const lang = b.readUInt16BE(r + 4);
      const nameId = b.readUInt16BE(r + 6);
      const len = b.readUInt16BE(r + 8);
      const off = b.readUInt16BE(r + 10);
      const key = NAME_IDS[nameId];
      if (!key) continue;
      const score = (platform === 3 ? 2 : 0) + (lang === 0x409 || lang === 0 ? 1 : 0);
      if (best[key] && best[key].score >= score) continue;
      const raw = b.subarray(strOff + off, strOff + off + len);
      best[key] = { score, text: decodeName(raw, platform).replace(/\s+/g, " ").trim() };
    }
    for (const [k, v] of Object.entries(best)) info[k] = v.text;
  }
  return info;
}

/** Thông tin license của font; với .ttc trả mặt đầu tiên kèm `faces`. null nếu không phải font đọc được. */
export function readFontInfo(file) {
  return withFile(file, (fd, size) => {
    const tag = readAt(fd, 0, 4).toString("latin1");
    if (tag === "ttcf") {
      const h = readAt(fd, 0, 12);
      const n = Math.min(h.readUInt32BE(8), 64);
      const offs = readAt(fd, 12, n * 4);
      const faces = [];
      for (let i = 0; i < n; i++) faces.push(readFace(fd, size, offs.readUInt32BE(i * 4)));
      return { ...(faces[0] || {}), faces: faces.length };
    }
    if (tag !== "\0\x01\0\0" && tag !== "OTTO" && tag !== "true") return null;
    return readFace(fd, size, 0);
  });
}

/** Mức nhúng theo fsType: "installable" | "restricted" | "preview-print" | "editable" | null. */
export function embeddingOf(fsType) {
  if (fsType === null || fsType === undefined) return null;
  const t = fsType & 0x000f;
  if (t & 0x0008) return "editable";
  if (t & 0x0004) return "preview-print";
  if (t & 0x0002) return "restricted";
  return "installable";
}
