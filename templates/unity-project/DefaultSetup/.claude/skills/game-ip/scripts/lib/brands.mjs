// Quét thương hiệu thật trong nội dung game (QC 4.15, gate IP-15): chữ người chơi thấy hoặc dữ liệu nội dung (CSV / TSV / JSON
// localize, ScriptableObject .asset, chữ trong prefab / scene, .txt dữ liệu), không quét code, file license / readme, folder bên
// thứ ba. Khớp phân biệt hoa thường, nguyên từ. Logo, hình dạng đặc trưng (xe, súng) máy không thấy: người xem.
import path from "node:path";
import { fileURLToPath } from "node:url";
import { readText } from "./fsutil.mjs";
import { LICENSE_FILE } from "./licenses.mjs";

const DATA = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "..", "data");
const CONTENT = /\.(csv|tsv|json|txt|asset|prefab|unity|xml|yaml|yml|po|strings)$/i;
const MAX = 20 * 1024 * 1024;

function regexFor(name) {
  const body = name
    .split(/([\s-]+)/)
    .map((part) => (/^[\s-]+$/.test(part) ? "[\\s-]*" : part.replace(/[.*+?^${}()|[\]\\&']/g, (c) => (c === "&" ? "\\s*&\\s*" : `\\${c}`))))
    .join("");
  return new RegExp(`(?<![A-Za-z0-9À-ỹ])${body}(?![A-Za-z0-9À-ỹ])`, "g");
}

/** Danh sách thương hiệu: data/terms/brands.txt + `extra` ([tên] hoặc [{ name, category }]). */
export function loadBrands(extra = []) {
  const list = (readText(path.join(DATA, "terms", "brands.txt")) || "")
    .split(/\r?\n/)
    .map((l) => l.trim())
    .filter((l) => l && !l.startsWith("#") && l.includes("|"))
    .map((l) => {
      const [category, name] = l.split("|");
      return { category: category.trim(), name: name.trim() };
    });
  for (const e of extra) list.push(typeof e === "string" ? { category: "project", name: e } : { category: e.category || "project", name: e.name });
  return list.map((b) => ({ ...b, re: regexFor(b.name) }));
}

/**
 * Quét file nội dung trong Assets (bỏ folder bên thứ ba, file license / readme). Trả về [{ brand, category, hits: [{ path, line,
 * snippet, inBuild }], count }].
 */
export function scanBrands({ index, inBuild, vendorDirs = [], extra = [] }) {
  const brands = loadBrands(extra);
  const byBrand = new Map();
  for (const f of index.files) {
    if (!f.path.startsWith("Assets/") || !CONTENT.test(f.path)) continue;
    if (vendorDirs.some((d) => f.path.startsWith(`${d}/`))) continue;
    const base = path.basename(f.path);
    if (LICENSE_FILE.test(base) || /readme|changelog|documentation|manual/i.test(base)) continue;
    if (/(^|\/)(Editor|AddressableAssetsData|Plugins)\//.test(f.path)) continue;
    let txt;
    try {
      txt = readText(f.abs);
    } catch {
      continue;
    }
    if (!txt || txt.length > MAX) continue;
    for (const b of brands) {
      b.re.lastIndex = 0;
      let m;
      while ((m = b.re.exec(txt))) {
        if (!byBrand.has(b.name)) byBrand.set(b.name, { brand: b.name, category: b.category, hits: [], count: 0 });
        const e = byBrand.get(b.name);
        e.count++;
        if (e.hits.length < 10) {
          const line = txt.slice(0, m.index).split("\n").length;
          const ls = txt.lastIndexOf("\n", m.index) + 1;
          const le = txt.indexOf("\n", m.index);
          // YAML của Unity ghi chữ ngoài ASCII dạng \uXXXX: giải ra cho người đọc báo cáo.
          const raw = txt.slice(ls, le < 0 ? undefined : le).trim().replace(/\\u([0-9a-fA-F]{4})/g, (_, h) => String.fromCharCode(parseInt(h, 16)));
          e.hits.push({ path: f.path, line, snippet: raw.slice(0, 140), inBuild: inBuild.has(f.path) });
        }
      }
    }
  }
  return [...byBrand.values()].sort((a, b) => b.count - a.count);
}
