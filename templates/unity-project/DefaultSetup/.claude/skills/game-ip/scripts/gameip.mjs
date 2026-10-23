#!/usr/bin/env node
// CLI của skill game-ip. Chỉ đọc / ghi file, không cần mở Unity, không cần cài gói (Node 18+).
//
//   node gameip.mjs survey [--project <gốc>] [--out <folder>] [--asset-store-cache <folder>]… [--no-default-cache]
//   node gameip.mjs init   [--project <gốc>] [--out <folder>] [--merge] [--asset-store-cache <folder>]… [--no-default-cache]
//   node gameip.mjs check  [--project <gốc>] [--settings <folder>] [--out <folder>] [--strict] [--fails-only] [cache…]
//   node gameip.mjs notices [--project <gốc>] [--settings <folder>] [--to <file>]
//   node gameip.mjs compare <phần> --ref "<game tham chiếu>" [--genre <thể loại>] [--mine <file|folder>]… [--theirs <file|folder>]… [--brief "<lời>"] [--threshold 10]
//   node gameip.mjs name "<tên game>" [--markets VN,US,EU,…] [--save]
//   node gameip.mjs creative <folder creative> [--init] [--name "<tên>"]
//   node gameip.mjs selftest [--keep]
//
// survey: chỉ đọc, ghi báo cáo Logs/GameIPSurvey.md (+ .json) của project, hoặc vào --out.
// init: survey, rồi tạo ProjectSettings/GameIPProject.json, GameIPProject.md (nếu chưa có, không ghi đè) và sổ
// ProjectSettings/GameIPLedger.json gồm các mục draft. Sổ đã có: ghi GameIPLedger.new.json bên cạnh (mục cũ giữ nguyên, thêm mục
// mới); --merge ghi thẳng vào sổ. --out: ghi mọi thứ vào folder đó (ProjectSettings/, Logs/) thay vì project, để chạy chỉ đọc.
// check: chạy gate máy kiểm được (QC 4) trên sổ, in checklist ✓ ✗ ! · –, ghi Logs/GameIPCheck.md; mã thoát 1 khi có ✗ (--strict: cả !).
// notices: sinh nội dung màn Credits / Licenses từ sổ, ghi vào credits.noticesFile của GameIPProject.json, --to, hoặc Logs/.
// notices không ghi gì khi chưa có sổ.
// compare: thẻ so sánh (QC 6) điền sẵn án lệ trùng ở NL 5.4, ghi Docs/GameIP/compare/<phần>.md (có rồi thì .new.md), và cặp ảnh
// giống nhau theo pHash giữa --mine và --theirs (PNG, JPEG baseline) ở <phần>.pairs.md. Không cần project Unity.
// name: chấm tên game (độ mạnh, gần tên game lớn / đối thủ / thương hiệu, giới hạn 30 ký tự), link tra nhãn hiệu theo thị trường;
// in ra, --save ghi Docs/GameIP/names/<tên>.md. Không gửi tên đi đâu.
// creative: kiểm một folder creative theo sổ creative.json trong folder (--init sinh sổ nháp nếu chưa có); ghi GameIPCreative.md
// vào folder (hoặc --out); mã thoát 1 khi có ✗.
//
// Cờ chung:
//   --project <gốc>             gốc project Unity (mặc định: đi lên từ thư mục hiện tại)
//   --out <folder>              ghi báo cáo (Logs/) và file init (ProjectSettings/) vào folder này thay vì project: chạy không đụng project
//   --settings <folder>         chỗ đọc GameIPProject.json, GameIPLedger.json, GameIPDebt.json (mặc định <project>/ProjectSettings)
//   --asset-store-cache <dir>   thêm folder cache Asset Store (lặp được); mặc định đọc cache của máy (%APPDATA%/Unity/Asset Store-5.x …)
//   --no-default-cache          không đọc cache của máy
//   --quiet                     không in tiến độ
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { exists, findProjectRoot, readText, writeText } from "./lib/fsutil.mjs";
import { LEDGER_FILE, candidatesFromSurvey, loadLedger, mergeLedger } from "./lib/ledger.mjs";
import { projectNotes, projectTemplate, surveyReport } from "./lib/report.mjs";
import { buildNotices, checkReport, runGates } from "./lib/gates.mjs";
import { VERSION, survey } from "./lib/survey.mjs";
import { casesByGenre, compareCard, compareImages, matchCases, pairsReport, slug } from "./lib/compare.mjs";
import { assessName, nameReport } from "./lib/name.mjs";
import { CREATIVE_FILE, checkCreative, creativeReport, draftCreative } from "./lib/creative.mjs";

