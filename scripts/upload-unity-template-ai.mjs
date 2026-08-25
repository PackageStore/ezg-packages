#!/usr/bin/env node
/**
 * Publish AI assets (Claude Code skills, commands, agents, rules, docs, scripts…) to Cloudflare R2
 * so the Feature Hub "AI Feature" tab can install them ONE BY ONE into an existing project.
 *
 * Why this exists: DefaultSetup ships the whole `.claude/` tree exactly once, when a project is
 * generated (defaultsetup.tgz). A project created last month has no way to pick up a skill written
 * last week. This catalog makes every AI asset individually installable/updatable at any time.
 *
 * Sources (an item found in BOTH is taken from AIFeatures/ — that folder is the override):
 *   1. templates/unity-project/DefaultSetup/  — the shipped baseline, mapped by KIND_MAP below
 *   2. templates/AIFeatures/<Category>/<item> — extra items that are NOT part of DefaultSetup
 *   3. ai-manifest.json `sources`                — anything else in the repo, published in place
 *                                                  (no copy to drift out of sync)
 *
 * For every item it:
 *   1. Collects the item's files (a directory item = the whole folder; a file item = one file).
 *   2. Packs them into a deterministic .zip whose entry names are PROJECT-RELATIVE paths
 *      (e.g. ".claude/skills/ui-kit/SKILL.md") so the installer just writes entries under the
 *      project root — no path rewriting, and the archive is self-describing.
 *   3. Uploads it to  unity-template/ai/files/<Category>/<file>.zip, but only when its sha256
 *      differs from the one the live index.json records — an edited item keeps its key, so
 *      "does the key exist" is the wrong question. Unchanged items cost nothing to re-run.
 *   4. Regenerates + uploads  unity-template/ai/index.json  (categories + every item on disk).
 *
 * The generated index.json is also written to templates/AIFeatures/index.json so it is
 * inspectable and committable, same as the feature catalogs.
 *
 * Curation lives in templates/AIFeatures/ai-manifest.json (optional): `exclude` drops items from
 * the catalog, `overrides` sets displayName/description/installedByDefault per "<Category>/<name>".
 *
 * Run:
 *   node --env-file=.env upload-unity-template-ai.mjs --dry-run
 *   node --env-file=.env upload-unity-template-ai.mjs                      # everything
 *   node --env-file=.env upload-unity-template-ai.mjs --category Skills
 *   node --env-file=.env upload-unity-template-ai.mjs --item Skills/ui-kit
 *
 * Flags:
 *   --category <Id>       only (re)upload this category's payloads (index still covers everything)
 *   --item <Cat/name>     only (re)upload this single item (index still covers everything)
 *   --dry-run             print what would happen, touch nothing on R2 or disk
 *   --force               re-upload payloads even when the live catalog says they are current
 *                         (only needed if an object was deleted from R2 by hand)
 *   --skip-files          upload index.json only, not the .zip payloads
 */
import { createHash } from "node:crypto";
import { deflateRawSync } from "node:zlib";
import { existsSync, readdirSync, readFileSync, statSync, writeFileSync, mkdirSync } from "node:fs";
import { basename, dirname, extname, join, relative, sep } from "node:path";
import {
  REPO_ROOT,
  getJson,
  makeClient,
  putObject,
  hasFlag,
} from "./registry-lib.mjs";

const DEFAULT_SETUP_DIR = join(REPO_ROOT, "templates", "unity-project", "DefaultSetup");
const EXTRA_DIR = join(REPO_ROOT, "templates", "AIFeatures");

// Public read root, e.g. https://.../template  (same convention as the feature catalogs).
const PUBLIC_ROOT = (
  process.env.UNITY_TEMPLATE_PUBLIC_BASE_URL ||
  "https://upm-registry-worker.developer-a1f.workers.dev/template/files"
).replace(/\/files\/?$/, "");
const R2_AI_PREFIX = (process.env.UNITY_TEMPLATE_AI_PREFIX || "unity-template/ai")
  .replace(/^\/+|\/+$/g, "");

const DRY_RUN = hasFlag("--dry-run");
const FORCE = hasFlag("--force");
const SKIP_FILES = hasFlag("--skip-files");
const argValue = (flag) => {
  const i = process.argv.indexOf(flag);
  return i !== -1 ? process.argv[i + 1] : null;
};
const ONLY_CATEGORY = argValue("--category");
const ONLY_ITEM = argValue("--item");

