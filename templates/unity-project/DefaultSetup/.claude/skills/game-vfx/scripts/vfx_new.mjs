#!/usr/bin/env node
// Tao prefab VFX khung dung khuon cua quy chuan (7.1, 8.2, 8.4):
//
//   fx_<ten>              root: ParticleSystem khong phat hat, renderer tat, Stop Action = Callback
//   └─ containers         nhom: ParticleSystem khong phat hat, renderer tat, Stop Action = None
//      ├─ impact_add      lop: ParticleSystem + renderer bat, thiet lap theo 7.2
//      └─ glow_ab_sec     ...
//
// Chi doc / ghi file (Node 18+, khong can mo Unity). Unity tu import khi duoc focus.
//
//   node vfx_new.mjs <duong dan .prefab> [--layers impact_add,glow_ab,ring_add_2,glow_ab_sec]
//                    [--project <goc project>] [--dry-run]
//
// Luat (quy chuan 8.2, 8.4):
//   - Ten prefab: snake_case, bat dau fx_ (fx_hit_slash_01, fx_burst_impact_aoe_d).
//   - Ten lop: <vai>(_<vai>)*_<add|ab>(_<so>)?(_sec)?  vai lay tu ROLES ben duoi; bat buoc co hau to add / ab.
//   - Lop _add gan material _mat_add, lop _ab gan _mat_ab (tim theo ten trong Assets/, khong phan biet hoa thuong).
//     Trung ten: lay ban duoc prefab VFX tham chieu nhieu nhat. Khong co: tao moi (shader Mobile/Particles) o
//     <folder Materials canh folder Prefabs> neu prefab nam duoi mot folder ten Prefabs, khong thi
//     <folder chua folder prefab>/Materials.
//   - Da co file cung ten thi dung, khong ghi de.
//
// Mau YAML (templates/vfx_template.prefab, templates/_mat_*.mat) do Unity 6000.3 sinh ra. Doi thiet lap mac dinh cua
// lop thi dung lai mau trong Unity (root fx_template -> containers -> impact_add) roi chep de vao templates/.

import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";
import { fileURLToPath } from "node:url";

// Bang vai co dinh (quy chuan 8.4). Them vai moi: sua bang 8.4 truoc roi moi sua mang nay.
export const ROLES = [
  "impact", "glow", "flash", "ring", "shockwave", "wave", "fire", "smoke", "dust",
  "spark", "debris", "trail", "lightning", "halfsphere", "decal",
];
const BLEND_MATERIAL = { add: "_mat_add", ab: "_mat_ab" };
const DEFAULT_LAYERS = ["impact_add"];

const ROLE_RE = `(?:${ROLES.join("|")})`;
const LAYER_RE = new RegExp(`^${ROLE_RE}(?:_${ROLE_RE})*_(add|ab)(?:_[0-9]+)?(?:_sec)?$`);
const PREFAB_RE = /^fx(?:_[a-z0-9]+)+$/;

const HERE = path.dirname(fileURLToPath(import.meta.url));
const TEMPLATE_DIR = path.join(HERE, "templates");

// ------------------------------------------------------------------ tham so
function parseArgs(argv) {
  const out = { target: null, layers: null, project: null, dryRun: false };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === "--layers") out.layers = argv[++i];
    else if (a === "--project") out.project = argv[++i];
    else if (a === "--dry-run") out.dryRun = true;
    else if (a === "-h" || a === "--help") out.help = true;
    else if (!out.target) out.target = a;
    else throw new Error(`Tham so la: ${a}`);
  }
  return out;
}

const HELP = `node vfx_new.mjs <duong dan .prefab> [--layers a_add,b_ab] [--project <goc>] [--dry-run]
  Vai cho phep: ${ROLES.join(", ")}
  Ten lop: <vai>(_<vai>)*_<add|ab>(_<so>)?(_sec)?   vd impact_add, fire_impact_ab, ring_add_2, glow_ab_sec
  Khong truyen --layers thi khung co mot lop: ${DEFAULT_LAYERS.join(", ")}`;

// ------------------------------------------------------------------ kiem ten
export function checkLayerName(name) {
  if (!LAYER_RE.test(name)) {
    const parts = name.split("_");
    const bad = parts.filter((p) => !ROLES.includes(p) && !/^(add|ab|sec|[0-9]+)$/.test(p));
    if (!/_(add|ab)(_[0-9]+)?(_sec)?$/.test(name))
      return `"${name}": thieu hau to _add / _ab (quy chuan 8.4: moi lop phai noi kieu tron)`;
    if (bad.length) return `"${name}": vai khong co trong bang 8.4: ${bad.join(", ")}`;
    return `"${name}": sai ngu phap <vai>(_<vai>)*_<add|ab>(_<so>)?(_sec)?`;
  }
  return null;
}

