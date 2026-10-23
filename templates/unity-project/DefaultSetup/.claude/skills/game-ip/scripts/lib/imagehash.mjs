// Giải mã ảnh ra mức xám và tính perceptual hash (pHash 64 bit) để tìm cặp ảnh giống nhau giữa project và ảnh tham chiếu
// (QC 4.11). Không phụ thuộc gói ngoài: PNG (mọi kiểu màu, không interlace), JPEG baseline (chỉ giải kênh sáng Y).
// JPEG progressive, PNG interlace, PSD: chưa đọc được → null (báo người lưu lại dạng PNG).
import fs from "node:fs";
import zlib from "node:zlib";

const MAX_BYTES = 64 * 1024 * 1024;

/** Ảnh xám { w, h, px: Float32Array 0..255 }; phần trong suốt trộn lên nền trắng. null nếu không đọc được. */
export function decodeGray(file) {
  let buf;
  try {
    if (fs.statSync(file).size > MAX_BYTES) return null;
    buf = fs.readFileSync(file);
  } catch {
    return null;
  }
  try {
    if (buf.length > 8 && buf.readUInt32BE(0) === 0x89504e47) return decodePng(buf);
    if (buf[0] === 0xff && buf[1] === 0xd8) return decodeJpegY(buf);
  } catch {
    return null;
  }
  return null;
}

// ---------- PNG ----------
function decodePng(buf) {
  let p = 8;
  let w = 0;
  let h = 0;
  let depth = 8;
  let ct = 0;
  let interlace = 0;
  let palette = null;
  let trns = null;
  const idat = [];
  while (p + 8 <= buf.length) {
    const len = buf.readUInt32BE(p);
    const type = buf.toString("latin1", p + 4, p + 8);
    const d = buf.subarray(p + 8, p + 8 + len);
    if (type === "IHDR") {
      w = d.readUInt32BE(0);
      h = d.readUInt32BE(4);
      depth = d[8];
      ct = d[9];
      interlace = d[12];
    } else if (type === "PLTE") palette = d;
    else if (type === "tRNS") trns = d;
    else if (type === "IDAT") idat.push(d);
    else if (type === "IEND") break;
    p += 12 + len;
  }
  if (!w || !h || interlace) return null;
  const channels = { 0: 1, 2: 3, 3: 1, 4: 2, 6: 4 }[ct];
  if (!channels) return null;
  const raw = zlib.inflateSync(Buffer.concat(idat));
  const bitsPP = depth * channels;
  const bpp = Math.max(1, bitsPP >> 3);
  const stride = Math.ceil((w * bitsPP) / 8);
  const out = Buffer.alloc(stride * h);
  let prev = Buffer.alloc(stride);
  for (let y = 0; y < h; y++) {
    const f = raw[y * (stride + 1)];
    const line = raw.subarray(y * (stride + 1) + 1, (y + 1) * (stride + 1));
    const cur = out.subarray(y * stride, (y + 1) * stride);
    for (let i = 0; i < stride; i++) {
      const a = i >= bpp ? cur[i - bpp] : 0;
      const b = prev[i];
      const c = i >= bpp ? prev[i - bpp] : 0;
      let v = line[i];
      if (f === 1) v += a;
      else if (f === 2) v += b;
      else if (f === 3) v += (a + b) >> 1;
      else if (f === 4) {
        const pa = Math.abs(b - c);
        const pb = Math.abs(a - c);
        const pc = Math.abs(a + b - 2 * c);
        v += pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
      }
      cur[i] = v & 0xff;
    }
    prev = cur;
  }
  const px = new Float32Array(w * h);
  const sample = (row, idx) => {
    // giá trị mẫu thứ idx của dòng, chuẩn 0..255
    if (depth === 8) return row[idx];
    if (depth === 16) return row[idx * 2];
    const perByte = 8 / depth;
    const byte = row[Math.floor(idx / perByte)];
    const shift = 8 - depth * ((idx % perByte) + 1);
    const v = (byte >> shift) & ((1 << depth) - 1);
    return ct === 3 ? v : (v * 255) / ((1 << depth) - 1);
  };
  for (let y = 0; y < h; y++) {
    const row = out.subarray(y * stride, (y + 1) * stride);
    for (let x = 0; x < w; x++) {
      let r;
      let g;
      let b;
      let a = 255;
      if (ct === 0) {
        r = g = b = sample(row, x);
      } else if (ct === 2) {
        r = sample(row, x * 3);
        g = sample(row, x * 3 + 1);
        b = sample(row, x * 3 + 2);
      } else if (ct === 3) {
        const i = sample(row, x);
        r = palette ? palette[i * 3] : 0;
        g = palette ? palette[i * 3 + 1] : 0;
        b = palette ? palette[i * 3 + 2] : 0;
        if (trns && i < trns.length) a = trns[i];
      } else if (ct === 4) {
        r = g = b = sample(row, x * 2);
        a = sample(row, x * 2 + 1);
      } else {
        r = sample(row, x * 4);
        g = sample(row, x * 4 + 1);
        b = sample(row, x * 4 + 2);
        a = sample(row, x * 4 + 3);
      }
      const lum = 0.299 * r + 0.587 * g + 0.114 * b;
      px[y * w + x] = (lum * a + 255 * (255 - a)) / 255;
    }
  }
  return { w, h, px };
}