const args = process.argv.slice(2);
const cmd = args[0];
const opt = (name, def = null) => {
  const i = args.indexOf(name);
  return i >= 0 && i + 1 < args.length && !args[i + 1].startsWith("--") ? args[i + 1] : def;
};
const optAll = (name) => args.flatMap((a, i) => (a === name && i + 1 < args.length ? [args[i + 1]] : []));
const flag = (name) => args.includes(name);
const today = () => new Date().toISOString().slice(0, 10);

function die(msg, code = 2) {
  console.error(msg);
  process.exit(code);
}

function projectRoot() {
  const p = opt("--project");
  const root = findProjectRoot(p || process.cwd());
  if (!root) die(`Không thấy project Unity (cần ProjectSettings/ProjectVersion.txt và Assets/) từ ${p || process.cwd()}. Truyền --project <gốc project>.`);
  return root;
}

/** Survey; ghi báo cáo vào <outBase>/Logs khi outBase khác null. */
async function runSurvey(root, outBase) {
  const s = await survey({
    root,
    settingsDir: opt("--settings") ? path.resolve(opt("--settings")) : null,
    assetStoreCaches: optAll("--asset-store-cache").map((d) => path.resolve(d)),
    useDefaultCache: !flag("--no-default-cache"),
    log: flag("--quiet") ? () => {} : (m) => console.error(m),
  });
  if (!outBase) return { s, logs: null };
  const logs = path.join(outBase, "Logs");
  writeText(path.join(logs, "GameIPSurvey.md"), surveyReport(s));
  writeText(path.join(logs, "GameIPSurvey.json"), JSON.stringify(s, null, 2));
  return { s, logs };
}

function printSummary(s, logs) {
  const c = s.counts;
  console.log(`Khảo sát xong trong ${s.seconds} giây: ${c.files} file, ${c.inBuild} asset vào build.`);
  console.log(`  Pack Asset Store khớp GUID: ${s.assetStore.filter((a) => !a.error).length} · folder bên thứ ba: ${s.vendorFolders.length} · package: ${s.packages.length} · font: ${s.fonts.length}`);
  console.log(`  Âm thanh có tag nguồn khác: ${s.audio.tagged.filter((a) => a.sourceLike && !a.own).length} · ảnh metadata bên khác: ${s.images.foreign.length} · ảnh dấu hiệu AI: ${s.images.ai.length}`);
  console.log(`  Tên file đáng ngờ: ${s.suspicious.filter((x) => !x.vendor).length} · code dịch ngược: ${s.decompiled.length} · file cài đặt: ${s.installers.length} · tên game khác: ${s.gameNames.length}`);
  console.log(`  Yêu cầu ghi công: ${s.creditDemands.length} · màn Credits: ${s.credits.found ? "có dấu vết" : "không thấy"}`);
  if (s.coverage) console.log(`  Sổ phủ ${s.coverage.percentCovered}% asset vào build (${s.coverage.confirmed} xác nhận, ${s.coverage.draft} draft, ${s.coverage.uncovered} chưa có nguồn).`);
  console.log(`Báo cáo: ${path.join(logs, "GameIPSurvey.md")}`);
}

async function cmdSurvey() {
  const root = projectRoot();
  const outBase = opt("--out") ? path.resolve(opt("--out")) : root;
  const { s, logs } = await runSurvey(root, outBase);
  printSummary(s, logs);
}