// ---------------------------------------------------------------------------
// Category map — what counts as an "AI feature" and where it lands in a project
// ---------------------------------------------------------------------------
// mode: "dir"  = every subdirectory is one item
//       "file" = every file (matching `ext` if set) is one item
//       "any"  = every direct child, file or directory, is one item
//       "list" = the explicit `files` paths, each one item
const KIND_MAP = [
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
  { id: "Config", name: "Config", mode: "list",
    files: [".claude/settings.json", ".claude/project-profile.json", ".mcp.json", "CLAUDE.md"],
    description: "Cấu hình gốc — CÀI ĐÈ file hiện có của project, cân nhắc trước khi cài." },
];
const CATEGORY_BY_ID = new Map(KIND_MAP.map((k) => [k.id, k]));

/** Where an extra (non-DefaultSetup) item of a given category installs to. */
function categoryTargetDir(categoryId) {
  const kind = CATEGORY_BY_ID.get(categoryId);
  if (kind?.dir) return kind.dir;
  if (kind?.mode === "list") return "."; // Config items carry their own full path
  return `.claude/${categoryId.toLowerCase()}`; // unknown category from AIFeatures/
}

// Junk that must never travel to a project.
const EXCLUDED_NAMES = new Set([".DS_Store", "settings.local.json", "__pycache__", ".pytest_cache"]);
const isJunk = (name) => EXCLUDED_NAMES.has(name) || name.startsWith("._") || name.endsWith(".pyc");

const sha256 = (buf) => createHash("sha256").update(buf).digest("hex");
const toPosix = (p) => p.split(sep).join("/");

