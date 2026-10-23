// Chỉ mục file của project Unity (Assets, package nhúng trong Packages/, package trong Library/PackageCache) theo GUID, và danh sách
// asset "vào build" (QC 1): scene đang bật trong Build Settings, folder Resources, nhóm Addressables, asset có AssetBundle name,
// StreamingAssets, code và plugin không thuộc Editor, cộng mọi thứ các asset đó tham chiếu theo GUID (đi bắc cầu).
// Đường dẫn trong package cache được đổi về dạng Unity hiển thị: Packages/<tên package>/…
import fs from "node:fs";
import path from "node:path";
import { exists, readText, rel, walkFiles } from "./fsutil.mjs";

// Asset dạng text YAML của Unity: đọc để lấy tham chiếu GUID.
const YAML_EXT = /\.(unity|prefab|asset|mat|controller|overrideController|anim|playable|spriteatlas|spriteatlasv2|mask|preset|lighting|renderTexture|flare|guiskin|physicMaterial|physicsMaterial2D|shadervariants|terrainlayer|brush|signal|mixer|giparams|cubemap|fontsettings)$/i;
// Asset dạng JSON có GUID ("guid": "…"): shader graph, VFX graph, input actions.
const JSON_EXT = /\.(shadergraph|shadersubgraph|vfx|vfxoperator|vfxblock|inputactions)$/i;
const PLUGIN_EXT = /\.(dll|so|aar|jar|a|jslib|jspre|bundle|framework|xcframework|m|mm|swift)$/i;
const isEditorPath = (p) => /(^|\/)Editor(\/|$)/.test(p) || /(^|\/)Editor Default Resources(\/|$)/.test(p);

function readGuid(metaAbs) {
  try {
    const fd = fs.openSync(metaAbs, "r");
    const buf = Buffer.alloc(400);
    const n = fs.readSync(fd, buf, 0, 400, 0);
    fs.closeSync(fd);
    const head = buf.toString("utf8", 0, n);
    const g = head.match(/guid: ([0-9a-f]{32})/);
    return g ? { guid: g[1], folder: /folderAsset: yes/.test(head) } : null;
  } catch {
    return null;
  }
}

/** Package nhúng (Packages/<tên>/package.json) và package trong cache (Library/PackageCache/<tên>@<hash>). */
function listPackageDirs(root) {
  const out = [];
  const embedded = path.join(root, "Packages");
  for (const d of safeDirs(embedded)) {
    const pj = path.join(embedded, d, "package.json");
    if (exists(pj)) out.push({ name: readPackageName(pj) || d, dir: path.join(embedded, d), source: "embedded" });
  }
  const cache = path.join(root, "Library", "PackageCache");
  for (const d of safeDirs(cache)) {
    const pj = path.join(cache, d, "package.json");
    if (!exists(pj)) continue;
    const name = readPackageName(pj) || d.replace(/@.*$/, "");
    if (!out.some((p) => p.name === name)) out.push({ name, dir: path.join(cache, d), source: "cache" });
  }
  return out;
}

function safeDirs(dir) {
  try {
    return fs.readdirSync(dir, { withFileTypes: true }).filter((e) => e.isDirectory()).map((e) => e.name);
  } catch {
    return [];
  }
}

function readPackageName(pj) {
  try {
    return JSON.parse(readText(pj)).name || null;
  } catch {
    return null;
  }
}

/**
 * Chỉ mục project. Mỗi file: { path (dạng Unity), abs, guid, folder?, pkg? }. `byGuid`, `byPath`.
 * Folder có .meta cũng vào chỉ mục (folder: true) để tham chiếu tới folder (Addressables, SpriteAtlas) mở ra được.
 */
export function indexProject(root) {
  const byGuid = new Map();
  const byPath = new Map();
  const files = [];
  const add = (abs, unityPath, pkg) => {
    if (abs.endsWith(".meta")) return;
    const meta = readGuid(abs + ".meta");
    const f = { path: unityPath, abs, guid: meta ? meta.guid : null, pkg: pkg || null };
    files.push(f);
    byPath.set(unityPath, f);
    if (f.guid) byGuid.set(f.guid, f);
  };
  const addFolders = (dirAbs, toUnity) => {
    // .meta của folder: <tên folder>.meta nằm cạnh folder
    for (const d of safeDirs(dirAbs)) {
      const abs = path.join(dirAbs, d);
      const meta = readGuid(abs + ".meta");
      if (meta && meta.folder) byGuid.set(meta.guid, { path: toUnity(abs), abs, guid: meta.guid, folder: true });
      if (!d.startsWith(".") && !d.endsWith("~")) addFolders(abs, toUnity);
    }
  };
  const assets = path.join(root, "Assets");
  for (const abs of walkFiles(assets)) add(abs, rel(root, abs));
  addFolders(assets, (abs) => rel(root, abs));
  const packages = listPackageDirs(root);
  for (const p of packages) {
    const toUnity = (abs) => `Packages/${p.name}/${path.relative(p.dir, abs).split(path.sep).join("/")}`;
    for (const abs of walkFiles(p.dir)) add(abs, toUnity(abs), p.name);
    addFolders(p.dir, toUnity);
  }
  return { root, files, byGuid, byPath, packages };
}

