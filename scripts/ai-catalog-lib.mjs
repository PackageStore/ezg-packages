/**
 * Shared scanning/packing for the AI catalog — used by upload-unity-template-ai.mjs (catalog + blobs
 * + history for Feature Hub's "AI Feature" tab and /update-ai) and upload-unity-template-defaultsetup.mjs
 * (the base lock baked into defaultsetup.tgz). One implementation, so the two can never disagree on
 * what an item is, which files it owns, or how a file is hashed.
 *
 * Ownership model (see DefaultSetup/.claude/rules/base-ownership.md):
 *   - every published file is BASE-owned: a project must not edit it in place;
 *   - Config/settings.json and Config/.mcp.json are MERGE-owned: projects legitimately add hooks /
 *     MCP servers, so updates 3-way merge them instead of overwriting;
 *   - project-owned files (CLAUDE_PROJECT.md, .claude/project/**, `local-*` skills/commands/agents/
 *     rules, the lock itself) must never reach the catalog — finding one fails the publish.
 *
 * File hash ("nsha"): sha256 of the content with CRLF folded to LF when the file is text (no NUL byte
 * in the first 8000 bytes). Identical in ezg_base.py and Feature Hub's EzgBaseLock.cs, so a Windows
 * checkout with core.autocrlf does not look like a local edit.
 */
import { createHash } from "node:crypto";
import { execFileSync } from "node:child_process";
import { deflateRawSync } from "node:zlib";
import { existsSync, readdirSync, readFileSync, statSync } from "node:fs";
import { basename, dirname, extname, join, relative, sep } from "node:path";
import { REPO_ROOT } from "./registry-lib.mjs";

export const DEFAULT_SETUP_DIR = join(REPO_ROOT, "templates", "unity-project", "DefaultSetup");
export const EXTRA_DIR = join(REPO_ROOT, "templates", "AIFeatures");
export const LOCK_REL = ".claude/ezg-base.lock.json";

// ---------------------------------------------------------------------------
// Category map — what counts as an "AI feature" and where it lands in a project
// ---------------------------------------------------------------------------
// mode: "dir"  = every subdirectory is one item
//       "file" = every file (matching `ext` if set) is one item
//       "any"  = every direct child, file or directory, is one item
//       "list" = the explicit `files` paths, each one item
// Mirrored by KIND_DIRS in DefaultSetup/.claude/skills/update-ai/scripts/ezg_base.py — change both.
export const KIND_MAP = [
  { id: "Skills", name: "Skills", dir: ".claude/skills", mode: "dir",
    description: "Playbook Claude tự nạp khi task khớp mô tả." },
  { id: "Commands", name: "Commands", dir: ".claude/commands", mode: "file", ext: ".md",
    description: "Slash command gọi tay (/new-ui, /push…)." },
  { id: "Agents", name: "Agents", dir: ".claude/agents", mode: "file", ext: ".md",
    description: "Subagent chuyên trách (review, QA, planner…)." },
  { id: "Rules", name: "Rules", dir: ".claude/rules", mode: "file", ext: ".md",
    description: "Quy tắc code/kiến trúc Claude phải tuân theo." },
  { id: "Docs", name: "Docs", dir: ".claude/docs", mode: "any",
    description: "Tài liệu tham chiếu cho pipeline AI." },
  { id: "Scripts", name: "Scripts", dir: ".claude/scripts", mode: "any",
    description: "Script hỗ trợ mà skill/command gọi tới." },
  { id: "Harness", name: "Harness", dir: ".claude/harness", mode: "any",
    description: "Launcher chạy harness/model ngoài." },
  { id: "Templates", name: "Backlog Templates", dir: ".claude/backlog-templates", mode: "any",
    description: "Template task/backlog cho planning pipeline." },
  { id: "UiKit", name: "UI Kit", dir: ".claude/ui-kit", mode: "any",
    description: "Contract UI kit mà mockup và /new-ui đọc." },
  // project-profile.json is NOT here: it is seeded once by the builder and then owned by the project
  // (new keys reach it through project_profile.DEFAULTS), so the catalog must never overwrite it.
  { id: "Config", name: "Config", mode: "list",
    files: [".claude/settings.json", ".mcp.json", "CLAUDE.md"],
    description: "Cấu hình gốc. CLAUDE.md: thuộc base, cài là ghi đè. settings.json / .mcp.json: project được thêm hook/MCP riêng — cập nhật bằng /update-ai (gộp JSON, giữ phần của project)." },
];
export const CATEGORY_BY_ID = new Map(KIND_MAP.map((k) => [k.id, k]));

