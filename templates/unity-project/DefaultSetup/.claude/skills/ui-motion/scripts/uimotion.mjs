#!/usr/bin/env node
// CLI của skill ui-motion. Chỉ đọc / ghi file, không cần mở Unity, không cần cài gói (Node 18+).
//
//   node uimotion.mjs survey        [--project <gốc>] [--out <folder>] [--json]
//   node uimotion.mjs init          [--project <gốc>] [--force]
//   node uimotion.mjs check-rules   [--project <gốc>] [--file <json>]
//   node uimotion.mjs install-module --to <Assets/…/UIMotion> [--project <gốc>] [--from <folder module>] [--data-to <Assets/…>]
//   node uimotion.mjs install-skill [--to <folder>]
//   node uimotion.mjs selftest
//
// survey: báo cáo Logs/UIMotionSurvey.md (+ .json), in tóm tắt. init: tạo ProjectSettings/UIMotionProject.json (luật) và
// ProjectSettings/UIMotionProject.md (ghi chú) nếu chưa có — không bao giờ ghi đè (--force ghi bản .new bên cạnh).
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { exists, findProjectRoot, readText, writeText } from "./lib/fsutil.mjs";
import { survey } from "./lib/survey.mjs";
import { projectNotes, rulesFromSurvey, summary, surveyReport } from "./lib/writers.mjs";
import { FALLBACK_ROLES, rolesFromSource, validateRules } from "./lib/rules.mjs";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const SKILL_DIR = path.resolve(HERE, "..");
const args = process.argv.slice(2);
const cmd = args[0];
const opt = (name, def = null) => {
  const i = args.indexOf(name);
  return i >= 0 && i + 1 < args.length && !args[i + 1].startsWith("--") ? args[i + 1] : def;
};
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

/** Folder module cài kèm skill: skill nằm trong <module>/Skill~/ui-motion, hoặc install.json ghi lúc cài. */
function moduleSource() {
  const inside = path.resolve(SKILL_DIR, "..", "..");
  if (exists(path.join(inside, "UIMotionDefaults.cs"))) return inside;
  const info = readText(path.join(SKILL_DIR, "install.json"));
  if (info) {
    try {
      const src = JSON.parse(info).moduleSource;
      if (src && exists(path.join(src, "UIMotionDefaults.cs"))) return src;
    } catch {
      /* install.json hỏng: coi như không có */
    }
  }
  return null;
}

function runSurvey(root) {
  const s = survey({ root, moduleSource: moduleSource() });
  const outDir = opt("--out") ? path.resolve(opt("--out")) : path.join(root, "Logs");
  writeText(path.join(outDir, "UIMotionSurvey.md"), surveyReport(s));
  writeText(path.join(outDir, "UIMotionSurvey.json"), JSON.stringify(s, null, 2));
  return { s, outDir };
}

function cmdSurvey() {
  const root = projectRoot();
  const { s, outDir } = runSurvey(root);
  if (flag("--json")) console.log(JSON.stringify(s, null, 2));
  else console.log(summary(s) + `\nBáo cáo: ${path.join(outDir, "UIMotionSurvey.md")}`);
}

function cmdInit() {
  const root = projectRoot();
  const { s, outDir } = runSurvey(root);
  const rules = rulesFromSurvey(s);
  const rulesFile = path.join(root, "ProjectSettings", "UIMotionProject.json");
  const notesFile = path.join(root, "ProjectSettings", "UIMotionProject.md");
  const written = [];
  const write = (file, text) => {
    if (exists(file)) {
      if (!flag("--force")) return console.log(`Đã có ${path.relative(root, file)} — không ghi đè (thêm --force để ghi bản .new bên cạnh mà so).`);
      file += ".new";
    }
    writeText(file, text);
    written.push(path.relative(root, file));
  };
  write(rulesFile, JSON.stringify(rules, null, 2) + "\n");
  write(notesFile, projectNotes(s, rules, today()));
  console.log(summary(s));
  if (written.length) console.log("Đã ghi: " + written.join(", "));
  const pending = s.candidates.ownMotion.filter((c) => c.confidence !== "cao" && !c.coveredByParent && !c.inRoleMap).length + s.candidates.clickable.filter((c) => c.confidence !== "cao" && !c.coveredByParent).length;
  console.log(`Luật đã ghi: ${rules.componentRules.length} class. Cần xác nhận: ${pending} (mục "Cần xác nhận" trong UIMotionProject.md). Báo cáo đủ: ${path.join(outDir, "UIMotionSurvey.md")}`);
  console.log("Bước sau: xác nhận với người giữ project, sửa file luật, rồi trong Unity bấm Tools/UI Motion/Áp luật riêng của project.");
}