/** Scene trong Build Settings: [{ path, guid, enabled }]. */
export function buildScenes(root) {
  const txt = readText(path.join(root, "ProjectSettings", "EditorBuildSettings.asset")) || "";
  const out = [];
  const re = /- enabled: (\d)\s+path: ([^\r\n]*)\s+guid: ([0-9a-f]{32})/g;
  let m;
  while ((m = re.exec(txt))) out.push({ enabled: m[1] === "1", path: m[2].trim(), guid: m[3] });
  return out;
}

/** asmdef chỉ cho Editor, hay asmdef test (chỉ build khi bật UNITY_INCLUDE_TESTS). */
function asmdefEditorOnly(abs) {
  try {
    const j = JSON.parse(readText(abs));
    const plats = j.includePlatforms || [];
    if (plats.length === 1 && plats[0] === "Editor") return true;
    if ((j.defineConstraints || []).includes("UNITY_INCLUDE_TESTS")) return true;
    return false;
  } catch {
    return false;
  }
}

/**
 * Tính tập asset vào build. Trả về Map path → lý do đầu tiên đưa nó vào ("scene", "resources", "addressables", "assetbundle",
 * "streaming", "code", "plugin", "ref:<path>"). Code: file .cs không trong folder Editor, không thuộc asmdef chỉ cho Editor / test.
 */