/** Items whose file a project edits on purpose: updated by a 3-way JSON merge, never overwritten. */
export const MERGE_IDS = new Set(["Config/settings.json", "Config/.mcp.json"]);
export const ownershipOf = (id) => (MERGE_IDS.has(id) ? "merge" : "base");

/** Where an extra (non-DefaultSetup) item of a given category installs to. */
export function categoryTargetDir(categoryId) {
  const kind = CATEGORY_BY_ID.get(categoryId);
  if (kind?.dir) return kind.dir;
  if (kind?.mode === "list") return "."; // Config items carry their own full path
  return `.claude/${categoryId.toLowerCase()}`; // unknown category from AIFeatures/
}

// Junk that must never travel to a project.
const EXCLUDED_NAMES = new Set([".DS_Store", "settings.local.json", "__pycache__", ".pytest_cache"]);
export const isJunk = (name) => EXCLUDED_NAMES.has(name) || name.startsWith("._") || name.endsWith(".pyc");

// A game project's OWN files, which only ever exist inside that project. One game's copy in the
// catalog would overwrite every other game's on install. Finding one here is a mistake upstream, so
// the publish fails instead of quietly dropping it.
const PROJECT_OWNED_NAMES = new Set(["CLAUDE_PROJECT.md", "ezg-base.lock.json", "ezg-pins.json"]);
const PROJECT_ITEM_DIRS = [".claude/skills/", ".claude/commands/", ".claude/agents/", ".claude/rules/"];
export function projectOwnedReason(projectRelative) {
  if (PROJECT_OWNED_NAMES.has(basename(projectRelative))) return "file riêng của project";
  if (projectRelative.startsWith(".claude/project/")) return ".claude/project/ là thư mục riêng của project";
  for (const dir of PROJECT_ITEM_DIRS)
    if (projectRelative.startsWith(dir) && projectRelative.slice(dir.length).startsWith("local-"))
      return "tên `local-*` dành riêng cho skill/command/agent/rule của project";
  return null;
}

export const sha256 = (buf) => createHash("sha256").update(buf).digest("hex");
export const toPosix = (p) => p.split(sep).join("/");

/** Text content with CRLF folded to LF (binary untouched) — what a blob stores and nsha hashes. */
export function normalizeContent(buf) {
  if (buf.subarray(0, 8000).includes(0)) return buf;
  const text = buf.toString("latin1");
  return text.includes("\r\n") ? Buffer.from(text.replace(/\r\n/g, "\n"), "latin1") : buf;
}
export const nsha = (buf) => sha256(normalizeContent(buf));

// ---------------------------------------------------------------------------
// Deterministic zip writer (store/deflate, no deps)
// ---------------------------------------------------------------------------
// Timestamps are frozen so re-packing an unchanged item yields a byte-identical archive: the
// sha256 in the catalog then only moves when the CONTENT moves, which is what "Có bản mới" in the
// Feature Hub must mean. `zip`(1) would stamp mtimes and make every run look like a change.
const CRC_TABLE = (() => {
  const table = new Int32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    table[n] = c;
  }
  return table;
})();

function crc32(buf) {
  let c = ~0;
  for (let i = 0; i < buf.length; i++) c = CRC_TABLE[(c ^ buf[i]) & 0xff] ^ (c >>> 8);
  return ~c >>> 0;
}

const DOS_TIME = 0;
const DOS_DATE = ((2020 - 1980) << 9) | (1 << 5) | 1; // 2020-01-01

