// Đọc tag trong file âm thanh để tìm nguồn: ID3v2 / ID3v1 (mp3), RIFF INFO (wav), Vorbis comment (ogg).
// Chỉ lấy các trường nói về nguồn, tác giả, bản quyền: tên, nghệ sĩ, album, bản quyền, nhà phát hành, phần mềm, ghi chú, URL.
import { readAt, withFile } from "./fsutil.mjs";

const ID3_FRAMES = {
  TIT2: "title", TPE1: "artist", TALB: "album", TCOP: "copyright", TPUB: "publisher", TENC: "encodedBy", TSSE: "software",
  COMM: "comment", TXXX: "userText", WXXX: "url", WCOP: "copyrightUrl", WOAR: "artistUrl", WPUB: "publisherUrl", WOAS: "sourceUrl",
  // ID3v2.2 (3 ký tự)
  TT2: "title", TP1: "artist", TAL: "album", TCR: "copyright", TPB: "publisher", TEN: "encodedBy", TSS: "software", COM: "comment",
  TXX: "userText", WXX: "url",
};
const RIFF_INFO = { INAM: "title", IART: "artist", ICOP: "copyright", ICMT: "comment", ISFT: "software", IPRD: "album", ISRC: "source", IENG: "engineer" };

function decodeText(buf, enc) {
  if (!buf.length) return "";
  let s;
  if (enc === 1 || enc === 2) {
    let le = enc === 1;
    let start = 0;
    if (buf[0] === 0xff && buf[1] === 0xfe) (le = true), (start = 2);
    else if (buf[0] === 0xfe && buf[1] === 0xff) (le = false), (start = 2);
    else if (enc === 2) le = false;
    s = "";
    for (let i = start; i + 1 < buf.length; i += 2) s += String.fromCharCode(le ? buf.readUInt16LE(i) : buf.readUInt16BE(i));
  } else s = buf.toString(enc === 3 ? "utf8" : "latin1");
  return s.replace(/\0+/g, " | ").replace(/\s+/g, " ").replace(/^[ |]+|[ |]+$/g, "");
}

function synchsafe(b, o) {
  return ((b[o] & 0x7f) << 21) | ((b[o + 1] & 0x7f) << 14) | ((b[o + 2] & 0x7f) << 7) | (b[o + 3] & 0x7f);
}

function readId3v2(fd) {
  const h = readAt(fd, 0, 10);
  if (h.length < 10 || h.toString("latin1", 0, 3) !== "ID3") return null;
  const ver = h[3];
  const size = synchsafe(h, 6);
  const body = readAt(fd, 10, Math.min(size, 512 * 1024));
  const tags = {};
  let p = 0;
  // Bỏ extended header nếu có (cờ bit 6).
  if (h[5] & 0x40 && body.length >= 4) p += ver === 4 ? synchsafe(body, 0) : body.readUInt32BE(0) + 4;
  const idLen = ver === 2 ? 3 : 4;
  const hdrLen = ver === 2 ? 6 : 10;
  while (p + hdrLen <= body.length) {
    const id = body.toString("latin1", p, p + idLen);
    if (!/^[A-Z0-9]+$/.test(id)) break;
    let len;
    if (ver === 2) len = (body[p + 3] << 16) | (body[p + 4] << 8) | body[p + 5];
    else if (ver === 4) len = synchsafe(body, p + 4);
    else len = body.readUInt32BE(p + 4);
    const data = body.subarray(p + hdrLen, p + hdrLen + len);
    p += hdrLen + len;
    const key = ID3_FRAMES[id];
    if (!key || !data.length) continue;
    let text;
    if (id[0] === "W" && id !== "WXXX" && id !== "WXX") text = data.toString("latin1").replace(/\0.*$/s, "");
    else if (id === "COMM" || id === "COM") text = decodeText(data.subarray(4), data[0]);
    else text = decodeText(data.subarray(1), data[0]);
    if (text) tags[key] = tags[key] ? `${tags[key]} | ${text}` : text;
  }
  return tags;
}