function blendOf(layer) {
  return LAYER_RE.exec(layer)[1];
}

// ------------------------------------------------------------------ project
// Goc project = folder co du Assets/, ProjectSettings/ProjectVersion.txt va Packages/manifest.json. Chi Assets/ +
// ProjectSettings/ la chua du: co project mang ban chep lac ca hai folder do vao trong Assets/.
function findProjectRoot(fromDir) {
  let d = path.resolve(fromDir);
  for (;;) {
    if (fs.existsSync(path.join(d, "Assets")) &&
        fs.existsSync(path.join(d, "ProjectSettings", "ProjectVersion.txt")) &&
        fs.existsSync(path.join(d, "Packages", "manifest.json"))) return d;
    const up = path.dirname(d);
    if (up === d) return null;
    d = up;
  }
}

// Duyet Assets/ nhu Unity: bo folder bat dau bang "." va ket thuc bang "~".
function* walk(dir) {
  let entries;
  try {
    entries = fs.readdirSync(dir, { withFileTypes: true });
  } catch {
    return;
  }
  for (const e of entries) {
    if (e.name.startsWith(".") || e.name.endsWith("~")) continue;
    const p = path.join(dir, e.name);
    if (e.isDirectory()) yield* walk(p);
    else yield p;
  }
}

function readGuid(metaPath) {
  try {
    const m = /^guid:\s*([0-9a-f]{32})/m.exec(fs.readFileSync(metaPath, "utf8"));
    return m ? m[1] : null;
  } catch {
    return null;
  }
}

function newGuid() {
  return crypto.randomBytes(16).toString("hex");
}

// ------------------------------------------------------------------ material
// Tim material theo ten (khong phan biet hoa thuong). Trung ten: ban duoc nhieu prefab VFX tham chieu nhat.
function resolveMaterials(assetsDir, wanted) {
  const found = Object.fromEntries(wanted.map((w) => [w, []]));
  const prefabs = [];
  for (const f of walk(assetsDir)) {
    const lower = path.basename(f).toLowerCase();
    for (const w of wanted) if (lower === `${w}.mat`) {
      const guid = readGuid(`${f}.meta`);
      if (guid) found[w].push({ path: f, guid, refs: 0 });
    }
    if (lower.endsWith(".prefab")) prefabs.push(f);
  }
  const needCount = wanted.filter((w) => found[w].length > 1);
  if (needCount.length) {
    for (const p of prefabs) {
      let text;
      try {
        text = fs.readFileSync(p, "utf8");
      } catch {
        continue;
      }
      if (!text.includes("--- !u!198 ")) continue; // chi tinh prefab co ParticleSystem (prefab VFX)
      for (const w of needCount) for (const c of found[w]) if (text.includes(`guid: ${c.guid}`)) c.refs++;
    }
  }
  const result = {};
  for (const w of wanted) {
    const list = found[w].sort((a, b) => b.refs - a.refs || a.path.length - b.path.length || a.path.localeCompare(b.path));
    result[w] = list.length ? { ...list[0], candidates: list } : null;
  }
  return result;
}

function materialsDirFor(prefabPath, assetsDir) {
  // Prefab nam duoi mot folder ten Prefabs -> folder Materials canh no (vd VFX/Prefabs/BurstGloves/x.prefab -> VFX/Materials).
  let d = path.dirname(prefabPath);
  while (d.startsWith(assetsDir) && d !== assetsDir) {
    if (path.basename(d).toLowerCase() === "prefabs") return path.join(path.dirname(d), "Materials");
    d = path.dirname(d);
  }
  return path.join(path.dirname(path.dirname(prefabPath)), "Materials");
}

const MAT_META = (guid) =>
  `fileFormatVersion: 2\nguid: ${guid}\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 2100000\n` +
  `  userData: \n  assetBundleName: \n  assetBundleVariant: \n`;
const PREFAB_META = (guid) =>
  `fileFormatVersion: 2\nguid: ${guid}\nPrefabImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n` +
  `  assetBundleVariant: \n`;

