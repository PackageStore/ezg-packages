// Lệnh creative (QC 4.17, QC 8, gate IP-16, IP-17): kiểm một folder creative (video, ảnh, nhạc, font, playable) theo sổ riêng của
// creative `creative.json` đặt trong folder. Mỗi file phải có nguồn, mọi nguồn phải được dùng cho quảng cáo trả tiền trên đúng các
// mạng sẽ chạy. Không xem được nội dung video: cảnh, người, footage trong video do người khai trong sổ.
import fs from "node:fs";
import path from "node:path";
import { readAudioTags } from "./audio.mjs";
import { globToRegExp, readText, walkFiles } from "./fsutil.mjs";
import { aiSignal, readImageMeta } from "./image.mjs";
import { licenseInfo } from "./licenses.mjs";
import { loadBrands } from "./brands.mjs";

export const CREATIVE_FILE = "creative.json";
const KIND_BY_EXT = [
  [/\.(mp4|mov|webm|avi|mkv)$/i, "footage", "Video (gameplay quay từ game, hay footage / cutscene?)"],
  [/\.(mp3|wav|ogg|m4a|aac|flac)$/i, "music", "Nhạc / âm thanh"],
  [/\.(png|jpe?g|psd|gif|webp|tga|svg)$/i, "asset", "Ảnh"],
  [/\.(ttf|otf|woff2?)$/i, "font", "Font"],
  [/\.(html?|js|zip)$/i, "playable-code", "Playable"],
  [/\.(aep|prproj|drp|fla|blend|spine|json)$/i, "project-file", "File dựng"],
];
const SKIP = (f) => /^(creative\.json|GameIPCreative.*\.md|\.DS_Store|Thumbs\.db)$/i.test(path.basename(f));

/** Sổ creative nháp từ file trong folder: một mục cho mỗi loại file, phạm vi ads "unknown" (gameplay của mình: true). */
export function draftCreative(dir, name) {
  const files = walkFiles(dir, { skipDir: (n) => n.startsWith(".") }).filter((f) => !SKIP(f));
  const groups = new Map();
  for (const f of files) {
    const r = path.relative(dir, f).split(path.sep).join("/");
    const k = KIND_BY_EXT.find(([re]) => re.test(f)) || [null, "other", "Khác"];
    if (!groups.has(k[1])) groups.set(k[1], { title: k[2], paths: [] });
    groups.get(k[1]).paths.push(r);
  }
  return {
    creative: name,
    networks: [],
    storePreview: false,
    entries: [...groups.entries()].map(([kind, g]) => ({
      id: kind,
      kind,
      title: g.title,
      source: "",
      license: "unknown",
      scope: { ads: "unknown" },
      platforms: [],
      proof: "",
      notInGame: false,
      paths: g.paths,
      status: "draft",
      notes: "Máy gộp theo đuôi file: tách thành từng nguồn thật (mỗi bài nhạc, mỗi footage, phần gameplay quay từ game, phần AI)",
    })),
  };
}