function cmdCheckRules() {
  const root = opt("--file") ? null : projectRoot();
  const file = opt("--file") ? path.resolve(opt("--file")) : path.join(root, "ProjectSettings", "UIMotionProject.json");
  const text = readText(file);
  if (text == null) die(`Không có ${file}.`);
  let roles = FALLBACK_ROLES;
  if (root) {
    const s = survey({ root, moduleSource: moduleSource() });
    const src = s.module ? path.join(root, s.module.path) : moduleSource();
    if (src) roles = rolesFromSource(readText(path.join(src, "Roles", "UIMotionRoleTypes.cs"))) || roles;
  } else if (moduleSource()) {
    roles = rolesFromSource(readText(path.join(moduleSource(), "Roles", "UIMotionRoleTypes.cs"))) || roles;
  }
  const { errors, warnings } = validateRules(text, roles);
  for (const w of warnings) console.log("cảnh báo: " + w);
  for (const e of errors) console.log("LỖI: " + e);
  console.log(errors.length ? `${file}: ${errors.length} lỗi` : `${file}: hợp lệ` + (warnings.length ? ` (${warnings.length} cảnh báo)` : ""));
  process.exit(errors.length ? 1 : 0);
}

function copyDir(from, to, skip = () => false) {
  fs.mkdirSync(to, { recursive: true });
  for (const e of fs.readdirSync(from, { withFileTypes: true })) {
    const a = path.join(from, e.name);
    const b = path.join(to, e.name);
    if (skip(a, e)) continue;
    if (e.isDirectory()) copyDir(a, b, skip);
    else fs.copyFileSync(a, b);
  }
}

function cmdInstallModule() {
  const root = projectRoot();
  const from = opt("--from") ? path.resolve(opt("--from")) : moduleSource();
  if (!from || !exists(path.join(from, "UIMotionDefaults.cs"))) die("Không biết lấy module ở đâu: truyền --from <folder module UIMotion (có UIMotionDefaults.cs)>.");
  const s = survey({ root, moduleSource: from });
  const target = opt("--to") || s.module?.path;
  if (!target) die("Project chưa có module: truyền --to <Assets/…/UIMotion> (chỗ đặt module, hỏi người giữ project).");
  if (!/^Assets\//.test(target)) die("--to phải là đường dẫn trong project, bắt đầu bằng Assets/.");
  const dest = path.join(root, target);
  if (path.resolve(dest) === path.resolve(from)) die("Nguồn và đích là một folder.");
  const version = (/const string Version = "([^"]+)"/.exec(readText(path.join(from, "UIMotionDefaults.cs")) || "") || [])[1];
  if (exists(path.join(root, "Temp", "UnityLockfile"))) console.log("Lưu ý: Unity đang mở project này — chép xong, focus lại Editor để nó import (không cần tắt).");

  // Bản cũ để asset của project trong folder module: dời ra trước (giữ .meta = giữ GUID), rồi mới thay folder.
  const inside = s.data.insideModule.filter((r) => r.startsWith(target + "/"));
  if (inside.length) {
    const rulesData = s.projectFiles.rulesData;
    const dataTo = opt("--data-to") || rulesData?.dataFolder || "Assets/Resources/UIMotion";
    const dataDir = path.join(root, /\/Resources\/UIMotion$/.test(dataTo) ? dataTo : dataTo + "/Resources/UIMotion");
    for (const r of inside) {
      const to = path.join(dataDir, path.basename(r));
      if (exists(to)) die(`Đích đã có ${path.relative(root, to)} — xử lý tay trước (không ghi đè asset của project).`);
    }
    const createdDir = !exists(dataDir);
    fs.mkdirSync(dataDir, { recursive: true });
    // Folder mới: mang .meta của folder cũ theo (giữ GUID), không để Unity sinh GUID khác nhau trên mỗi máy.
    const oldDirMeta = path.join(root, path.posix.dirname(inside[0])) + ".meta";
    if (createdDir && exists(oldDirMeta) && !exists(dataDir + ".meta")) fs.copyFileSync(oldDirMeta, dataDir + ".meta");
    for (const r of inside) {
      const a = path.join(root, r);
      const b = path.join(dataDir, path.basename(r));
      fs.renameSync(a, b);
      if (exists(a + ".meta")) fs.renameSync(a + ".meta", b + ".meta");
    }
    console.log(`Đã dời ${inside.length} asset của project ra ${path.relative(root, dataDir)} (kèm .meta).`);
  }

  const old = s.module ? s.module.version : null;
  if (exists(dest)) fs.rmSync(dest, { recursive: true, force: true });
  copyDir(from, dest, (a, e) => e.isDirectory() && e.name === "Resources" && path.dirname(a) === from);
  const metaOfDest = dest + ".meta";
  if (!exists(metaOfDest) && exists(from + ".meta")) fs.copyFileSync(from + ".meta", metaOfDest);
  console.log(`Module ${version} → ${target}` + (old ? ` (thay bản ${old})` : ""));
  const others = s.traps.filter((t) => t.kind === "odin-stubs");
  for (const t of others) console.log("Lưu ý: " + t.detail);
  console.log("Bước sau (trong Unity): mở project cho define tự bật → Tools/UI Motion/Tạo asset mẫu → Áp luật riêng của project → Cập nhật RoleMap theo bản module mới (nếu nâng bản) → Quét và gắn (đọc báo cáo trước) → Kiểm tra (gate).");
}