async function cmdInit() {
  const root = projectRoot();
  const outBase = opt("--out") ? path.resolve(opt("--out")) : root;
  const { s, logs } = await runSurvey(root, outBase);
  const ps = path.join(outBase, "ProjectSettings");
  const projFile = path.join(ps, "GameIPProject.json");
  const notesFile = path.join(ps, "GameIPProject.md");
  const ledgerFile = path.join(outBase, LEDGER_FILE);
  const written = [];

  const existing = loadLedger(outBase);
  const index = s.index;
  const candidates = candidatesFromSurvey(s);
  const { ledger, added } = mergeLedger(existing, candidates, s.inBuildList, index.byPath);
  ledger.generator = existing ? existing.generator : `game-ip init ${VERSION} (${today()})`;
  let target = ledgerFile;
  if (existing && !flag("--merge")) target = ledgerFile.replace(/\.json$/, ".new.json");
  if (!existing || added.length) {
    writeText(target, `${JSON.stringify(ledger, null, 2)}\n`);
    written.push(`${target} (${existing ? `thêm ${added.length} mục mới` : `${ledger.entries.length} mục draft`})`);
  }
  if (!exists(projFile)) {
    writeText(projFile, `${JSON.stringify(projectTemplate(s), null, 2)}\n`);
    written.push(projFile);
  }
  if (!exists(notesFile)) {
    writeText(notesFile, projectNotes(s, existing ? added.length : ledger.entries.length, today()));
    written.push(notesFile);
  }
  printSummary(s, logs);
  console.log(written.length ? `Đã ghi:\n  ${written.join("\n  ")}` : "Không có gì mới để ghi (sổ đã phủ mọi nguồn khảo sát thấy).");
  if (existing && !flag("--merge") && added.length) console.log("Sổ đã có nên mục mới ghi vào GameIPLedger.new.json; xem rồi chạy lại với --merge để gộp vào sổ.");
}

function loadJsonFile(file, def) {
  try {
    return JSON.parse(readText(file));
  } catch {
    return def;
  }
}

async function cmdCheck() {
  const root = projectRoot();
  const outBase = opt("--out") ? path.resolve(opt("--out")) : root;
  const settings = opt("--settings") ? path.resolve(opt("--settings")) : path.join(root, "ProjectSettings");
  const { s } = await runSurvey(root, outBase);
  const debts = loadJsonFile(path.join(settings, "GameIPDebt.json"), []);
  const nf = s.projectFile && s.projectFile.credits && s.projectFile.credits.noticesFile;
  const noticesText = nf ? readText(path.resolve(root, nf)) : null;
  const result = runGates(s, { debts: Array.isArray(debts) ? debts : (debts && debts.items) || [], noticesText });
  const md = checkReport(s, result, flag("--fails-only"));
  writeText(path.join(outBase, "Logs", "GameIPCheck.md"), md);
  writeText(path.join(outBase, "Logs", "GameIPCheck.json"), JSON.stringify(result, null, 2));
  console.log(md);
  console.log(`Báo cáo: ${path.join(outBase, "Logs", "GameIPCheck.md")}`);
  const bad = result.totals.fail > 0 || (flag("--strict") && result.totals.warn > 0);
  process.exit(bad ? 1 : 0);
}

async function cmdNotices() {
  const root = projectRoot();
  const settings = opt("--settings") ? path.resolve(opt("--settings")) : path.join(root, "ProjectSettings");
  if (!loadLedger(null, path.join(settings, "GameIPLedger.json"))) die(`Chưa có sổ ${path.join(settings, "GameIPLedger.json")}: chạy init trước. Không ghi gì.`);
  const { s } = await runSurvey(root, null);
  const { text, count } = buildNotices(s);
  const nf = s.projectFile && s.projectFile.credits && s.projectFile.credits.noticesFile;
  const target = opt("--to") ? path.resolve(opt("--to")) : nf ? path.resolve(root, nf) : path.join(opt("--out") ? path.resolve(opt("--out")) : root, "Logs", "ThirdPartyNotices.txt");
  writeText(target, text);
  console.log(`Ghi ${count} nguồn đòi ghi công vào ${target}`);
  if (!nf) console.log("Chưa khai credits.noticesFile trong GameIPProject.json: đặt file vào chỗ màn Credits của game đọc (vd Assets/…/Resources/ThirdPartyNotices.txt) rồi khai đường dẫn đó để check IP-6 đối chiếu.");
}