/** Kiểm folder creative. Trả về { lines: [{ mark, gate, text }], totals }. */
export function checkCreative(dir, { games = [], brandsExtra = [], ownNames = [] } = {}) {
  const lines = [];
  const add = (mark, gate, text) => lines.push({ mark, gate, text });
  let sheet = null;
  try {
    sheet = JSON.parse(readText(path.join(dir, CREATIVE_FILE)));
  } catch {
    sheet = null;
  }
  const files = walkFiles(dir, { skipDir: (n) => n.startsWith(".") }).filter((f) => !SKIP(f));
  const rel = (f) => path.relative(dir, f).split(path.sep).join("/");
  const own = ownNames.map((n) => n.toLowerCase());
  const isOwn = (s) => !!s && own.some((n) => s.toLowerCase().includes(n));
  if (!sheet || !Array.isArray(sheet.entries)) {
    add("✗", "IP-17", `Chưa có ${CREATIVE_FILE} (sổ của creative): chạy \`creative <folder> --init\` rồi điền nguồn từng file`);
    sheet = { entries: [], networks: [] };
  }
  const networks = (sheet.networks || []).map((n) => n.toLowerCase());
  if (!networks.length) add("!", "IP-17", "Sổ chưa ghi `networks` (mạng sẽ chạy): không kiểm được nhạc của nền tảng, chính sách từng mạng");
  const compiled = sheet.entries.map((e) => ({ e, res: (e.paths || []).map(globToRegExp) }));
  const entryOf = (r) => {
    let best = null;
    let spec = -1;
    for (const c of compiled)
      for (let i = 0; i < c.res.length; i++) {
        const g = c.e.paths[i];
        const s = g.search(/[*?]/) < 0 ? 1e6 : g.search(/[*?]/);
        if (s > spec && c.res[i].test(r)) (best = c.e), (spec = s);
      }
    return best;
  };

  // Mục sổ
  for (const e of sheet.entries) {
    const L = licenseInfo(e.license || "unknown");
    const ads = e.scope && e.scope.ads;
    const draft = e.status !== "confirmed" ? ", mục còn draft" : "";
    if (ads === false || L.ads === false) add("✗", "IP-17", `Mục \`${e.id}\` (${e.title || e.kind}): không được dùng cho quảng cáo (${L.name})${draft}`);
    else if (ads !== true) add("✗", "IP-17", `Mục \`${e.id}\` (${e.title || e.kind}): chưa rõ được dùng cho quảng cáo trả tiền (\`scope.ads\` phải là true)${draft}`);
    else if (draft) add("!", "IP-17", `Mục \`${e.id}\` còn draft: người xác nhận nguồn, license rồi đặt \`status: "confirmed"\``);
    if (e.kind === "platform-music") {
      const plats = (e.platforms || []).map((p) => p.toLowerCase());
      const outside = networks.filter((n) => !plats.includes(n));
      if (!plats.length) add("✗", "IP-17", `Mục \`${e.id}\`: nhạc thư viện của nền tảng nhưng chưa ghi \`platforms\``);
      else if (outside.length) add("✗", "IP-17", `Mục \`${e.id}\`: nhạc thư viện của ${plats.join(", ")} chỉ dùng trên nền tảng đó, nhưng creative chạy cả ${outside.join(", ")}`);
    }
    if (["actor", "voice", "creator"].includes(e.kind) && !e.proof) add("✗", "IP-17", `Mục \`${e.id}\`: người thật / giọng thật cần hợp đồng quyền hình ảnh, giọng nói (\`proof\`)`);
    if (e.kind === "ai") {
      const ai = e.ai || {};
      const miss = ["tool", "plan", "prompt"].filter((k) => !ai[k]);
      if (miss.length) add("!", "IP-7", `Mục \`${e.id}\` (AI): thiếu ${miss.join(", ")}; người, nơi thật làm bằng AI chạy ở EU cần [DUYỆT] (AI Act Điều 50)`);
    }
    if (e.notInGame) {
      if (sheet.storePreview) add("✗", "IP-16", `Mục \`${e.id}\`: cảnh không có trong game, mà creative dùng làm preview App Store (Apple 2.3.4 chỉ cho quay màn hình app)`);
      else add("!", "IP-17", `Mục \`${e.id}\`: cảnh không có trong game: kiểm năm kiểu "gameplay giả" (NL 5.6), chính sách từng mạng; minigame như game chính, footage game khác → Chặn`);
    }
  }

  // File
  const brandList = loadBrands(brandsExtra);
  const gameRe = games.map((g) => ({ name: g, re: new RegExp(`(?<![A-Za-z0-9])${g.replace(/[.*+?^${}()|[\]\\]/g, "\\$&").replace(/\s+/g, "[\\s_-]*")}(?![A-Za-z0-9])`, "i") }));
  for (const f of files) {
    const r = rel(f);
    const e = entryOf(r);
    if (!e) add("✗", "IP-17", `${r}: chưa có mục trong sổ creative (thêm mục: id, kind, title, source, license, scope.ads, proof, paths; hoặc bỏ file khỏi folder nếu không dùng)`);
    const base = path.basename(f, path.extname(f));
    for (const g of gameRe) if (g.re.test(base) && !isOwn(g.name)) add("!", "IP-17", `${r}: tên file có "${g.name}"`);
    if (/\.(mp3|wav|ogg)$/i.test(f)) {
      const t = (readAudioTags(f) || {}).tags || {};
      const v = Object.entries(t).map(([k, x]) => `${k}: ${x}`).join("; ");
      if (v && !isOwn(v) && (t.copyright || t.publisher || t.url || /https?:|www\./.test(v)) && !(e && e.status === "confirmed")) add("✗", "IP-4", `${r}: tag ghi nguồn khác (${v.slice(0, 140)}), sổ chưa xác nhận`);
    }
    if (/\.(png|jpe?g|psd)$/i.test(f)) {
      const m = readImageMeta(f) || {};
      const who = [m.creator, m.rights, m.credit, m.source].filter(Boolean);
      if (who.length && !who.some(isOwn) && !(e && e.status === "confirmed")) add("✗", "IP-8", `${r}: metadata ghi ${who.join("; ")}`);
      const ai = aiSignal(m);
      if (ai && !(e && e.kind === "ai")) add("!", "IP-7", `${r}: ${ai}; chưa khai là AI trong sổ`);
    }
    if (/\.html?$/i.test(f)) {
      const size = fs.statSync(f).size;
      const html = readText(f) || "";
      if (size > 5 * 1024 * 1024) add("!", "IP-17", `${r}: playable ${(size / 1048576).toFixed(1)} MB, nhiều mạng giới hạn 5 MB`);
      const ext = [...new Set([...html.matchAll(/(?:src|href)\s*=\s*["'](https?:\/\/[^"']+)|url\(\s*["']?(https?:\/\/[^"')]+)|fetch\(\s*["'](https?:\/\/[^"']+)/gi)].map((m) => m[1] || m[2] || m[3]))];
      if (ext.length) add("!", "IP-17", `${r}: playable gọi tài nguyên ngoài (${ext.slice(0, 3).join(", ")}${ext.length > 3 ? "…" : ""}): nhiều mạng cấm khi chưa duyệt`);
      const fonts = (html.match(/@font-face/g) || []).length;
      if (fonts && !sheet.entries.some((x) => x.kind === "font")) add("!", "IP-17", `${r}: playable nhúng ${fonts} font, sổ chưa có mục font`);
      const text = html.replace(/<script[\s\S]*?<\/script>|<style[\s\S]*?<\/style>|data:[^"')\s]+/gi, " ");
      // Chữ quảng cáo chứa tên game khác, đối thủ: Chặn (QC 4.17). Thương hiệu khác trong chữ: cảnh báo (QC 4.15).
      const hitGames = gameRe.filter((g) => !isOwn(g.name) && g.re.test(text)).map((g) => g.name);
      for (const n of hitGames) add("✗", "IP-17", `${r}: chữ quảng cáo nhắc game khác "${n}" (QC 4.17: Chặn)`);
      for (const b of brandList) {
        b.re.lastIndex = 0;
        if (b.re.test(text) && !hitGames.some((n) => n.toLowerCase() === b.name.toLowerCase())) add("!", "IP-15", `${r}: nhắc "${b.name}" (${b.category})`);
      }
    }
  }
  if (!lines.some((l) => l.mark === "✗" || l.mark === "!")) add("✓", "IP-17", `${files.length} file, mọi file có nguồn được dùng cho quảng cáo trên ${networks.join(", ") || "các mạng đã ghi"}`);
  const totals = { fail: lines.filter((l) => l.mark === "✗").length, warn: lines.filter((l) => l.mark === "!").length };
  return { lines, totals, files: files.length, sheet };
}

export function creativeReport(dir, name, res, date) {
  const L = [`# Kiểm creative: ${name}`, "", `${date}. ${res.files} file trong ${dir}. ✗ ${res.totals.fail} · ! ${res.totals.warn}. Không phải tư vấn pháp lý.`, ""];
  for (const l of res.lines) L.push(`- ${l.mark} ${l.gate} ${l.text}`);
  L.push("", "Máy không xem được nội dung video: người đi checklist QC 8 (footage, người thật, gameplay đúng với game, preview Apple).", "");
  return L.join("\n");
}