/** entries: [{ name, data, executable }] -> Buffer of a .zip */
export function makeZip(entries) {
  const locals = [];
  const central = [];
  let offset = 0;

  for (const entry of entries) {
    const nameBuf = Buffer.from(entry.name, "utf8");
    const deflated = deflateRawSync(entry.data, { level: 9 });
    const useDeflate = deflated.length < entry.data.length;
    const body = useDeflate ? deflated : entry.data;
    const method = useDeflate ? 8 : 0;
    const crc = crc32(entry.data);

    const local = Buffer.alloc(30);
    local.writeUInt32LE(0x04034b50, 0);
    local.writeUInt16LE(20, 4);
    local.writeUInt16LE(0x0800, 6); // UTF-8 names
    local.writeUInt16LE(method, 8);
    local.writeUInt16LE(DOS_TIME, 10);
    local.writeUInt16LE(DOS_DATE, 12);
    local.writeUInt32LE(crc, 14);
    local.writeUInt32LE(body.length, 18);
    local.writeUInt32LE(entry.data.length, 22);
    local.writeUInt16LE(nameBuf.length, 26);
    local.writeUInt16LE(0, 28);
    locals.push(local, nameBuf, body);

    const cd = Buffer.alloc(46);
    cd.writeUInt32LE(0x02014b50, 0);
    cd.writeUInt16LE(0x031e, 4); // made by unix / zip 3.0
    cd.writeUInt16LE(20, 6);
    cd.writeUInt16LE(0x0800, 8);
    cd.writeUInt16LE(method, 10);
    cd.writeUInt16LE(DOS_TIME, 12);
    cd.writeUInt16LE(DOS_DATE, 14);
    cd.writeUInt32LE(crc, 16);
    cd.writeUInt32LE(body.length, 20);
    cd.writeUInt32LE(entry.data.length, 24);
    cd.writeUInt16LE(nameBuf.length, 28);
    cd.writeUInt16LE(0, 30);
    cd.writeUInt16LE(0, 32);
    cd.writeUInt16LE(0, 34);
    cd.writeUInt16LE(0, 36); // internal attrs
    // Unix mode in the high 16 bits — kept for humans unzipping by hand; the Unity installer
    // re-applies +x itself because .NET's ZipArchive ignores these bits.
    cd.writeUInt32LE(((entry.executable ? 0o100755 : 0o100644) << 16) >>> 0, 38);
    cd.writeUInt32LE(offset, 42);
    central.push(cd, nameBuf);

    offset += local.length + nameBuf.length + body.length;
  }

  const centralBuf = Buffer.concat(central);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(0, 4);
  end.writeUInt16LE(0, 6);
  end.writeUInt16LE(entries.length, 8);
  end.writeUInt16LE(entries.length, 10);
  end.writeUInt32LE(centralBuf.length, 12);
  end.writeUInt32LE(offset, 16);
  end.writeUInt16LE(0, 20);

  return Buffer.concat([...locals, centralBuf, end]);
}

// ---------------------------------------------------------------------------
// Scanning
// ---------------------------------------------------------------------------

/** Every file under `abs` (recursive), as absolute paths, junk filtered, sorted. */
function walkFiles(abs) {
  const out = [];
  const stack = [abs];
  while (stack.length > 0) {
    const current = stack.pop();
    for (const entry of readdirSync(current, { withFileTypes: true })) {
      if (isJunk(entry.name)) continue;
      const full = join(current, entry.name);
      if (entry.isDirectory()) stack.push(full);
      else if (entry.isFile()) out.push(full);
    }
  }
  return out.sort();
}

export const EXECUTABLE_EXT = new Set([".sh", ".command", ".py", ".ps1", ".bat"]);