/** Project Unity nếu có (không bắt buộc với compare, name, creative), và file riêng của nó. */
function optionalProject() {
  const root = findProjectRoot(opt("--project") || process.cwd());
  const settings = opt("--settings") ? path.resolve(opt("--settings")) : root ? path.join(root, "ProjectSettings") : null;
  const pf = settings ? loadJsonFile(path.join(settings, "GameIPProject.json"), null) : null;
  return { root, pf };
}

function positional() {
  // đối số đầu tiên sau lệnh không phải cờ và không phải giá trị của cờ
  for (let i = 1; i < args.length; i++) {
    if (args[i].startsWith("--")) {
      if (i + 1 < args.length && !args[i + 1].startsWith("--") && !["--save", "--init", "--merge", "--strict", "--fails-only", "--quiet", "--keep", "--no-default-cache"].includes(args[i])) i++;
      continue;
    }
    return args[i];
  }
  return null;
}

async function cmdCompare() {
  const part = positional();
  const ref = opt("--ref");
  if (!part || !ref) die('Cần: compare <phần của mình> --ref "<game tham chiếu>"');
  const { root } = optionalProject();
  const base = opt("--out") ? path.resolve(opt("--out")) : root || process.cwd();
  const dir = path.join(base, "Docs", "GameIP", "compare");
  const name = slug(part);
  const mine = optAll("--mine");
  const theirs = optAll("--theirs");
  const threshold = Number(opt("--threshold", "10"));
  let pairsName = null;
  let closest = null;
  if (mine.length && theirs.length) {
    const result = compareImages({ mine, theirs, base: root || process.cwd(), threshold });
    pairsName = `${name}.pairs.md`;
    const rel = (f) => path.relative(root || process.cwd(), f).split(path.sep).join("/");
    const link = (f) => path.relative(dir, f).split(path.sep).join("/");
    writeText(path.join(dir, pairsName), pairsReport({ part, ref, result, threshold, rel, link }));
    console.log(`So ảnh: ${result.counts.mine} của mình × ${result.counts.theirs} tham chiếu → ${result.pairs.length} cặp ≤ ${threshold}; bỏ qua ${result.skipped.length} file.`);
    for (const p of result.pairs.slice(0, 10)) console.log(`  ${p.distance}${p.flipped ? " (lật)" : ""}  ${rel(p.mine)}  ↔  ${rel(p.theirs)}`);
    if (result.pairs.length) closest = { ...result.pairs[0], mine: rel(result.pairs[0].mine), theirs: rel(result.pairs[0].theirs) };
  } else if (mine.length || theirs.length) console.log("So ảnh cần cả --mine và --theirs.");
  const cases = matchCases(ref);
  for (const c of cases) console.log(`Án lệ trùng: ${c.case} (${c.place}, ${c.year}): ${c.lesson}`);
  const genreCases = !cases.length && opt("--genre") ? casesByGenre(opt("--genre")) : [];
  for (const c of genreCases) console.log(`Án lệ cùng thể loại (tham khảo): ${c.case} (${c.place}, ${c.year}): ${c.lesson}`);
  if (!cases.length && !opt("--genre")) console.log("Không vụ nào trùng tên game; thêm --genre <thể loại> (vd match3, merge, puzzle, shooter, rpg) để xem án lệ cùng thể loại.");
  let cardFile = path.join(dir, `${name}.md`);
  if (exists(cardFile)) cardFile = path.join(dir, `${name}.new.md`);
  writeText(cardFile, compareCard({ part, ref, date: today(), cases, pairsFile: pairsName, brief: opt("--brief"), genreCases, closest }));
  console.log(`Thẻ so sánh: ${cardFile}${cardFile.endsWith(".new.md") ? " (thẻ cũ giữ nguyên)" : ""}. Điền theo QC 6 cùng người làm.`);
}