// ------------------------------------------------------------------ YAML mau
function parseTemplate(text) {
  const parts = text.split(/^(--- !u!\d+ &-?\d+.*)$/m);
  const head = parts[0];
  const docs = [];
  for (let i = 1; i < parts.length; i += 2) {
    const m = /^--- !u!(\d+) &(-?\d+)/.exec(parts[i]);
    docs.push({ cls: Number(m[1]), id: m[2], text: parts[i] + parts[i + 1] });
  }
  const byId = new Map(docs.map((d) => [d.id, d]));
  const nodes = {};
  for (const d of docs) if (d.cls === 1) {
    const name = /^\s*m_Name:\s*(.*)$/m.exec(d.text)[1].trim();
    const comps = [...d.text.matchAll(/component:\s*\{fileID:\s*(-?\d+)\}/g)].map((m) => m[1]);
    nodes[name] = { go: d.id, ids: [d.id, ...comps], docs: [d, ...comps.map((c) => byId.get(c))] };
  }
  for (const want of ["fx_template", "containers", "impact_add"])
    if (!nodes[want]) throw new Error(`Mau thieu node "${want}" (templates/vfx_template.prefab)`);
  return { head, nodes };
}

function makeIdFactory() {
  const used = new Set();
  return () => {
    for (;;) {
      // fileID kieu Unity: so nguyen duong ngau nhien 64 bit
      const id = (crypto.randomBytes(8).readBigUInt64BE() >> 1n).toString();
      if (id.length >= 15 && !used.has(id)) {
        used.add(id);
        return id;
      }
    }
  };
}

// Nhan ban mot node voi fileID moi; tra ve docs da doi ten + id cua Transform / Renderer.
function cloneNode(node, name, newId) {
  const map = new Map(node.ids.map((id) => [id, newId()]));
  const re = new RegExp(`(&|fileID:\\s*)(${node.ids.map((i) => i.replace("-", "\\-")).join("|")})\\b`, "g");
  const docs = node.docs.map((d) => ({ cls: d.cls, text: d.text.replace(re, (_, p, id) => p + map.get(id)) }));
  const go = docs.find((d) => d.cls === 1);
  go.text = go.text.replace(/^(\s*m_Name:).*$/m, `$1 ${name}`);
  const idOf = (cls) => {
    const d = docs.find((x) => x.cls === cls);
    return d ? /^--- !u!\d+ &(-?\d+)/.exec(d.text)[1] : null;
  };
  return { docs, tf: idOf(4), renderer: idOf(199) };
}

function setHierarchy(node, fatherId, childIds) {
  const tf = node.docs.find((d) => d.cls === 4);
  const children = childIds.length ? "\n" + childIds.map((c) => `  - {fileID: ${c}}`).join("\n") : " []";
  tf.text = tf.text
    .replace(/^  m_Children:(?: \[\])?(?:\n  - \{fileID: -?\d+\})*/m, `  m_Children:${children}`)
    .replace(/^  m_Father: \{fileID: -?\d+\}/m, `  m_Father: {fileID: ${fatherId}}`);
}

function setMaterial(node, guid) {
  const r = node.docs.find((d) => d.cls === 199);
  r.text = r.text.replace(/^(  m_Materials:\n)  - \{[^}]*\}/m, `$1  - {fileID: 2100000, guid: ${guid}, type: 2}`);
}