function readId3v1(fd, size) {
  if (size < 128) return null;
  const b = readAt(fd, size - 128, 128);
  if (b.toString("latin1", 0, 3) !== "TAG") return null;
  const f = (s, e) => b.toString("latin1", s, e).replace(/\0.*$/s, "").trim();
  const tags = {};
  for (const [k, s, e] of [["title", 3, 33], ["artist", 33, 63], ["album", 63, 93], ["comment", 97, 127]]) {
    const v = f(s, e);
    if (v) tags[k] = v;
  }
  return tags;
}

function readRiffInfo(fd, size) {
  const h = readAt(fd, 0, 12);
  if (h.toString("latin1", 0, 4) !== "RIFF") return null;
  const tags = {};
  let p = 12;
  for (let guard = 0; p + 8 <= size && guard < 512; guard++) {
    const ch = readAt(fd, p, 12);
    const id = ch.toString("latin1", 0, 4);
    const len = ch.readUInt32LE(4);
    if (id === "LIST" && ch.toString("latin1", 8, 12) === "INFO") {
      const b = readAt(fd, p + 12, Math.min(len - 4, 64 * 1024));
      let q = 0;
      while (q + 8 <= b.length) {
        const sid = b.toString("latin1", q, q + 4);
        const slen = b.readUInt32LE(q + 4);
        const key = RIFF_INFO[sid];
        if (key) {
          const v = b.toString("latin1", q + 8, q + 8 + slen).replace(/\0.*$/s, "").trim();
          if (v) tags[key] = v;
        }
        q += 8 + slen + (slen & 1);
      }
    }
    p += 8 + len + (len & 1);
  }
  return tags;
}

function readVorbis(fd) {
  // Gói comment nằm trong vài trang Ogg đầu; tìm chuỗi "\x03vorbis" trong 64 KB đầu.
  const b = readAt(fd, 0, 64 * 1024);
  if (b.toString("latin1", 0, 4) !== "OggS") return null;
  const at = b.indexOf(Buffer.from("\x03vorbis", "latin1"));
  if (at < 0) return {};
  let p = at + 7;
  const tags = {};
  try {
    const vlen = b.readUInt32LE(p);
    tags.software = b.toString("utf8", p + 4, p + 4 + vlen);
    p += 4 + vlen;
    const n = b.readUInt32LE(p);
    p += 4;
    for (let i = 0; i < n && i < 200; i++) {
      const len = b.readUInt32LE(p);
      const kv = b.toString("utf8", p + 4, p + 4 + len);
      p += 4 + len;
      const eq = kv.indexOf("=");
      if (eq < 0) continue;
      const k = kv.slice(0, eq).toUpperCase();
      const map = { TITLE: "title", ARTIST: "artist", ALBUM: "album", COPYRIGHT: "copyright", LICENSE: "license", ORGANIZATION: "publisher", COMMENT: "comment", DESCRIPTION: "comment", CONTACT: "url" };
      if (map[k]) tags[map[k]] = kv.slice(eq + 1).trim();
    }
  } catch {
    /* comment cắt ngang: giữ phần đã đọc */
  }
  return tags;
}

/** Tag nguồn của file âm thanh: { format, tags } ; tags rỗng khi file không có tag. null nếu không đọc được. */
export function readAudioTags(file) {
  return withFile(file, (fd, size) => {
    const lower = file.toLowerCase();
    if (lower.endsWith(".mp3")) {
      const tags = { ...(readId3v1(fd, size) || {}), ...(readId3v2(fd) || {}) };
      return { format: "mp3", tags };
    }
    if (lower.endsWith(".wav")) return { format: "wav", tags: readRiffInfo(fd, size) || {} };
    if (lower.endsWith(".ogg")) return { format: "ogg", tags: readVorbis(fd) || {} };
    return { format: lower.split(".").pop(), tags: {} };
  });
}