// ---------- JPEG baseline, chỉ kênh Y ----------
const ZIGZAG = [0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5, 12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28, 35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51, 58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63];

function buildHuffman(counts, symbols) {
  const maxcode = new Int32Array(18).fill(-1);
  const valptr = new Int32Array(17);
  const mincode = new Int32Array(17);
  let code = 0;
  let k = 0;
  for (let l = 1; l <= 16; l++) {
    valptr[l] = k;
    mincode[l] = code;
    code += counts[l - 1];
    k += counts[l - 1];
    maxcode[l] = counts[l - 1] ? code - 1 : -1;
    code <<= 1;
  }
  maxcode[17] = 0x7fffffff;
  return { maxcode, valptr, mincode, symbols };
}

const COS = (() => {
  const t = new Float32Array(64);
  for (let x = 0; x < 8; x++) for (let u = 0; u < 8; u++) t[x * 8 + u] = (u === 0 ? Math.SQRT1_2 : 1) * Math.cos(((2 * x + 1) * u * Math.PI) / 16);
  return t;
})();

function idct8x8(coef, out) {
  const tmp = new Float32Array(64);
  for (let y = 0; y < 8; y++)
    for (let u = 0; u < 8; u++) {
      let s = 0;
      for (let v = 0; v < 8; v++) s += COS[y * 8 + v] * coef[v * 8 + u];
      tmp[y * 8 + u] = s / 2;
    }
  for (let y = 0; y < 8; y++)
    for (let x = 0; x < 8; x++) {
      let s = 0;
      for (let u = 0; u < 8; u++) s += COS[x * 8 + u] * tmp[y * 8 + u];
      out[y * 8 + x] = s / 2 + 128;
    }
}

function decodeJpegY(buf) {
  const qt = [];
  const hts = {};
  let frame = null;
  let restart = 0;
  let p = 2;
  while (p + 4 <= buf.length) {
    if (buf[p] !== 0xff) {
      p++;
      continue;
    }
    const marker = buf[p + 1];
    if (marker === 0xd8 || marker === 0x01 || (marker >= 0xd0 && marker <= 0xd7)) {
      p += 2;
      continue;
    }
    if (marker === 0xd9) break;
    const len = buf.readUInt16BE(p + 2);
    const d = buf.subarray(p + 4, p + 2 + len);
    if (marker === 0xdb) {
      let q = 0;
      while (q < d.length) {
        const pq = d[q] >> 4;
        const tq = d[q] & 15;
        const t = new Int32Array(64);
        for (let i = 0; i < 64; i++) t[ZIGZAG[i]] = pq ? d.readUInt16BE(q + 1 + i * 2) : d[q + 1 + i];
        qt[tq] = t;
        q += 1 + 64 * (pq ? 2 : 1);
      }
    } else if (marker === 0xc4) {
      let q = 0;
      while (q < d.length) {
        const tc = d[q] >> 4;
        const th = d[q] & 15;
        const counts = d.subarray(q + 1, q + 17);
        const n = counts.reduce((a, b) => a + b, 0);
        hts[`${tc}${th}`] = buildHuffman(counts, d.subarray(q + 17, q + 17 + n));
        q += 17 + n;
      }
    } else if (marker === 0xc0 || marker === 0xc1) {
      const comps = [];
      for (let i = 0; i < d[5]; i++) comps.push({ id: d[6 + i * 3], h: d[7 + i * 3] >> 4, v: d[7 + i * 3] & 15, tq: d[8 + i * 3] });
      frame = { h: d.readUInt16BE(1), w: d.readUInt16BE(3), comps };
    } else if (marker >= 0xc2 && marker <= 0xcf && marker !== 0xc4 && marker !== 0xc8 && marker !== 0xcc) {
      return null; // progressive, lossless, arithmetic: chưa hỗ trợ
    } else if (marker === 0xdd) restart = d.readUInt16BE(0);
    else if (marker === 0xda) {
      if (!frame) return null;
      const ns = d[0];
      const scomps = [];
      for (let i = 0; i < ns; i++) {
        const c = frame.comps.find((x) => x.id === d[1 + i * 2]);
        scomps.push({ c, dc: hts[`0${d[2 + i * 2] >> 4}`], ac: hts[`1${d[2 + i * 2] & 15}`] });
      }
      return decodeScan(buf, p + 2 + len, frame, scomps, qt, restart);
    }
    p += 2 + len;
  }
  return null;
}