// ------------------------------------------------------------------ main
function main() {
  const args = parseArgs(process.argv.slice(2));
  if (args.help || !args.target) {
    console.log(HELP);
    return args.help ? 0 : 1;
  }
  const target = path.resolve(args.target);
  const errors = [];

  if (path.extname(target) !== ".prefab") errors.push(`Duong dan phai ket thuc bang .prefab: ${target}`);
  const prefabName = path.basename(target, ".prefab");
  if (!PREFAB_RE.test(prefabName))
    errors.push(`Ten prefab "${prefabName}" sai quy chuan 8.2: snake_case chu thuong, bat dau fx_ (vd fx_hit_slash_01)`);

  const layers = (args.layers ? args.layers.split(",") : DEFAULT_LAYERS).map((s) => s.trim()).filter(Boolean);
  if (!layers.length) errors.push("Danh sach lop rong");
  for (const l of layers) {
    const e = checkLayerName(l);
    if (e) errors.push(e);
  }
  const dup = layers.filter((l, i) => layers.indexOf(l) !== i);
  if (dup.length) errors.push(`Lop trung ten: ${[...new Set(dup)].join(", ")} (trung vai thi danh so _2, _3)`);

  const root = args.project ? path.resolve(args.project) : findProjectRoot(path.dirname(target));
  if (!root) errors.push("Khong tim thay goc project Unity (folder co Assets/ va ProjectSettings/); truyen --project");
  const assetsDir = root ? path.join(root, "Assets") : null;
  if (assetsDir && !path.resolve(target).startsWith(assetsDir + path.sep))
    errors.push(`Prefab phai nam trong ${assetsDir}`);
  if (fs.existsSync(target) || fs.existsSync(`${target}.meta`))
    errors.push(`Da co ${target}: khong ghi de. Doi ten hoac xoa ban cu truoc.`);

  if (errors.length) {
    console.error("KHONG TAO DUOC:\n  - " + errors.join("\n  - "));
    return 1;
  }

  // Material can dung
  const neededBlends = [...new Set(layers.map(blendOf))];
  const wanted = neededBlends.map((b) => BLEND_MATERIAL[b]);
  const mats = resolveMaterials(assetsDir, wanted);
  const plan = [];
  const matDir = materialsDirFor(target, assetsDir);
  for (const w of wanted) {
    if (mats[w]) {
      const note = mats[w].candidates.length > 1
        ? ` (trung ten ${mats[w].candidates.length} ban; chon ban duoc ${mats[w].refs} prefab VFX dung: ` +
          mats[w].candidates.map((c) => `${path.relative(root, c.path)}=${c.refs}`).join(", ") + ")"
        : "";
      plan.push(`dung  ${path.relative(root, mats[w].path)}${note}`);
    } else {
      const p = path.join(matDir, `${w}.mat`);
      mats[w] = { path: p, guid: newGuid(), create: true };
      plan.push(`TAO   ${path.relative(root, p)} (shader Mobile/Particles, chua co ${w} trong Assets/)`);
    }
  }

  // Dung YAML
  const tpl = parseTemplate(fs.readFileSync(path.join(TEMPLATE_DIR, "vfx_template.prefab"), "utf8"));
  const newId = makeIdFactory();
  const rootNode = cloneNode(tpl.nodes.fx_template, prefabName, newId);
  const contNode = cloneNode(tpl.nodes.containers, "containers", newId);
  const layerNodes = layers.map((l) => {
    const n = cloneNode(tpl.nodes.impact_add, l, newId);
    setMaterial(n, mats[BLEND_MATERIAL[blendOf(l)]].guid);
    return n;
  });
  setHierarchy(rootNode, 0, [contNode.tf]);
  setHierarchy(contNode, rootNode.tf, layerNodes.map((n) => n.tf));
  for (const n of layerNodes) setHierarchy(n, contNode.tf, []);
  const yaml = tpl.head + [rootNode, contNode, ...layerNodes].flatMap((n) => n.docs.map((d) => d.text)).join("");

  console.log(`${args.dryRun ? "[dry-run] " : ""}Prefab: ${path.relative(root, target)}`);
  console.log(`  ${prefabName}  (root, Stop Action Callback)\n  └─ containers`);
  layers.forEach((l, i) => console.log(`     ${i === layers.length - 1 ? "└" : "├"}─ ${l}  -> ${BLEND_MATERIAL[blendOf(l)]}`));
  console.log("Material:\n  " + plan.join("\n  "));
  if (args.dryRun) return 0;

  for (const w of wanted) if (mats[w].create) {
    fs.mkdirSync(path.dirname(mats[w].path), { recursive: true });
    const matText = fs.readFileSync(path.join(TEMPLATE_DIR, `${w}.mat`), "utf8")
      .replace(/^(\s*m_Name:).*$/m, `$1 ${w}`);
    fs.writeFileSync(mats[w].path, matText);
    fs.writeFileSync(`${mats[w].path}.meta`, MAT_META(mats[w].guid));
  }
  fs.mkdirSync(path.dirname(target), { recursive: true });
  fs.writeFileSync(target, yaml);
  fs.writeFileSync(`${target}.meta`, PREFAB_META(newGuid()));

  console.log(`Da ghi. Unity import khi duoc focus. Viec tiep theo (quy chuan):
  - root Duration = do dai ca hieu ung (7.1); Max Particles moi lop = so hat that x 1,2 (7.2)
  - Sorting layer theo bang cua game, khong de Default (7.3)
  - Gan texture cho material / material rieng (8.2); lop phu danh dau _sec (2.2, 6.4)
  - Hieu ung va cham: motion theo 3.7; kiem bang mat (SKILL.md muc 5)`);
  return 0;
}

try {
  process.exitCode = main();
} catch (e) {
  console.error("LOI:", e.message);
  process.exitCode = 1;
}