function cmdInstallSkill() {
  const to = path.resolve(opt("--to") || path.join(os.homedir(), ".claude", "skills", "ui-motion"));
  if (to === SKILL_DIR) die("Skill đang chạy từ chính folder đích.");
  if (exists(to)) fs.rmSync(to, { recursive: true, force: true });
  copyDir(SKILL_DIR, to, (a, e) => (e.isDirectory() && (e.name === "results" || e.name === "node_modules")) || e.name === "install.json");
  const mod = moduleSource();
  const version = mod ? (/const string Version = "([^"]+)"/.exec(readText(path.join(mod, "UIMotionDefaults.cs")) || "") || [])[1] : null;
  writeText(path.join(to, "install.json"), JSON.stringify({ moduleSource: mod, moduleVersion: version, from: SKILL_DIR, installedAt: new Date().toISOString() }, null, 2) + "\n");
  console.log(`Đã cài skill ui-motion vào ${to}` + (mod ? ` (module nguồn ${mod}, bản ${version})` : " (không biết module nguồn — install-module cần --from)"));
}

async function cmdSelftest() {
  const { makeFixture } = await import("./tests/make_fixture.mjs");
  const { runSelftest } = await import("./tests/selftest.mjs");
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "uimotion-selftest-"));
  try {
    const ok = runSelftest({ dir, makeFixture, moduleSource: moduleSource(), survey, rulesFromSurvey, projectNotes, surveyReport, validateRules, rolesFromSource });
    process.exitCode = ok ? 0 : 1;
  } finally {
    if (!flag("--keep")) fs.rmSync(dir, { recursive: true, force: true });
    else console.log("giữ lại: " + dir);
  }
}

const COMMANDS = { survey: cmdSurvey, init: cmdInit, "check-rules": cmdCheckRules, "install-module": cmdInstallModule, "install-skill": cmdInstallSkill, selftest: cmdSelftest };
if (!COMMANDS[cmd]) {
  console.log(fs.readFileSync(fileURLToPath(import.meta.url), "utf8").split("\n").slice(1, 14).map((l) => l.replace(/^\/\/ ?/, "")).join("\n"));
  process.exit(cmd ? 2 : 0);
}
await COMMANDS[cmd]();