function decodeScan(buf, start, frame, scomps, qt, restart) {
  const hmax = Math.max(...frame.comps.map((c) => c.h));
  const vmax = Math.max(...frame.comps.map((c) => c.v));
  const Y = frame.comps[0];
  const yW = Math.ceil((frame.w * Y.h) / hmax);
  const yH = Math.ceil((frame.h * Y.v) / vmax);
  const bw = Math.ceil(yW / 8);
  const bh = Math.ceil(yH / 8);
  const plane = new Float32Array(bw * 8 * bh * 8);
  let pos = start;
  let bits = 0;
  let nbits = 0;
  const readBit = () => {
    if (nbits === 0) {
      if (pos >= buf.length) return 0;
      let b = buf[pos++];
      if (b === 0xff) {
        const nx = buf[pos];
        if (nx === 0) pos++;
        else if (nx >= 0xd0 && nx <= 0xd7) b = 0; // gặp RST giữa chừng: dữ liệu hỏng, trả 0
      }
      bits = b;
      nbits = 8;
    }
    nbits--;
    return (bits >> nbits) & 1;
  };
  const receive = (n) => {
    let v = 0;
    for (let i = 0; i < n; i++) v = (v << 1) | readBit();
    return v;
  };
  const extend = (v, t) => (v < 1 << (t - 1) ? v - (1 << t) + 1 : v);
  const decodeHuff = (ht) => {
    let code = 0;
    for (let l = 1; l <= 16; l++) {
      code = (code << 1) | readBit();
      if (code <= ht.maxcode[l]) return ht.symbols[ht.valptr[l] + code - ht.mincode[l]];
    }
    throw new Error("huffman");
  };
  const preds = scomps.map(() => 0);
  const coef = new Float32Array(64);
  const blockOut = new Float32Array(64);
  const decodeBlock = (si, wantY, bx, by) => {
    const s = scomps[si];
    coef.fill(0);
    const t = decodeHuff(s.dc);
    const diff = t ? extend(receive(t), t) : 0;
    preds[si] += diff;
    const q = qt[s.c.tq];
    coef[0] = preds[si] * q[0];
    for (let k = 1; k < 64; ) {
      const rs = decodeHuff(s.ac);
      const r = rs >> 4;
      const sz = rs & 15;
      if (sz === 0) {
        if (r === 15) {
          k += 16;
          continue;
        }
        break;
      }
      k += r;
      if (k > 63) break;
      coef[ZIGZAG[k]] = extend(receive(sz), sz) * q[ZIGZAG[k]];
      k++;
    }
    if (!wantY || bx >= bw || by >= bh) return;
    idct8x8(coef, blockOut);
    const W = bw * 8;
    for (let y = 0; y < 8; y++) for (let x = 0; x < 8; x++) plane[(by * 8 + y) * W + bx * 8 + x] = blockOut[y * 8 + x];
  };
  const resetRestart = () => {
    nbits = 0;
    // bỏ qua tới sau marker RST
    while (pos + 1 < buf.length && !(buf[pos] === 0xff && buf[pos + 1] >= 0xd0 && buf[pos + 1] <= 0xd7)) pos++;
    pos += 2;
    preds.fill(0);
  };
  if (scomps.length === 1) {
    const c = scomps[0].c;
    const cw = Math.ceil(Math.ceil((frame.w * c.h) / hmax) / 8);
    const ch = Math.ceil(Math.ceil((frame.h * c.v) / vmax) / 8);
    const isY = c === Y;
    let n = 0;
    for (let by = 0; by < ch; by++)
      for (let bx = 0; bx < cw; bx++) {
        if (restart && n && n % restart === 0) resetRestart();
        decodeBlock(0, isY, bx, by);
        n++;
      }
  } else {
    const mx = Math.ceil(frame.w / (8 * hmax));
    const my = Math.ceil(frame.h / (8 * vmax));
    let n = 0;
    for (let mcy = 0; mcy < my; mcy++)
      for (let mcx = 0; mcx < mx; mcx++) {
        if (restart && n && n % restart === 0) resetRestart();
        for (let si = 0; si < scomps.length; si++) {
          const c = scomps[si].c;
          for (let v = 0; v < c.v; v++) for (let hh = 0; hh < c.h; hh++) decodeBlock(si, c === Y, mcx * c.h + hh, mcy * c.v + v);
        }
        n++;
      }
  }
  // cắt về kích thước kênh Y
  const W = bw * 8;
  const px = new Float32Array(yW * yH);
  for (let y = 0; y < yH; y++) for (let x = 0; x < yW; x++) px[y * yW + x] = Math.max(0, Math.min(255, plane[y * W + x]));
  return { w: yW, h: yH, px };
}

