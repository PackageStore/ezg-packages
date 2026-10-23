// Lệnh compare (QC 6, gate IP-10, IP-11, IP-12): thẻ so sánh điền sẵn phần máy biết (án lệ trùng ở NL 5.4, ngày, tên) từ mẫu
// trong reference/compare-card.md, và cặp ảnh giống nhau giữa ảnh của mình với ảnh tham chiếu theo pHash. Phần nhận định (lọc,
// so tổng thể, kết luận) là của Claude và người duyệt, theo mẫu.
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { readText, walkFiles } from "./fsutil.mjs";
import { decodeGray, hamming, phash } from "./imagehash.mjs";

const SKILL = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "..");
const IMG = /\.(png|jpe?g)$/i;
const OTHER_IMG = /\.(psd|tga|tif|tiff|bmp|gif|webp|exr|hdr)$/i;

const norm = (s) => s.normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase().replace(/[^a-z0-9]+/g, "");

/** Án lệ ở data/cases.json có game khớp tên game tham chiếu (một bên chứa bên kia, sau khi bỏ dấu, khoảng trắng). */
export function matchCases(refName) {
  const cases = JSON.parse(readText(path.join(SKILL, "data", "cases.json"))).cases;
  const r = norm(refName || "");
  if (r.length < 3) return [];
  return cases
    .map((c) => ({ ...c, hit: c.games.find((g) => norm(g.title).includes(r) || r.includes(norm(g.title))) }))
    .filter((c) => c.hit);
}

/** Án lệ cùng thể loại (data/cases.json → genres), để tham khảo khi tên game tham chiếu không khớp vụ nào. */
export function casesByGenre(genre) {
  const g = norm(genre || "");
  if (!g) return [];
  const cases = JSON.parse(readText(path.join(SKILL, "data", "cases.json"))).cases;
  return cases.filter((c) => (c.genres || []).some((x) => norm(x) === g || norm(x).includes(g) || g.includes(norm(x))));
}

export function slug(s) {
  return (s || "phan").normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "").slice(0, 60) || "phan";
}

