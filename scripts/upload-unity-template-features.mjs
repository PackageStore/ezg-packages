#!/usr/bin/env node
/**
 * Upload per-project game-feature catalogs (.unitypackage) to Cloudflare R2.
 *
 * Source layout (one folder per project, one subfolder per category):
 *   templates/Features/<PROJECT>/<Category>/<Feature>.unitypackage
 *   templates/Features/<PROJECT>/features-manifest.json   <- authored metadata (committed)
 *   templates/Features/<PROJECT>/catalog.json             <- generated (committed)
 *   templates/Features/index.json                         <- generated (committed)
 *
 * ## Why a manifest exists
 *
 * A `.unitypackage` carries no metadata beyond its asset paths, so early versions of this
 * script GUESSED the feature root (shallowest path inside the tarball) to fill `markerPaths`.
 * That guess decides what Feature Hub DELETES on uninstall — too shallow and it wipes
 * `Assets/_Project`. Guessing is now a fallback only: `features-manifest.json` declares
 * `markerPaths` / `markerGuids` / `description` / `requires` / `requiresPackages` per feature,
 * is committed to git, and always wins over anything derived from the binary.
 *
 * ## Why entries are merged, not rescanned
 *
 * `*.unitypackage` is gitignored — a clone only ever has the binaries whoever cloned it built.
 * Rebuilding catalog/index purely from disk therefore DELETED every project (and every feature)
 * whose binary happened to live on another machine. Entries are now merged in this order, per
 * feature, with the first available source winning each field:
 *
 *   authored (features-manifest.json)  >  computed from local binary  >  committed catalog.json
 *
 * so publishing one feature from a fresh clone can never drop the other 46.
 *
 * Run:
 *   node --env-file=.env upload-unity-template-features.mjs --dry-run
 *   node --env-file=.env upload-unity-template-features.mjs --project M001 --feature Events/BattleRoyale
 *   node --env-file=.env upload-unity-template-features.mjs --project M001 --all
 *   node upload-unity-template-features.mjs --project M001 --emit-manifest     # no creds needed
 *   node upload-unity-template-features.mjs --project M001 --emit-only         # no creds needed
 *
 * Flags:
 *   --project <ID>        project to (re)upload; index still covers every project known
 *   --feature <Cat/Name>  publish exactly this one feature (repeatable). Default when a project
 *                         has more than one feature on disk and --all was not passed.
 *   --all                 publish every feature of the project (seeding a project the first time)
 *   --dry-run             print what would happen, touch nothing on R2 or disk
 *   --force               re-upload packages even if the key already exists (updates need this)
 *   --skip-files          upload catalogs + index only, not the .unitypackage payloads
 *   --emit-only           write catalog.json + index.json to disk, never talk to R2 (route B:
 *                         the JSON is then published through upload-asset.yml)
 *   --emit-manifest       write a features-manifest.json skeleton for the project from the
 *                         binaries on disk (explicit markerPaths + markerGuids), then exit
 *   --fetch-missing       download binaries listed in the committed catalog but absent locally
 *   --remove <Cat/Name>   drop a feature from catalog + index (repeatable). Add --purge to also
 *                         delete its object from R2 (irreversible)
 *   --purge               with --remove: hard-delete the R2 object too
 *   --allow-multi-root    let a package span several root folders without a manifest entry
 *                         (marker detection stays correct, uninstall becomes partial)
 */