async function cmdName() {
  const name = positional();
  if (!name) die('Cần: name "<tên game>"');
  const { root, pf } = optionalProject();
  const markets = opt("--markets") ? opt("--markets").split(",").map((x) => x.trim().toUpperCase()) : (pf && pf.markets) || undefined;
  const others = [...((pf && pf.referenceGames) || []).map((n) => ({ name: n, from: "game tham chiếu" })), ...((pf && pf.competitors) || []).map((n) => ({ name: n, from: "đối thủ" }))];
  const r = assessName(name, { markets: markets && markets.flatMap((m) => (m === "SEA" ? [] : [m])), others });
  const md = nameReport(r, today());
  console.log(md);
  if (flag("--save")) {
    const base = opt("--out") ? path.resolve(opt("--out")) : root || process.cwd();
    let file = path.join(base, "Docs", "GameIP", "names", `${slug(name)}.md`);
    if (exists(file)) file = file.replace(/\.md$/, ".new.md");
    writeText(file, md);
    console.log(`Ghi ${file}`);
  }
  process.exit(r.findings.some((f) => f.mark === "✗") ? 1 : 0);
}

async function cmdCreative() {
  const folder = positional();
  if (!folder) die("Cần: creative <folder creative>");
  const dir = path.resolve(folder);
  if (!exists(dir)) die(`Không có folder ${dir}`);
  const name = opt("--name") || path.basename(dir);
  const sheetFile = path.join(dir, CREATIVE_FILE);
  if (flag("--init")) {
    if (exists(sheetFile)) console.log(`${sheetFile} đã có, không ghi đè.`);
    else {
      writeText(sheetFile, `${JSON.stringify(draftCreative(dir, name), null, 2)}\n`);
      console.log(`Ghi sổ nháp ${sheetFile}: tách mục theo từng nguồn thật, điền license, scope.ads, networks, rồi chạy lại không có --init.`);
      return;
    }
  }
  const { pf } = optionalProject();
  const games = (readText(path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "data", "terms", "games.txt")) || "")
    .split(/\r?\n/)
    .map((l) => l.trim())
    .filter((l) => l && !l.startsWith("#"));
  const res = checkCreative(dir, {
    games: [...games, ...((pf && pf.competitors) || []), ...((pf && pf.referenceGames) || [])],
    brandsExtra: (pf && pf.brands) || [],
    ownNames: (pf && pf.ownNames) || [],
  });
  const md = creativeReport(dir, name, res, today());
  const out = opt("--out") ? path.join(path.resolve(opt("--out")), "GameIPCreative.md") : path.join(dir, "GameIPCreative.md");
  writeText(out, md);
  console.log(md);
  console.log(`Báo cáo: ${out}`);
  process.exit(res.totals.fail ? 1 : 0);
}

async function cmdSelftest() {
  const { runSelftest } = await import("./tests/selftest.mjs");
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "game-ip-selftest-"));
  let ok = false;
  try {
    ok = await runSelftest(dir);
  } finally {
    if (flag("--keep")) console.log(`Giữ project giả ở ${dir}`);
    else fs.rmSync(dir, { recursive: true, force: true });
  }
  process.exit(ok ? 0 : 1);
}

const commands = { survey: cmdSurvey, init: cmdInit, check: cmdCheck, notices: cmdNotices, compare: cmdCompare, name: cmdName, creative: cmdCreative, selftest: cmdSelftest };
if (!commands[cmd]) {
  // In nguyên khối chú thích đầu file (cách dùng, ý nghĩa từng cờ).
  const lines = (readText(new URL(import.meta.url)) || "").split(/\r?\n/).slice(1);
  const head = lines.slice(0, lines.findIndex((l) => !l.startsWith("//"))).map((l) => l.replace(/^\/\/ ?/, "")).join("\n");
  die(`Lệnh: survey | init | check | notices | compare | name | creative | selftest\n\n${head}`, cmd && cmd !== "--help" && cmd !== "help" ? 2 : 0);
}
commands[cmd]().catch((e) => die(e.stack || String(e)));