/** Thẻ so sánh từ mẫu trong reference/compare-card.md, điền tên phần, game tham chiếu, ngày, án lệ trùng, file cặp ảnh. */
export function compareCard({ part, ref, date, cases, pairsFile, brief, genreCases = [], closest = null }) {
  const tpl = readText(path.join(SKILL, "reference", "compare-card.md")) || "";
  const m = tpl.match(/```markdown\r?\n([\s\S]*?)```/);
  let card = m ? m[1] : "# Thẻ so sánh: <phần của mình> ↔ <game tham chiếu>\n";
  const caseText = cases.length
    ? cases.map((c) => `${c.case} (${c.place}, ${c.year}; game tham chiếu là bên ${c.hit.side === "plaintiff" ? "bị chép" : "bị kiện"}): ${c.lesson}`).join("; ")
    : genreCases.length
      ? `không vụ nào trùng tên game; cùng thể loại để tham khảo: ${genreCases.map((c) => `${c.case} (${c.year}): ${c.lesson}`).join("; ")}`
      : "không có trong NL 5.4";
  card = card
    .replace("<phần của mình>", part)
    .replace("<game tham chiếu>", ref)
    .replace("<yyyy-mm-dd>", date)
    .replace('<tên vụ, hoặc "không có">', caseText);
  if (brief) card = card.replace("- Brief / người làm nói học theo game nào (nguyên văn, ai nói, ở đâu): …", `- Brief / người làm nói học theo game nào (nguyên văn, ai nói, ở đâu): ${brief}`);
  if (pairsFile) card = card.replace("- Ảnh đặt cạnh nhau (IP-11): …", `- Ảnh đặt cạnh nhau (IP-11): xem \`${pairsFile}\` (máy chọn cặp gần nhất; người xem, bỏ cặp giống vì thể loại)`);
  if (closest && closest.distance <= 5)
    card = card.replace(
      /(## 4\. Kết luận[^\n]*\n)/,
      `$1- Gợi ý của máy: cặp ảnh ${closest.distance} / 64 bit${closest.flipped ? " (lật ngang)" : ""} là gần như cùng một ảnh (${closest.mine} ↔ ${closest.theirs}). Người xác nhận không phải hình chung của thể loại thì **Đỏ** (IP-11, chép asset là thứ dễ chứng minh nhất).\n`,
    );
  return card;
}

function collect(paths, base) {
  const files = [];
  const skipped = [];
  for (const p of paths) {
    const abs = path.resolve(base, p);
    let list = [];
    try {
      list = fs.statSync(abs).isDirectory() ? walkFiles(abs) : [abs];
    } catch {
      skipped.push({ file: p, why: "không tìm thấy" });
      continue;
    }
    for (const f of list) {
      if (IMG.test(f)) files.push(f);
      else if (OTHER_IMG.test(f)) skipped.push({ file: f, why: "định dạng chưa đọc (lưu lại dạng PNG để so)" });
    }
  }
  return { files, skipped };
}

/**
 * So ảnh: mọi ảnh trong `mine` với mọi ảnh trong `theirs`. Trả về cặp có khoảng cách pHash ≤ threshold (tính cả bản lật ngang),
 * sắp gần nhất trước, mỗi ảnh của mình giữ tối đa 3 cặp.
 */
export function compareImages({ mine, theirs, base = process.cwd(), threshold = 10, minSize = 16 }) {
  const a = collect(mine, base);
  const b = collect(theirs, base);
  const skipped = [...a.skipped, ...b.skipped];
  const hashAll = (files) => {
    const out = [];
    for (const f of files) {
      const g = decodeGray(f);
      if (!g) skipped.push({ file: f, why: "không giải mã được (PNG interlace, JPEG progressive?): lưu lại dạng PNG" });
      else if (g.w < minSize || g.h < minSize) skipped.push({ file: f, why: `nhỏ hơn ${minSize} px` });
      else out.push({ file: f, w: g.w, h: g.h, ...phash(g) });
    }
    return out;
  };
  const ha = hashAll(a.files);
  const hb = hashAll(b.files);
  const pairs = [];
  for (const x of ha) {
    const mine = [];
    for (const y of hb) {
      const d = hamming(x.hash, y.hash);
      const df = hamming(x.flip, y.hash);
      const dist = Math.min(d, df);
      if (dist <= threshold) mine.push({ mine: x.file, theirs: y.file, distance: dist, flipped: df < d });
    }
    mine.sort((p, q) => p.distance - q.distance);
    pairs.push(...mine.slice(0, 3));
  }
  pairs.sort((p, q) => p.distance - q.distance);
  return { pairs, skipped, counts: { mine: ha.length, theirs: hb.length } };
}

export function pairsReport({ part, ref, result, threshold, rel, link = null }) {
  const L = [`# Cặp ảnh giống: ${part} ↔ ${ref}`, ""];
  L.push(`Máy so ${result.counts.mine} ảnh của mình với ${result.counts.theirs} ảnh tham chiếu bằng pHash 64 bit (cả bản lật ngang), ngưỡng ≤ ${threshold}.`);
  L.push("Khoảng cách 0–5: gần như cùng ảnh; 6–10: rất giống, cần xem; trên 10 không liệt kê. Người mở từng cặp cạnh nhau: giống vì thể loại");
  L.push("(ô vuông, viên ngọc, nút tròn) thì bỏ; giống ở cách vẽ cụ thể thì ghi vào thẻ, mức Chặn (QC 4.11). pHash không bắt được ảnh bị cắt, ghép");
  L.push("vào ảnh lớn hơn hay vẽ lại cùng bố cục: không có cặp nào không có nghĩa là không giống.");
  L.push("");
  if (!result.pairs.length) L.push("_(không có cặp nào dưới ngưỡng)_");
  else {
    // Ảnh xem trước: đường dẫn tương đối từ file báo cáo (link), để trình xem Markdown hiện cạnh nhau.
    const img = (f) => (link ? `<img src="${link(f)}" width="96">` : "");
    L.push("| Khoảng cách | Ảnh của mình | Ảnh tham chiếu | Lật ngang | Xem |");
    L.push("|---|---|---|---|---|");
    for (const p of result.pairs) L.push(`| ${p.distance} | ${rel(p.mine)} | ${rel(p.theirs)} | ${p.flipped ? "có" : ""} | ${img(p.mine)} ${img(p.theirs)} |`);
  }
  if (result.skipped.length) {
    L.push("", "Bỏ qua:", "");
    for (const s of result.skipped.slice(0, 50)) L.push(`- ${rel(s.file)}: ${s.why}`);
    if (result.skipped.length > 50) L.push(`- … và ${result.skipped.length - 50} file nữa`);
  }
  L.push("");
  return L.join("\n");
}