import { execFileSync } from "node:child_process";
import { createHash } from "node:crypto";
import {
  existsSync,
  mkdirSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { basename, dirname, extname, join } from "node:path";
import {
  REPO_ROOT,
  makeClient,
  objectExists,
  getObject,
  putObject,
  deleteObject,
  hasFlag,
} from "./registry-lib.mjs";

const FEATURES_DIR = join(REPO_ROOT, "templates", "Features");
// Public read root, e.g. https://pub-xxxx.r2.dev/unity-template
const PUBLIC_ROOT = (
  process.env.UNITY_TEMPLATE_PUBLIC_BASE_URL ||
  "https://upm-registry-worker.developer-a1f.workers.dev/template/files"
).replace(/\/files\/?$/, "");
const R2_FEATURES_PREFIX = (process.env.UNITY_TEMPLATE_FEATURES_PREFIX || "unity-template/features")
  .replace(/^\/+|\/+$/g, "");

/**
 * Folders a marker may never point at: uninstall deletes every marker path it resolves, so a
 * marker on a container folder wipes unrelated features (or the whole project source root).
 * Checked for authored markers too — an explicit `Assets/_Project` is still a project-eater.
 */
const MARKER_DENYLIST = new Set([
  "Assets",
  "Assets/_Project",
  "Assets/Plugins",
  "Assets/Scripts",
  "Assets/Resources",
  "Assets/StreamingAssets",
]);
/** `Assets/_Project/Features/<X>` = 4 segments — anything shallower is a container, not a feature. */
const MIN_MARKER_DEPTH = 4;

const DRY_RUN = hasFlag("--dry-run");
const FORCE = hasFlag("--force");
const SKIP_FILES = hasFlag("--skip-files");
const EMIT_ONLY = hasFlag("--emit-only");
const EMIT_MANIFEST = hasFlag("--emit-manifest");
const FETCH_MISSING = hasFlag("--fetch-missing");
const PURGE = hasFlag("--purge");
const ALL = hasFlag("--all");
const ALLOW_MULTI_ROOT = hasFlag("--allow-multi-root");

/** All values passed for a repeatable `--flag value` option. */
function argValues(flag) {
  const out = [];
  for (let i = 0; i < process.argv.length; i++) {
    if (process.argv[i] === flag && process.argv[i + 1]) out.push(process.argv[i + 1]);
  }
  return out;
}
const argValue = (flag) => argValues(flag)[0] || null;

const ONLY_PROJECT = argValue("--project");
const ONLY_FEATURES = argValues("--feature");
const REMOVE_FEATURES = argValues("--remove");

const sha256 = (buf) => createHash("sha256").update(buf).digest("hex");
const featureId = (category, name) => `${category}/${name}`;
const depth = (p) => p.split("/").filter(Boolean).length;

function readJson(path, fallback) {
  if (!existsSync(path)) return fallback;
  try {
    return JSON.parse(readFileSync(path, "utf8"));
  } catch (e) {
    throw new Error(`JSON hỏng: ${path} — ${e.message}`);
  }
}

const manifestPath = (id) => join(FEATURES_DIR, id, "features-manifest.json");
const catalogPath = (id) => join(FEATURES_DIR, id, "catalog.json");
const indexPath = () => join(FEATURES_DIR, "index.json");
const r2FileKey = (id, category, fileName) =>
  `${R2_FEATURES_PREFIX}/${id}/files/${category}/${fileName}`;
const r2CatalogKey = (id) => `${R2_FEATURES_PREFIX}/${id}/catalog.json`;
const r2IndexKey = () => `${R2_FEATURES_PREFIX}/index.json`;

/** Public URL for an R2 key (path-encoded, spaces & friends escaped per segment). */
function publicUrl(key) {
  // PUBLIC_ROOT already points at the bucket's "unity-template/" prefix — either literally
  // (r2.dev: .../unity-template) or via the worker route (.../template). So drop that prefix
  // from the key instead of from the root, or the path ends up doubled and 404s.
  const encoded = key
    .replace(/^unity-template\//, "")
    .split("/")
    .map((seg) => encodeURIComponent(seg))
    .join("/");
  return `${PUBLIC_ROOT.replace(/\/+$/, "")}/${encoded}`;
}

/**
 * Map every asset inside a .unitypackage to its GUID.
 *
 * A .unitypackage is a tarball of `<guid>/pathname` (+ `asset`, `asset.meta`, `preview.png`)
 * members. Extracting only the tiny `pathname` files into a temp dir keeps the GUID→path pairing
 * exact — reading the concatenated stream instead would rely on archive order lining up.
 * Returns Map<projectRelativePath, guid>.
 */
function readPackageAssets(pkgPath) {
  const tmp = mkdtempSync(join(tmpdir(), "ezg-feat-"));
  try {
    // bsdtar (macOS) matches glob members by default; GNU tar also accepts this form.
    execFileSync("tar", ["-xzf", pkgPath, "-C", tmp, "*/pathname"], { stdio: ["ignore", "ignore", "pipe"] });
    const map = new Map();
    for (const guid of readdirSync(tmp)) {
      const f = join(tmp, guid, "pathname");
      if (!existsSync(f)) continue;
      const p = readFileSync(f, "utf8").split(/[\r\n]/)[0].trim();
      if (p) map.set(p, guid);
    }
    return map;
  } catch (e) {
    throw new Error(`Không đọc được nội dung ${basename(pkgPath)}: ${e.message}`);
  } finally {
    rmSync(tmp, { recursive: true, force: true });
  }
}

/** Paths in the set that have no ancestor in the same set = the package's root folders. */
function computeRoots(paths) {
  const set = new Set(paths);
  const roots = [];
  for (const p of paths) {
    let hasAncestor = false;
    for (let cur = dirname(p); cur && cur !== "." && cur !== "/"; cur = dirname(cur)) {
      if (set.has(cur)) {
        hasAncestor = true;
        break;
      }
    }
    if (!hasAncestor) roots.push(p);
  }
  return [...new Set(roots)].sort();
}

/**
 * Derive `{ markerPaths, markerGuids }` from a package's contents — the fallback used when
 * features-manifest.json says nothing about this feature. Throws when the result would be a
 * marker that is unsafe to hand to uninstall.
 */
function deriveMarkers(pkgPath, id) {
  const assets = readPackageAssets(pkgPath);
  const roots = computeRoots([...assets.keys()]);
  if (roots.length === 0) throw new Error(`${id}: package rỗng, không có asset nào.`);

  if (roots.length > 1 && !ALLOW_MULTI_ROOT) {
    throw new Error(
      `${id}: package trải trên ${roots.length} thư mục gốc:\n` +
        roots.map((r) => `    - ${r}`).join("\n") +
        `\n  Feature phải self-contained trong MỘT folder (art/CSV để trong feature đó), vì marker\n` +
        `  vừa là dấu nhận diện "đã cài" vừa là thứ bị XÓA khi gỡ. Cách xử lý:\n` +
        `    a) gom asset lạc vào trong folder feature rồi export lại (khuyến nghị), hoặc\n` +
        `    b) khai markerPaths tường minh cho "${id}" trong features-manifest.json, hoặc\n` +
        `    c) chạy lại với --allow-multi-root (gỡ sẽ chỉ xóa một phần).`
    );
  }

  for (const r of roots) assertSafeMarker(r, id, "suy từ package");
  return {
    markerPaths: roots,
    markerGuids: roots.map((r) => assets.get(r)).filter(Boolean),
  };
}

/** A marker that names a container folder deletes unrelated work on uninstall. Never allow it. */
function assertSafeMarker(path, id, origin) {
  if (MARKER_DENYLIST.has(path))
    throw new Error(
      `${id}: marker "${path}" (${origin}) là thư mục chứa, không phải feature — gỡ feature sẽ xóa cả nó. Khai markerPaths đúng folder feature trong features-manifest.json.`
    );
  if (depth(path) < MIN_MARKER_DEPTH)
    throw new Error(
      `${id}: marker "${path}" (${origin}) chỉ có ${depth(path)} cấp, quá nông cho một feature (tối thiểu ${MIN_MARKER_DEPTH}, vd Assets/_Project/Features/<Domain>/<Feature>). Khai markerPaths tường minh trong features-manifest.json.`
    );
}

/** `<PROJECT>/<Category>/<Feature>.unitypackage` present on this machine. */
function scanDisk(projectId) {
  const projDir = join(FEATURES_DIR, projectId);
  const out = [];
  if (!existsSync(projDir)) return out;
  for (const cat of readdirSync(projDir, { withFileTypes: true })) {
    if (!cat.isDirectory()) continue;
    for (const f of readdirSync(join(projDir, cat.name))) {
      if (extname(f).toLowerCase() !== ".unitypackage") continue;
      out.push({
        category: cat.name,
        name: basename(f, extname(f)),
        fileName: f,
        localPath: join(projDir, cat.name, f),
      });
    }
  }
  return out;
}

/** Every project this repo knows about: on disk, in the committed index, or with a manifest. */
function listProjects() {
  const ids = new Set();
  if (existsSync(FEATURES_DIR)) {
    for (const d of readdirSync(FEATURES_DIR, { withFileTypes: true })) {
      if (!d.isDirectory()) continue;
      if (scanDisk(d.name).length > 0 || existsSync(manifestPath(d.name)) || existsSync(catalogPath(d.name)))
        ids.add(d.name);
    }
  }
  for (const p of readJson(indexPath(), { projects: [] }).projects || []) ids.add(p.id);
  return [...ids].sort();
}

/** Authored metadata for one feature, or an empty object. */
function authoredFor(manifest, id) {
  return (manifest.features && manifest.features[id]) || {};
}

/**
 * Merge one project's catalog entries: authored > local binary > committed catalog.
 * `targets` = feature ids whose binary should be re-read (and re-uploaded); everything else is
 * carried over untouched so a partial clone never truncates the catalog.
 */
function buildEntries(projectId, targets) {
  const manifest = readJson(manifestPath(projectId), {});
  const committed = readJson(catalogPath(projectId), { assets: [] });
  const previous = new Map((committed.assets || []).map((a) => [featureId(a.category, a.name), a]));
  const onDisk = new Map(scanDisk(projectId).map((f) => [featureId(f.category, f.name), f]));

  const ids = new Set([...previous.keys(), ...onDisk.keys()]);
  for (const id of Object.keys(manifest.features || {})) ids.add(id);
  for (const id of REMOVE_FEATURES) ids.delete(id);

  const entries = [];
  for (const id of [...ids].sort()) {
    const [category, name] = [id.slice(0, id.indexOf("/")), id.slice(id.indexOf("/") + 1)];
    const authored = authoredFor(manifest, id);
    const prev = previous.get(id);
    const disk = onDisk.get(id);

    if (!disk && !prev)
      throw new Error(
        `${projectId}/${id}: có trong features-manifest.json nhưng không có binary lẫn entry catalog. Export .unitypackage trước, hoặc bỏ entry khỏi manifest.`
      );

    const fileName = disk?.fileName || prev.fileName || `${name}.unitypackage`;
    const key = r2FileKey(projectId, category, fileName);

    // Recompute from the binary only for the features this run actually targets; everything
    // else keeps the sha256 the committed catalog already published.
    const recompute = disk && targets.has(id);
    let hash = prev?.sha256;
    let markerPaths = authored.markerPaths;
    let markerGuids = authored.markerGuids;

    if (recompute) {
      const bytes = readFileSync(disk.localPath);
      hash = sha256(bytes);
      if (!markerPaths) {
        const derived = deriveMarkers(disk.localPath, `${projectId}/${id}`);
        markerPaths = derived.markerPaths;
        markerGuids = markerGuids || derived.markerGuids;
      } else if (!markerGuids) {
        const assets = readPackageAssets(disk.localPath);
        markerGuids = markerPaths.map((p) => assets.get(p)).filter(Boolean);
      }
    }
    markerPaths = markerPaths || prev?.markerPaths || [];
    markerGuids = markerGuids || prev?.markerGuids || [];
    for (const p of markerPaths) assertSafeMarker(p, `${projectId}/${id}`, "features-manifest.json");

    if (!hash)
      throw new Error(`${projectId}/${id}: thiếu sha256 — binary không có trên máy này, chạy --fetch-missing.`);

    const entry = {
      name: authored.displayName || name,
      fileName,
      category,
      url: publicUrl(key),
      sha256: hash,
      markerPaths,
      markerGuids,
    };
    // Optional fields: emitted only when they carry something. Older Feature Hub clients ignore
    // unknown JSON fields (Newtonsoft), so shipping them early is safe.
    if (authored.description) entry.description = authored.description;
    if (authored.requires?.length) entry.requires = authored.requires;
    if (authored.requiresPackages && Object.keys(authored.requiresPackages).length)
      entry.requiresPackages = authored.requiresPackages;
    if (authored.installedByDefault) entry.installedByDefault = true;

    entries.push({ ...entry, _localPath: disk?.localPath || null, _key: key, _id: id, _upload: recompute });
  }

  entries.sort((a, b) => a.category.localeCompare(b.category) || a.name.localeCompare(b.name));
  validateRequires(projectId, entries);
  return { entries, manifest };
}

/** `requires` must name features in the same catalog and must not form a cycle. */
function validateRequires(projectId, entries) {
  const ids = new Set(entries.map((e) => e._id));
  const graph = new Map(entries.map((e) => [e._id, e.requires || []]));
  for (const [id, deps] of graph) {
    for (const d of deps)
      if (!ids.has(d))
        throw new Error(`${projectId}/${id}: requires "${d}" không có trong catalog của project này.`);
  }
  const state = new Map();
  const walk = (id, trail) => {
    if (state.get(id) === "done") return;
    if (state.get(id) === "open")
      throw new Error(`${projectId}: requires tạo vòng lặp — ${[...trail, id].join(" → ")}`);
    state.set(id, "open");
    for (const d of graph.get(id) || []) walk(d, [...trail, id]);
    state.set(id, "done");
  };
  for (const id of graph.keys()) walk(id, []);
}

/** Which features this run should re-read from disk (and push). */
function resolveTargets(projectId) {
  const onDisk = scanDisk(projectId).map((f) => featureId(f.category, f.name));
  if (ONLY_FEATURES.length > 0) {
    for (const id of ONLY_FEATURES)
      if (!onDisk.includes(id))
        throw new Error(
          `${projectId}: không có binary cho "${id}". Có trên đĩa:\n` +
            onDisk.map((i) => `    - ${i}`).join("\n")
        );
    return new Set(ONLY_FEATURES);
  }
  if (ALL) return new Set(onDisk);
  if (onDisk.length === 1) return new Set(onDisk);
  if (onDisk.length === 0) return new Set();
  throw new Error(
    `${projectId}: có ${onDisk.length} feature trên đĩa — chỉ rõ --feature <Category>/<Name> (lặp lại được) hoặc --all:\n` +
      onDisk.map((i) => `    - ${i}`).join("\n")
  );
}

/** Write a manifest skeleton from the binaries on disk so markers stop being guessed. */
function emitManifest(projectId) {
  const existing = readJson(manifestPath(projectId), {});
  const features = { ...(existing.features || {}) };
  const disk = scanDisk(projectId);
  if (disk.length === 0) throw new Error(`${projectId}: không có .unitypackage nào trên đĩa để dựng manifest.`);

  let filled = 0;
  for (const f of disk.sort((a, b) => a.category.localeCompare(b.category) || a.name.localeCompare(b.name))) {
    const id = featureId(f.category, f.name);
    const prior = features[id] || {};
    if (prior.markerPaths && prior.markerGuids) continue;
    const derived = deriveMarkers(f.localPath, `${projectId}/${id}`);
    features[id] = {
      description: prior.description || "",
      markerPaths: prior.markerPaths || derived.markerPaths,
      markerGuids: prior.markerGuids || derived.markerGuids,
      requires: prior.requires || [],
      requiresPackages: prior.requiresPackages || {},
    };
    filled++;
    console.log(`+ ${id}\n    marker: ${features[id].markerPaths.join(", ")}\n    guid  : ${features[id].markerGuids.join(", ") || "(none)"}`);
  }

  const body = `${JSON.stringify(
    {
      schemaVersion: 1,
      project: projectId,
      displayName: existing.displayName || projectId,
      description:
        existing.description || `${projectId} game features — install per-feature via EZG Feature Hub.`,
      $comment: [
        "Authored metadata per feature, keyed '<Category>/<Name>'. Wins over anything derived",
        "from the .unitypackage. markerPaths = what Feature Hub deletes on uninstall, so it must",
        "name the feature's own folder(s) and never a container like Assets/_Project.",
        "requires = other features in THIS project's catalog; requiresPackages = UPM id -> version.",
      ],
      features,
    },
    null,
    2
  )}\n`;

  if (DRY_RUN) {
    console.log(`\n~ dry-run manifest ${manifestPath(projectId)} (${Object.keys(features).length} feature, ${filled} mới)`);
    return;
  }
  mkdirSync(dirname(manifestPath(projectId)), { recursive: true });
  writeFileSync(manifestPath(projectId), body);
  console.log(`\n+ manifest ${manifestPath(projectId)} (${Object.keys(features).length} feature, ${filled} mới)`);
}

/** Pull binaries named by the committed catalog but missing locally (other machine built them). */
async function fetchMissing(projectId, client) {
  const committed = readJson(catalogPath(projectId), { assets: [] });
  const onDisk = new Set(scanDisk(projectId).map((f) => featureId(f.category, f.name)));
  const missing = (committed.assets || []).filter((a) => !onDisk.has(featureId(a.category, a.name)));
  if (missing.length === 0) {
    console.log(`= ${projectId}: đủ binary trên đĩa, không cần tải.`);
    return;
  }
  console.log(`↓ ${projectId}: thiếu ${missing.length} binary, đang tải từ R2…`);
  for (const a of missing) {
    const key = r2FileKey(projectId, a.category, a.fileName);
    const dest = join(FEATURES_DIR, projectId, a.category, a.fileName);
    if (DRY_RUN) {
      console.log(`~ dry-run tải ${key} → ${dest}`);
      continue;
    }
    const bytes = await getObject(client, key);
    if (!bytes) throw new Error(`Không có trên R2: ${key}`);
    const got = sha256(bytes);
    if (got !== a.sha256)
      throw new Error(`sha256 lệch khi tải ${key}\n  catalog: ${a.sha256}\n  R2     : ${got}`);
    mkdirSync(dirname(dest), { recursive: true });
    writeFileSync(dest, bytes);
    console.log(`  ✓ ${a.category}/${a.name} (${(bytes.length / 1048576).toFixed(1)} MB)`);
  }
}

async function main() {
  if (!existsSync(FEATURES_DIR)) throw new Error(`Not found: ${FEATURES_DIR}`);
  const allProjects = listProjects();
  if (allProjects.length === 0) throw new Error(`Chưa có project nào dưới ${FEATURES_DIR}`);

  if (EMIT_MANIFEST) {
    if (!ONLY_PROJECT) throw new Error("--emit-manifest cần --project <ID>.");
    emitManifest(ONLY_PROJECT);
    return;
  }

  const uploadTargets = ONLY_PROJECT ? [ONLY_PROJECT] : allProjects;
  for (const p of uploadTargets)
    if (!allProjects.includes(p))
      throw new Error(`Project không có: ${p}. Đang có: ${allProjects.join(", ")}`);
  if (REMOVE_FEATURES.length > 0 && !ONLY_PROJECT)
    throw new Error("--remove cần --project <ID> để biết gỡ khỏi catalog nào.");

  const needsClient = !DRY_RUN && !EMIT_ONLY;
  const client = needsClient || FETCH_MISSING ? await makeClient() : null;

  if (FETCH_MISSING) for (const p of uploadTargets) await fetchMissing(p, client);

  const summary = [];
  const indexProjects = [];

  // Build the index from EVERY project known (disk + committed index), but only push payloads
  // and catalog for the requested targets.
  for (const projectId of allProjects) {
    const isTarget = uploadTargets.includes(projectId);
    const targets = isTarget ? resolveTargets(projectId) : new Set();
    const { entries, manifest } = buildEntries(projectId, targets);

    const catalog = {
      schemaVersion: 1,
      project: projectId,
      description: manifest.description || `${projectId} game features — install per-feature via EZG Feature Hub.`,
      assets: entries.map(({ _localPath, _key, _id, _upload, ...pub }) => pub),
    };

    // A project with nothing installable is not a project: drop it from the index instead of
    // listing an empty tab. Also how a fully-removed project disappears (its id survives in the
    // previous index.json, which is exactly what listProjects() reads back).
    if (entries.length === 0) {
      summary.push(`bỏ qua ${projectId} (0 feature)`);
      continue;
    }

    indexProjects.push({
      id: projectId,
      name: manifest.displayName || projectId,
      catalogUrl: publicUrl(r2CatalogKey(projectId)),
      featureCount: entries.length,
      categories: [...new Set(entries.map((e) => e.category))].sort(),
    });

    if (!isTarget) {
      summary.push(`index-only ${projectId} (${entries.length} feature)`);
      continue;
    }

    // 1. payloads — only the features this run targets
    if (!SKIP_FILES && !EMIT_ONLY) {
      for (const e of entries.filter((x) => x._upload)) {
        if (DRY_RUN) {
          console.log(`~ dry-run ${projectId}/${e._id}`);
          console.log(`    key    : ${e._key}`);
          console.log(`    sha256 : ${e.sha256}`);
          console.log(`    marker : ${e.markerPaths.join(", ") || "(none)"}`);
          console.log(`    guid   : ${e.markerGuids.join(", ") || "(none)"}`);
          continue;
        }
        if ((await objectExists(client, e._key)) && !FORCE) {
          console.log(`= skip ${e._key} (đã tồn tại — dùng --force để đè)`);
          continue;
        }
        await putObject(client, e._key, readFileSync(e._localPath), "application/octet-stream");
        console.log(`+ uploaded ${e._key}`);
      }
    }

    // 1b. removals
    for (const id of REMOVE_FEATURES) {
      const [category, name] = [id.slice(0, id.indexOf("/")), id.slice(id.indexOf("/") + 1)];
      const key = r2FileKey(projectId, category, `${name}.unitypackage`);
      if (!PURGE) {
        console.log(`- gỡ khỏi catalog: ${id} (object R2 giữ lại, thêm --purge để xóa hẳn)`);
      } else if (DRY_RUN || EMIT_ONLY) {
        console.log(`~ dry-run xóa R2 ${key}`);
      } else {
        await deleteObject(client, key);
        console.log(`- đã xóa R2 ${key}`);
      }
    }

    // 2. catalog.json (write local + upload)
    const catalogBody = `${JSON.stringify(catalog, null, 2)}\n`;
    if (DRY_RUN) {
      console.log(`~ dry-run catalog ${r2CatalogKey(projectId)} (${entries.length} feature)`);
    } else {
      writeFileSync(catalogPath(projectId), catalogBody);
      if (EMIT_ONLY) {
        console.log(`+ catalog (local) ${catalogPath(projectId)} (${entries.length} feature)`);
      } else {
        await putObject(client, r2CatalogKey(projectId), catalogBody, "application/json");
        console.log(`+ catalog  ${r2CatalogKey(projectId)} (${entries.length} feature)`);
      }
    }
    const pushed = entries.filter((x) => x._upload).length;
    summary.push(`upload ${projectId} (${pushed}/${entries.length} feature đẩy payload)`);
  }

  // 3. index.json — always covers every project known, never just what's on this disk
  const index = {
    schemaVersion: 1,
    description: "EZG Feature Hub — registry of per-project feature catalogs.",
    projects: indexProjects.sort((a, b) => a.id.localeCompare(b.id)),
  };
  const indexBody = `${JSON.stringify(index, null, 2)}\n`;
  if (DRY_RUN) {
    console.log(`~ dry-run index ${r2IndexKey()} (${indexProjects.length} project)`);
  } else {
    writeFileSync(indexPath(), indexBody);
    if (EMIT_ONLY) {
      console.log(`+ index (local) ${indexPath()} (${indexProjects.length} project)`);
    } else {
      await putObject(client, r2IndexKey(), indexBody, "application/json");
      console.log(`+ index    ${r2IndexKey()} (${indexProjects.length} project)`);
    }
  }

  console.log("\n--- summary ---");
  for (const line of summary) console.log(`  ${line}`);
  console.log(`  index: ${indexProjects.map((p) => `${p.id}(${p.featureCount})`).join(", ")}`);
}

main().catch((err) => {
  console.error(err.message || err);
  process.exit(1);
});
