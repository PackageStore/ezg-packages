// Lệnh name (QC 7, gate IP-14): chấm độ mạnh của tên game, so với tên game lớn, game tham chiếu, đối thủ, thương hiệu, và đưa
// link tra cứu nhãn hiệu theo thị trường. Không gửi tên đi đâu: tên chưa công bố là bí mật kinh doanh; người tự mở link tra.
import path from "node:path";
import { fileURLToPath } from "node:url";
import { readText } from "./fsutil.mjs";
import { loadBrands } from "./brands.mjs";

const DATA = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "..", "data");

const lines = (f) => (readText(path.join(DATA, ...f)) || "").split(/\r?\n/).map((l) => l.trim()).filter((l) => l && !l.startsWith("#"));

export function words(s) {
  return s
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .normalize("NFD")
    .replace(/[̀-ͯ]/g, "")
    .toLowerCase()
    .split(/[^a-z0-9]+/)
    .filter(Boolean);
}

function levenshtein(a, b) {
  const d = Array.from({ length: a.length + 1 }, (_, i) => [i]);
  for (let j = 1; j <= b.length; j++) d[0][j] = j;
  for (let i = 1; i <= a.length; i++)
    for (let j = 1; j <= b.length; j++) d[i][j] = Math.min(d[i - 1][j] + 1, d[i][j - 1] + 1, d[i - 1][j - 1] + (a[i - 1] === b[j - 1] ? 0 : 1));
  return d[a.length][b.length];
}

/** Bộ khung âm: bỏ nguyên âm, gộp phụ âm đọc gần nhau, bỏ lặp ("Crush" ~ "Krush", "Fone" ~ "Phone"). */
function skeleton(s) {
  return s
    .toLowerCase()
    .replace(/ph/g, "f")
    .replace(/[ckq]/g, "k")
    .replace(/[sz]/g, "s")
    .replace(/[iy]/g, "i")
    .replace(/[^a-z]/g, "")
    .replace(/[aeiouh w]/g, "")
    .replace(/(.)\1+/g, "$1");
}

export const MARKET_LINKS = {
  US: ["USPTO Trademark Search", "https://tmsearch.uspto.gov"],
  EU: ["EUIPO eSearch / TMview", "https://www.tmdn.org/tmview"],
  UK: ["UKIPO trademark search", "https://trademarks.ipo.gov.uk/ipo-tmtext"],
  JP: ["J-PlatPat", "https://www.j-platpat.inpit.go.jp"],
  KR: ["KIPRIS", "https://www.kipris.or.kr"],
  CN: ["CNIPA (tra cả tên chữ Hán)", "https://sbj.cnipa.gov.cn"],
  VN: ["Thư viện số SHCN, Cục SHTT", "https://iplib.ipvietnam.gov.vn"],
  WIPO: ["WIPO Global Brand Database (nhiều nước; cấm truy vấn tự động)", "https://branddb.wipo.int"],
};