/** Public URL for an R2 key (path-encoded per segment, same rule as the feature catalogs). */
function publicUrl(key) {
  const encoded = key
    .replace(/^unity-template\//, "")
    .split("/")
    .map((seg) => encodeURIComponent(seg))
    .join("/");
  return `${PUBLIC_ROOT.replace(/\/+$/, "")}/${encoded}`;
}

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
function makeZip(entries) {
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

const EXECUTABLE_EXT = new Set([".sh", ".command", ".py", ".ps1", ".bat"]);

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
function scanDefaultSetup() {
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
function loadManifest() {
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
function matchesRule(rule, id) {
  if (rule === id) return true;
  return rule.endsWith("/*") && id.startsWith(rule.slice(0, -1));
}

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------

async function main() {
  if (!existsSync(DEFAULT_SETUP_DIR)) throw new Error(`Not found: ${DEFAULT_SETUP_DIR}`);
  const manifest = loadManifest();

  // Extra items win over DefaultSetup ones with the same id (that is the point of the override folder).
  const byId = new Map();
  for (const item of scanDefaultSetup()) byId.set(item.id, item);
  for (const item of scanExtra()) byId.set(item.id, item);
  for (const item of scanManifestSources(manifest.sources)) byId.set(item.id, item);

  const items = [...byId.values()]
    .filter((item) => !manifest.exclude.some((rule) => matchesRule(rule, item.id)))
    .map((item) => {
      const override = manifest.overrides[item.id];
      return override ? { ...item, ...override, id: item.id, category: item.category } : item;
    })
    .sort((a, b) => a.category.localeCompare(b.category) || a.name.localeCompare(b.name));

  if (items.length === 0) throw new Error("Không tìm thấy AI item nào để publish.");

  // Pack + hash every item (cheap: a few hundred KB of text), so the index always reflects disk.
  for (const item of items) {
    const entries = item.files.map((projectRelative) => {
      const stagingRelative = item._stagingPrefix
        ? item._stagingPrefix + projectRelative.slice(item.installPath.length)
        : projectRelative;
      return {
        name: projectRelative,
        data: readFileSync(join(item._rootAbs, stagingRelative)),
        executable: EXECUTABLE_EXT.has(extname(projectRelative).toLowerCase()),
      };
    });
    const zip = makeZip(entries);
    const fileName = `${item.name.replace(/^\.+/, "")}.zip`;
    const key = `${R2_AI_PREFIX}/files/${item.category}/${fileName}`;

    item.fileName = fileName;
    item.url = publicUrl(key);
    item.sha256 = sha256(zip);
    item.size = zip.length;
    item._zip = zip;
    item._key = key;
  }

  if (ONLY_ITEM && !items.some((i) => i.id === ONLY_ITEM))
    throw new Error(`Item không có trên đĩa: ${ONLY_ITEM}`);
  if (ONLY_CATEGORY && !items.some((i) => i.category === ONLY_CATEGORY))
    throw new Error(`Category không có trên đĩa: ${ONLY_CATEGORY}`);

  const isTarget = (item) => {
    if (ONLY_ITEM) return item.id === ONLY_ITEM;
    if (ONLY_CATEGORY) return item.category === ONLY_CATEGORY;
    return true;
  };

  const indexKey = `${R2_AI_PREFIX}/index.json`;

  // What decides "needs uploading" is the CONTENT, not the key. An edited item keeps its key
  // (<name>.zip), so a plain existence check would skip exactly the upload that matters — and
  // forcing every run instead would re-push all ~140 payloads on every CI build. The live catalog
  // already records each item's sha256, so one GET tells us precisely what changed.
  let client = null;
  const liveSha = new Map();
  try {
    client = await makeClient();
    const live = await getJson(client, indexKey);
    for (const item of live?.items || []) liveSha.set(item.id, item.sha256);
  } catch (err) {
    if (!DRY_RUN) throw err; // publish thật mà thiếu creds thì phải dừng
    console.log(`~ dry-run: không đọc được catalog live (${err.message}) — coi như mọi item đều mới.\n`);
  }

  // 1. payloads
  const targets = items.filter(isTarget);
  const outdated = targets.filter((item) => FORCE || liveSha.get(item.id) !== item.sha256);
  const skipped = targets.length - outdated.length;
  let uploaded = 0;

  if (!SKIP_FILES) {
    for (const item of outdated) {
      const state = liveSha.has(item.id) ? "đổi nội dung" : "mới";
      if (DRY_RUN) {
        console.log(`~ dry-run ${item.id}  (${state})`);
        console.log(`    key    : ${item._key}`);
        console.log(`    files  : ${item.fileCount}  (${item.size} bytes zipped)`);
        console.log(`    target : ${item.installPath}`);
        console.log(`    sha256 : ${item.sha256}`);
        continue;
      }
      await putObject(client, item._key, item._zip, "application/zip");
      console.log(`+ uploaded ${item._key}  (${item.fileCount} files, ${state})`);
      uploaded++;
    }
    if (skipped > 0)
      console.log(`= ${skipped} item không đổi so với catalog live — bỏ qua payload.`);
  }

  // 2. index.json — always covers EVERY item on disk so the catalog never drifts.
  const categories = [...new Set(items.map((i) => i.category))]
    .sort()
    .map((id) => {
      const kind = CATEGORY_BY_ID.get(id);
      return {
        id,
        name: kind?.name || id,
        description: kind?.description || "",
        count: items.filter((i) => i.category === id).length,
      };
    });

  const index = {
    schemaVersion: 1,
    description: "EZG Feature Hub — catalog of Claude AI assets (skills, commands, agents…).",
    categories,
    items: items.map(({ _zip, _key, _rootAbs, _stagingPrefix, ...pub }) => pub),
  };
  const indexBody = `${JSON.stringify(index, null, 2)}\n`;
  const indexLocal = join(EXTRA_DIR, "index.json");

  if (DRY_RUN) {
    console.log(`~ dry-run index ${indexKey} (${items.length} items, ${categories.length} categories)`);
  } else {
    mkdirSync(EXTRA_DIR, { recursive: true });
    writeFileSync(indexLocal, indexBody);
    await putObject(client, indexKey, indexBody, "application/json");
    console.log(`+ index    ${indexKey} (${items.length} items)`);
  }

  console.log("\n--- summary ---");
  for (const cat of categories) console.log(`  ${cat.id.padEnd(10)} ${cat.count} item`);
  console.log(`  total: ${items.length} item`);
  if (!DRY_RUN && !SKIP_FILES)
    console.log(`  payload: ${uploaded} uploaded, ${skipped} unchanged`);
}

main().catch((err) => {
  console.error(err.message || err);
  process.exit(1);
});