// ---------- pHash ----------
function resize(img, n) {
  const out = new Float32Array(n * n);
  const cnt = new Float32Array(n * n);
  for (let y = 0; y < img.h; y++) {
    const ty = Math.min(n - 1, Math.floor((y * n) / img.h));
    for (let x = 0; x < img.w; x++) {
      const tx = Math.min(n - 1, Math.floor((x * n) / img.w));
      out[ty * n + tx] += img.px[y * img.w + x];
      cnt[ty * n + tx]++;
    }
  }
  for (let i = 0; i < out.length; i++) out[i] = cnt[i] ? out[i] / cnt[i] : 255;
  return out;
}

const N = 32;
const DCT32 = (() => {
  const t = new Float32Array(N * 8);
  for (let u = 0; u < 8; u++) for (let x = 0; x < N; x++) t[u * N + x] = Math.cos(((2 * x + 1) * u * Math.PI) / (2 * N));
  return t;
})();

function hashOf(g) {
  // DCT 2 chiều, chỉ cần 8×8 hệ số tần số thấp
  const rows = new Float32Array(N * 8);
  for (let y = 0; y < N; y++) for (let u = 0; u < 8; u++) {
    let s = 0;
    for (let x = 0; x < N; x++) s += g[y * N + x] * DCT32[u * N + x];
    rows[y * 8 + u] = s;
  }
  const c = new Float32Array(64);
  for (let v = 0; v < 8; v++) for (let u = 0; u < 8; u++) {
    let s = 0;
    for (let y = 0; y < N; y++) s += rows[y * 8 + u] * DCT32[v * N + y];
    c[v * 8 + u] = s;
  }
  const ac = [...c.slice(1)].sort((a, b) => a - b);
  const med = ac[Math.floor(ac.length / 2)];
  let hi = 0;
  let lo = 0;
  for (let i = 0; i < 64; i++) {
    const bit = c[i] > med ? 1 : 0;
    if (i < 32) hi = (hi << 1) | bit;
    else lo = (lo << 1) | bit;
  }
  return [hi >>> 0, lo >>> 0];
}

/** pHash 64 bit của ảnh xám, và của ảnh lật ngang (bắt ảnh bị lật). */
export function phash(img) {
  const g = resize(img, N);
  const flipped = new Float32Array(N * N);
  for (let y = 0; y < N; y++) for (let x = 0; x < N; x++) flipped[y * N + x] = g[y * N + (N - 1 - x)];
  return { hash: hashOf(g), flip: hashOf(flipped) };
}

function popcount(v) {
  v -= (v >>> 1) & 0x55555555;
  v = (v & 0x33333333) + ((v >>> 2) & 0x33333333);
  return (((v + (v >>> 4)) & 0x0f0f0f0f) * 0x01010101) >>> 24;
}

export function hamming(a, b) {
  return popcount(a[0] ^ b[0]) + popcount(a[1] ^ b[1]);
}

export const hashHex = (h) => h[0].toString(16).padStart(8, "0") + h[1].toString(16).padStart(8, "0");