/** Đánh giá tên. `others`: [{ name, from }] (game tham chiếu, đối thủ của project). */
export function assessName(name, { markets = ["VN", "US", "EU", "UK", "JP", "KR", "CN"], others = [] } = {}) {
  const generic = new Set(lines(["terms", "generic_words.txt"]));
  const games = lines(["terms", "games.txt"]).map((n) => ({ name: n, from: "game lớn" }));
  const brands = loadBrands().map((b) => ({ name: b.name, from: `thương hiệu (${b.category})` }));
  const tokens = words(name);
  const gen = tokens.filter((t) => generic.has(t));
  const ratio = tokens.length ? gen.length / tokens.length : 1;
  const findings = [];
  const add = (mark, text) => findings.push({ mark, text });

  let strength;
  if (!tokens.length) strength = "Không đọc được tên";
  else if (ratio === 1) strength = "Yếu: chỉ gồm từ mô tả, thể loại (khó đăng ký, khó ngăn người khác dùng)";
  else if (ratio >= 0.5) strength = "Trung bình: một nửa trở lên là từ mô tả, thể loại";
  else strength = "Có thể mạnh: không phải từ mô tả quen; người xác nhận đó là từ tự bịa (mạnh nhất) hay từ có sẵn";
  if (ratio === 1) add("!", `Toàn từ mô tả / thể loại: ${gen.join(", ")} (NL 3.2)`);
  else if (gen.length) add("·", `Từ mô tả / thể loại: ${gen.join(", ")}`);

  if (name.length > 30) add("✗", `Dài ${name.length} ký tự: tên trên App Store tối đa 30 (Apple 2.3.7)`);

  const joined = tokens.join("");
  const sk = skeleton(joined);
  const near = [];
  const seen = new Set();
  for (const o of [...others, ...games, ...brands]) {
    const ow = words(o.name);
    if (!ow.length || seen.has(ow.join(" "))) continue;
    seen.add(ow.join(" "));
    const oj = ow.join("");
    const shared = tokens.filter((t) => ow.includes(t));
    const distinct = shared.filter((t) => !generic.has(t));
    const contains = joined.includes(oj) && oj.length >= 4;
    const lev = 1 - levenshtein(joined, oj) / Math.max(joined.length, oj.length);
    const sameSound = sk.length >= 3 && sk === skeleton(oj);
    const why = [];
    if (contains) why.push(`chứa nguyên tên "${o.name}"`);
    if (distinct.length) why.push(`trùng từ đặc trưng: ${distinct.join(", ")}`);
    else if (shared.length >= 2 || (shared.length && ow.length >= 2 && shared.length / ow.length >= 0.5)) why.push(`trùng từ: ${shared.join(", ")}`);
    if (lev >= 0.75 && !contains) why.push(`viết gần giống (${Math.round(lev * 100)}%)`);
    if (sameSound && !contains) why.push("đọc gần giống");
    const allOfOther = ow.length >= 2 && ow.every((w) => tokens.includes(w));
    if (allOfOther && !contains) why.push(`có đủ mọi từ của "${o.name}"`);
    if (why.length) near.push({ name: o.name, from: o.from, why, serious: contains || allOfOther || distinct.length > 0 || sameSound || lev >= 0.85 });
  }
  near.sort((a, b) => Number(b.serious) - Number(a.serious));
  for (const n of near.slice(0, 15)) add(n.serious ? "✗" : "!", `Gần "${n.name}" (${n.from}): ${n.why.join("; ")}`);
  if (!near.length) add("✓", "Không gần tên game lớn, đối thủ, thương hiệu nào trong danh sách của skill và project (danh sách không đủ thay tra cứu)");

  const links = [...new Set([...markets, "WIPO"])].filter((m) => MARKET_LINKS[m]).map((m) => ({ market: m, label: MARKET_LINKS[m][0], url: MARKET_LINKS[m][1] }));
  return { name, tokens, strength, findings, links };
}

export function nameReport(r, date) {
  const L = [`# Kiểm tên: ${r.name}`, "", `Ngày: ${date}. Không phải tư vấn pháp lý; tên chưa công bố là bí mật kinh doanh, chỉ tra ở các trang dưới khi được phép.`, ""];
  L.push(`**Độ mạnh**: ${r.strength}`, "");
  for (const f of r.findings) L.push(`- ${f.mark} ${f.text}`);
  L.push("", "## Tra trùng (người làm, QC 7.2)", "");
  L.push("Tra ở nhóm 9 (phần mềm tải về) và 41 (giải trí, game trực tuyến); thêm 28 nếu có merch. Tra cả store: App Store, Google Play, Steam.");
  L.push("");
  for (const l of r.links) L.push(`- ${l.market}: ${l.label}, ${l.url}`);
  L.push("", "## Đăng ký (QC 7.3)", "");
  L.push("- Nộp đơn trước khi công bố / soft launch ở thị trường nộp-trước-được-trước (Việt Nam, EU, Anh, Nhật, Hàn, Trung Quốc).");
  L.push("- Madrid qua Cục SHTT: một đơn nhiều nước, phụ thuộc đơn gốc Việt Nam 5 năm (đơn gốc yếu thì cả hệ thống yếu).");
  L.push("- Trung Quốc: nộp cả tên Latin và tên chữ Hán. Mỹ: bắt buộc luật sư được cấp phép ở Mỹ.");
  L.push("", "Kết quả tra, ngày tra ghi tiếp vào file này.", "");
  return L.join("\n");
}