export function buildScope(index) {
  const { root, files, byGuid, byPath } = index;
  const inBuild = new Map();
  const queue = [];
  const take = (f, why) => {
    if (!f || inBuild.has(f.path)) return;
    if (f.folder) {
      // Tham chiếu tới folder: mọi file bên trong.
      const prefix = `${f.path}/`;
      for (const g of files) if (g.path.startsWith(prefix) && !inBuild.has(g.path) && !isEditorPath(g.path)) take(g, why);
      return;
    }
    inBuild.set(f.path, why);
    queue.push(f);
  };

  for (const s of buildScenes(root)) if (s.enabled) take(byGuid.get(s.guid) || byPath.get(s.path), "scene");
  for (const f of files) {
    if (isEditorPath(f.path)) continue;
    if (/(^|\/)Resources\//.test(f.path)) take(f, "resources");
    else if (f.path.startsWith("Assets/StreamingAssets/")) take(f, "streaming");
  }
  // Addressables: m_GUID trong các group
  for (const f of files) {
    if (!/^Assets\/AddressableAssetsData\/AssetGroups\/[^/]+\.asset$/.test(f.path)) continue;
    const txt = readText(f.abs) || "";
    const re = /m_GUID: ([0-9a-f]{32})/g;
    let m;
    while ((m = re.exec(txt))) take(byGuid.get(m[1]), "addressables");
  }
  // AssetBundle name trong .meta (file hoặc folder)
  for (const f of [...files, ...[...byGuid.values()].filter((x) => x.folder)]) {
    const meta = readText(`${f.abs}.meta`);
    if (meta && /assetBundleName: \S/.test(meta) && !isEditorPath(f.path)) take(f, "assetbundle");
  }
  // Code và plugin: asmdef gần nhất phía trên quyết định (chỉ Editor / test thì không vào build).
  const asmdefCache = new Map();
  const asmByDir = new Map();
  for (const g of files) if (g.path.endsWith(".asmdef")) asmByDir.set(g.path.slice(0, g.path.lastIndexOf("/")), g);
  const editorOnlyDir = (dirPath) => {
    if (asmdefCache.has(dirPath)) return asmdefCache.get(dirPath);
    const asm = asmByDir.get(dirPath);
    const r = asm ? asmdefEditorOnly(asm.abs) : dirPath.includes("/") ? editorOnlyDir(dirPath.slice(0, dirPath.lastIndexOf("/"))) : false;
    asmdefCache.set(dirPath, r);
    return r;
  };
  for (const f of files) {
    if (isEditorPath(f.path)) continue;
    const dir = f.path.slice(0, f.path.lastIndexOf("/"));
    if (f.path.endsWith(".cs")) {
      if (!editorOnlyDir(dir)) take(f, "code");
    } else if (PLUGIN_EXT.test(f.path) || /\.(framework|bundle)\//i.test(f.path)) {
      if (!editorOnlyDir(dir)) take(f, "plugin");
    }
  }

  // Danh sách thật từ lần build gần nhất (gói Unity com.ezg.game-ip ghi Library/GameIP/build_assets.json sau mỗi lần build)
  const exactFile = path.join(root, "Library", "GameIP", "build_assets.json");
  inBuild.buildReport = null;
  try {
    const j = JSON.parse(readText(exactFile));
    let added = 0;
    for (const a of j.assets || []) {
      const f = byPath.get(a.path) || (a.guid && byGuid.get(a.guid));
      if (f && !f.folder && !inBuild.has(f.path)) {
        take(f, "buildreport");
        added++;
      }
    }
    inBuild.buildReport = { generatedAt: j.generatedAt, target: j.target, development: j.development, assets: (j.assets || []).length, packedAssetGroups: j.packedAssetGroups, added };
  } catch {
    /* chưa build lần nào với gói Unity: chỉ có danh sách ước lượng */
  }

  // Đi bắc cầu theo GUID
  while (queue.length) {
    const f = queue.pop();
    const refs = new Set();
    let txt = null;
    if (YAML_EXT.test(f.path) || JSON_EXT.test(f.path)) txt = readText(f.abs);
    if (txt) {
      for (const m of txt.matchAll(/guid: ([0-9a-f]{32})/g)) refs.add(m[1]);
      for (const m of txt.matchAll(/"guid"\s*:\s*"([0-9a-f]{32})"/g)) refs.add(m[1]);
      for (const m of txt.matchAll(/m_SourceFontFileGUID: ([0-9a-f]{32})/g)) refs.add(m[1]);
    }
    // .meta của model, texture có thể tham chiếu material / texture ngoài (externalObjects, sprite atlas…)
    if (!f.path.endsWith(".cs")) {
      const meta = readText(`${f.abs}.meta`);
      if (meta && meta.length < 2_000_000) for (const m of meta.matchAll(/guid: ([0-9a-f]{32})/g)) if (m[1] !== f.guid) refs.add(m[1]);
    }
    for (const g of refs) {
      const t = byGuid.get(g);
      if (t && !isEditorPath(t.path)) take(t, `ref:${f.path}`);
    }
  }
  return inBuild;
}

/** Package trong manifest + lock: { name, version, source, dir, registry, licenseFile, licenseField }. */
export function readPackages(index) {
  const { root, packages } = index;
  let manifest = {};
  let lock = {};
  let registries = [];
  try {
    const m = JSON.parse(readText(path.join(root, "Packages", "manifest.json")));
    manifest = m.dependencies || {};
    registries = m.scopedRegistries || [];
  } catch {
    /* không có manifest */
  }
  try {
    lock = JSON.parse(readText(path.join(root, "Packages", "packages-lock.json"))).dependencies || {};
  } catch {
    /* không có lock */
  }
  const names = new Set([...Object.keys(manifest), ...packages.map((p) => p.name)]);
  const out = [];
  for (const name of names) {
    const p = packages.find((x) => x.name === name);
    const l = lock[name] || {};
    let licenseFile = null;
    let licenseField = null;
    let displayName = null;
    let author = null;
    if (p) {
      for (const f of safeFiles(p.dir)) if (/^(license|licence|copying|third[ -]party notices)(\.md|\.txt)?$/i.test(f)) licenseFile = licenseFile || path.join(p.dir, f);
      try {
        const pj = JSON.parse(readText(path.join(p.dir, "package.json")));
        licenseField = pj.license || null;
        displayName = pj.displayName || null;
        author = typeof pj.author === "string" ? pj.author : pj.author && pj.author.name;
      } catch {
        /* package.json hỏng */
      }
    }
    out.push({
      name,
      version: l.version || manifest[name] || null,
      source: l.source || (p ? p.source : null),
      direct: name in manifest || (l.depth === 0),
      dir: p ? p.dir : null,
      displayName,
      author: author || null,
      registry: (registries.find((r) => (r.scopes || []).some((sc) => name === sc || name.startsWith(`${sc}.`))) || {}).name || null,
      licenseFile,
      licenseField,
    });
  }
  return out.sort((a, b) => a.name.localeCompare(b.name));
}

function safeFiles(dir) {
  try {
    return fs.readdirSync(dir, { withFileTypes: true }).filter((e) => e.isFile()).map((e) => e.name);
  } catch {
    return [];
  }
}

/** productName, companyName, phiên bản Unity. */
export function projectInfo(root) {
  const ps = readText(path.join(root, "ProjectSettings", "ProjectSettings.asset")) || "";
  const pv = readText(path.join(root, "ProjectSettings", "ProjectVersion.txt")) || "";
  const pick = (re, s) => {
    const m = s.match(re);
    return m ? m[1].trim().replace(/^'(.*)'$/, "$1") : null;
  };
  return {
    productName: pick(/productName: ([^\r\n]*)/, ps),
    companyName: pick(/companyName: ([^\r\n]*)/, ps),
    unityVersion: pick(/m_EditorVersion: ([^\r\n]*)/, pv),
  };
}