/** Pull `description:` out of a markdown frontmatter block (skills/commands/agents all use it). */
function frontmatterDescription(absFile) {
  let text;
  try {
    text = readFileSync(absFile, "utf8");
  } catch {
    return null;
  }
  if (!text.startsWith("---")) return null;
  const end = text.indexOf("\n---", 3);
  if (end === -1) return null;
  const block = text.slice(3, end);
  const match = block.match(/^description:\s*(.+?)\s*$/m);
  if (!match) return null;
  return match[1].replace(/^["']|["']$/g, "").trim();
}

/** Best-effort one-liner describing an item, for the card subtitle in the Hub. */
function describeItem(rootAbs, installPath, isDirectory) {
  const abs = join(rootAbs, installPath);
  if (isDirectory) {
    for (const candidate of ["SKILL.md", "README.md", "AGENT.md"]) {
      const file = join(abs, candidate);
      if (existsSync(file)) {
        const desc = frontmatterDescription(file);
        if (desc) return desc;
      }
    }
    return null;
  }
  return extname(abs).toLowerCase() === ".md" ? frontmatterDescription(abs) : null;
}

/**
 * Build one catalog item from a project-relative path inside `rootAbs`.
 * `installPath` is where it lands in a target project — identical to its path in the source,
 * which is exactly why the zip can carry project-relative entry names.
 */
function makeItem({ rootAbs, categoryId, installPath, name, source }) {
  const abs = join(rootAbs, installPath);
  const isDirectory = statSync(abs).isDirectory();
  const files = (isDirectory ? walkFiles(abs) : [abs])
    .map((f) => toPosix(relative(rootAbs, f)));
  if (files.length === 0) return null;

  return {
    id: `${categoryId}/${name}`,
    name,
    category: categoryId,
    description: describeItem(rootAbs, installPath, isDirectory) || "",
    installPath: toPosix(installPath),
    isDirectory,
    files,
    fileCount: files.length,
    installedByDefault: false,
    source,
    _rootAbs: rootAbs,
  };
}

/** Item name shown in the Hub: `.md` loses its extension (that is how commands are invoked). */
function itemName(fileOrDirName, isDirectory) {
  if (isDirectory) return fileOrDirName;
  return extname(fileOrDirName).toLowerCase() === ".md"
    ? basename(fileOrDirName, extname(fileOrDirName))
    : fileOrDirName;
}

/** Scan DefaultSetup according to KIND_MAP. */
export function scanDefaultSetup() {
  const items = [];
  for (const kind of KIND_MAP) {
    if (kind.mode === "list") {
      for (const rel of kind.files) {
        const abs = join(DEFAULT_SETUP_DIR, rel);
        if (!existsSync(abs)) continue;
        const item = makeItem({
          rootAbs: DEFAULT_SETUP_DIR,
          categoryId: kind.id,
          installPath: rel,
          name: basename(rel),
          source: "defaultsetup",
        });
        if (item) items.push(item);
      }
      continue;
    }

    const dirAbs = join(DEFAULT_SETUP_DIR, kind.dir);
    if (!existsSync(dirAbs)) continue;

    for (const entry of readdirSync(dirAbs, { withFileTypes: true })) {
      if (isJunk(entry.name)) continue;
      const isDirectory = entry.isDirectory();
      if (kind.mode === "dir" && !isDirectory) continue;
      if (kind.mode === "file" && isDirectory) continue;
      if (kind.mode === "file" && kind.ext && extname(entry.name).toLowerCase() !== kind.ext) continue;

      const item = makeItem({
        rootAbs: DEFAULT_SETUP_DIR,
        categoryId: kind.id,
        installPath: `${kind.dir}/${entry.name}`,
        name: itemName(entry.name, isDirectory),
        source: "defaultsetup",
      });
      if (item) items.push(item);
    }
  }
  return items;
}

/**
 * Build an item whose files live somewhere else on disk than where they must land in a project.
 * `parentAbs/entryName` is the source; the zip entries are rewritten to the install path so the
 * archive stays self-describing (installer just writes entries under the project root).
 */
function makeStagedItem({ parentAbs, entryName, categoryId, name }) {
  const targetDir = categoryTargetDir(categoryId);
  const installPath = toPosix(targetDir === "." ? entryName : `${targetDir}/${entryName}`);
  const item = makeItem({
    rootAbs: parentAbs,
    categoryId,
    installPath: entryName,
    name,
    source: "extra",
  });
  if (!item) return null;

  item.files = item.files.map((f) =>
    f === entryName ? installPath : `${installPath}/${f.slice(entryName.length + 1)}`);
  item.installPath = installPath;
  item._stagingPrefix = entryName;
  return item;
}

/**
 * Scan templates/AIFeatures/<Category>/<item>. These are items that do NOT ship with
 * DefaultSetup (or deliberately override one). The install path is derived from the category.
 */
function scanExtra() {
  if (!existsSync(EXTRA_DIR)) return [];
  const items = [];

  for (const catDir of readdirSync(EXTRA_DIR, { withFileTypes: true })) {
    if (!catDir.isDirectory() || isJunk(catDir.name)) continue;
    const catAbs = join(EXTRA_DIR, catDir.name);

    for (const entry of readdirSync(catAbs, { withFileTypes: true })) {
      if (isJunk(entry.name)) continue;
      const item = makeStagedItem({
        parentAbs: catAbs,
        entryName: entry.name,
        categoryId: catDir.name,
        name: itemName(entry.name, entry.isDirectory()),
      });
      if (item) items.push(item);
    }
  }
  return items;
}

/**
 * Publish something that already lives elsewhere in the repo, WITHOUT copying it into
 * AIFeatures/ — a second copy would drift the first time someone edits one of them.
 * manifest.sources maps "<Category>/<name>" to a repo-relative path.
 */
function scanManifestSources(sources) {
  const items = [];
  for (const [id, relPath] of Object.entries(sources)) {
    const slash = id.indexOf("/");
    if (slash <= 0) throw new Error(`sources: id phải là "<Category>/<name>", nhận: ${id}`);
    const abs = join(REPO_ROOT, relPath);
    if (!existsSync(abs)) throw new Error(`sources["${id}"]: không tìm thấy ${relPath}`);

    const item = makeStagedItem({
      parentAbs: dirname(abs),
      entryName: basename(abs),
      categoryId: id.slice(0, slash),
      name: id.slice(slash + 1),
    });
    if (item) items.push(item);
  }
  return items;
}

/** Optional curation file: { exclude, overrides, sources } — see templates/AIFeatures/README.md. */
export function loadManifest() {
  const path = join(EXTRA_DIR, "ai-manifest.json");
  if (!existsSync(path)) return { exclude: [], overrides: {}, sources: {} };
  try {
    const parsed = JSON.parse(readFileSync(path, "utf8"));
    return {
      exclude: parsed.exclude || [],
      overrides: parsed.overrides || {},
      sources: parsed.sources || {},
    };
  } catch (err) {
    throw new Error(`ai-manifest.json không parse được: ${err.message}`);
  }
}

/** "Cat/name" or "Cat/*" — the only two forms the manifest needs. */
export function matchesRule(rule, id) {
  if (rule === id) return true;
  return rule.endsWith("/*") && id.startsWith(rule.slice(0, -1));
}

/** Fail loudly when a project-owned file would be published (see projectOwnedReason). */
function assertNoProjectFiles(items) {
  const leaked = items.flatMap((item) =>
    item.files.map((f) => [f, projectOwnedReason(f)]).filter(([, why]) => why)
      .map(([f, why]) => `${item.id}: ${f} — ${why}`));
  if (leaked.length > 0)
    throw new Error(
      "File riêng của project không được lên catalog dùng chung (chỉ ship khung template) — gỡ khỏi repo " +
      "rồi chạy lại:\n  " + leaked.join("\n  "));
}

/**
 * Every catalog item: DefaultSetup → AIFeatures → manifest `sources` (later wins on the same id),
 * minus `exclude`, with `overrides` applied, sorted. `onlyDefaultSetup` = the items exactly as the
 * defaultsetup.tgz ships them (for the lock baked into it).
 */
export function collectItems({ manifest = loadManifest(), onlyDefaultSetup = false } = {}) {
  const byId = new Map();
  for (const item of scanDefaultSetup()) byId.set(item.id, item);
  if (!onlyDefaultSetup) {
    for (const item of scanExtra()) byId.set(item.id, item);
    for (const item of scanManifestSources(manifest.sources)) byId.set(item.id, item);
  }

  const items = [...byId.values()]
    .filter((item) => !manifest.exclude.some((rule) => matchesRule(rule, item.id)))
    .map((item) => {
      const override = manifest.overrides[item.id];
      return override ? { ...item, ...override, id: item.id, category: item.category } : item;
    })
    .sort((a, b) => a.category.localeCompare(b.category) || a.name.localeCompare(b.name));

  assertNoProjectFiles(items);
  return items;
}

/** Bytes of one item file, addressed by its project-relative install path. */
export function readItemFile(item, projectRelative) {
  const stagingRelative = item._stagingPrefix
    ? item._stagingPrefix + projectRelative.slice(item.installPath.length)
    : projectRelative;
  return readFileSync(join(item._rootAbs, stagingRelative));
}

/**
 * Pack one item: deterministic zip + its sha256 + per-file nsha. Fills fileName/sha256/size/
 * fileHashes/ownership on the item and keeps the zip + raw contents on private fields.
 */
export function packItem(item) {
  const contents = new Map();
  const entries = item.files.map((projectRelative) => {
    const data = readItemFile(item, projectRelative);
    contents.set(projectRelative, data);
    return {
      name: projectRelative,
      data,
      executable: EXECUTABLE_EXT.has(extname(projectRelative).toLowerCase()),
    };
  });
  const zip = makeZip(entries);
  item.fileName = `${item.name.replace(/^\.+/, "")}.zip`;
  item.sha256 = sha256(zip);
  item.size = zip.length;
  item.ownership = ownershipOf(item.id);
  item.fileHashes = Object.fromEntries([...contents].map(([path, data]) => [path, nsha(data)]));
  item._zip = zip;
  item._contents = contents;
  return item;
}

/** The base lock a project gets for a set of packed items (same shape ezg_base.py writes). */
export function buildLock(items, comment) {
  const lockItems = {};
  const lockFiles = {};
  for (const item of items) {
    lockItems[item.id] = {
      sha256: item.sha256,
      installPath: item.installPath,
      isDirectory: item.isDirectory,
      ownership: item.ownership || ownershipOf(item.id),
    };
    for (const [path, hash] of Object.entries(item.fileHashes)) lockFiles[path] = { item: item.id, sha256: hash };
  }
  const sortObj = (o) => Object.fromEntries(Object.keys(o).sort().map((k) => [k, o[k]]));
  return {
    $comment: comment,
    schemaVersion: 1,
    updatedAtUtc: null,
    items: sortObj(lockItems),
    files: sortObj(lockFiles),
  };
}

// ---------------------------------------------------------------------------
// Git history → every version ever published for each install path
// ---------------------------------------------------------------------------

/**
 * Map a repo path to where it lands in a project, or null when it is not catalog content.
 * Uses the CURRENT layout rules; an old path that no longer maps is simply skipped.
 */
function repoPathToInstallPath(repoPath, sources) {
  const ds = "templates/unity-project/DefaultSetup/";
  if (repoPath.startsWith(ds)) {
    const rest = repoPath.slice(ds.length);
    if (rest.startsWith("ProjectSettings/") || rest.startsWith(".agents/")) return null;
    return rest;
  }
  const extra = "templates/AIFeatures/";
  if (repoPath.startsWith(extra)) {
    const parts = repoPath.slice(extra.length).split("/");
    if (parts.length < 2) return null; // index.json, README.md, ai-manifest.json
    const target = categoryTargetDir(parts[0]);
    const rest = parts.slice(1).join("/");
    return target === "." ? rest : `${target}/${rest}`;
  }
  for (const [id, relPath] of Object.entries(sources)) {
    const src = relPath.replace(/\/+$/, "");
    if (repoPath === src || repoPath.startsWith(src + "/")) {
      const target = categoryTargetDir(id.slice(0, id.indexOf("/")));
      return `${target}/${basename(src)}${repoPath.slice(src.length)}`;
    }
  }
  return null;
}

/**
 * [{ path, hash, data }] for every blob version of catalog content in git history, oldest first.
 * Returns null (with a warning) on a shallow clone — CI must check out with fetch-depth: 0.
 */
export function collectGitHistory(manifest = loadManifest()) {
  const git = (args, input) => execFileSync("git", args, {
    cwd: REPO_ROOT, input, maxBuffer: 512 * 1024 * 1024, stdio: ["pipe", "pipe", "ignore"],
  });
  try {
    if (git(["rev-parse", "--is-shallow-repository"]).toString().trim() === "true") {
      console.warn("! git history: clone nông (shallow) — bỏ qua lịch sử; CI cần checkout fetch-depth: 0.");
      return null;
    }
  } catch {
    console.warn("! git history: không chạy được git — bỏ qua lịch sử.");
    return null;
  }

  const roots = ["templates/unity-project/DefaultSetup", "templates/AIFeatures", ...Object.values(manifest.sources)];
  const log = git(["-c", "core.quotepath=off", "log", "--reverse", "--format=", "--raw", "--no-abbrev", "--no-renames", "--", ...roots]).toString();
  const seen = new Set();
  const wanted = [];
  for (const line of log.split("\n")) {
    if (!line.startsWith(":")) continue;
    const tab = line.indexOf("\t");
    const [, newMode, , newSha, status] = line.slice(0, tab).split(/\s+/);
    if (status === "D" || !newMode.startsWith("100")) continue; // deletions, symlinks, submodules
    const installPath = repoPathToInstallPath(line.slice(tab + 1), manifest.sources);
    if (!installPath || projectOwnedReason(installPath)) continue;
    const key = `${installPath}\0${newSha}`;
    if (seen.has(key)) continue;
    seen.add(key);
    wanted.push({ path: installPath, blob: newSha });
  }

  // One `git cat-file --batch` for every distinct blob.
  const blobs = [...new Set(wanted.map((w) => w.blob))];
  const out = blobs.length ? git(["cat-file", "--batch"], blobs.join("\n") + "\n") : Buffer.alloc(0);
  const content = new Map();
  let offset = 0;
  for (const sha of blobs) {
    const headerEnd = out.indexOf(0x0a, offset);
    const [gotSha, type, size] = out.subarray(offset, headerEnd).toString().split(" ");
    const len = Number(size);
    if (gotSha !== sha || type !== "blob") throw new Error(`git cat-file: mong ${sha}, nhận ${gotSha} ${type}`);
    content.set(sha, out.subarray(headerEnd + 1, headerEnd + 1 + len));
    offset = headerEnd + 1 + len + 1; // trailing LF after each object
  }

  return wanted.map((w) => {
    const data = normalizeContent(content.get(w.blob));
    return { path: w.path, hash: sha256(data), data };
  });
}
