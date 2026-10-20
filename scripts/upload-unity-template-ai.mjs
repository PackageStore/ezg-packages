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
 *   4. Uploads every file version not seen before to  unity-template/ai/blobs/<nsha>  and merges
 *      them into  unity-template/ai/history.json  ({ path: [nsha, ...] }, git history included —
 *      CI checks out with fetch-depth: 0). /update-ai + Feature Hub use them to tell an older base
 *      version from a project's in-place edit, and to fetch the exact baseline for a 3-way diff.
 *   5. Regenerates + uploads  unity-template/ai/index.json  (categories + every item on disk; each
 *      item carries `fileHashes` {path: nsha} and `ownership` "base" | "merge").
 *
 * Scanning, packing and hashing live in ai-catalog-lib.mjs (shared with the defaultsetup.tgz
 * publisher, which bakes the same per-file hashes into .claude/ezg-base.lock.json).
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
 *   --skip-files          upload index.json only, not the .zip payloads (nor history/blobs)
 *   --local-out <dir>     write everything into <dir> instead of R2 (offline mirror for tests)
 */
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { getJson, makeClient, putObject, hasFlag } from "./registry-lib.mjs";
import {
  CATEGORY_BY_ID,
  EXTRA_DIR,
  DEFAULT_SETUP_DIR,
  collectGitHistory,
  collectItems,
  loadManifest,
  normalizeContent,
  packItem,
} from "./ai-catalog-lib.mjs";

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
// --local-out <dir>: write the whole catalog (index, history, blobs, zips) into a local folder
// instead of R2 — an offline mirror for tests (ezg_base.py --source <dir>). Never touches the repo.
const LOCAL_OUT = argValue("--local-out");
const BLOB_CONCURRENCY = 16;

/** Public URL for an R2 key (path-encoded per segment, same rule as the feature catalogs). */
function publicUrl(key) {
  const encoded = key
    .replace(/^unity-template\//, "")
    .split("/")
    .map((seg) => encodeURIComponent(seg))
    .join("/");
  return `${PUBLIC_ROOT.replace(/\/+$/, "")}/${encoded}`;
}

/** R2 (or --local-out folder) access, so every write below has one code path. */
const localPath = (key) => join(LOCAL_OUT, key.slice(R2_AI_PREFIX.length + 1));
async function storeGetJson(client, key) {
  if (LOCAL_OUT) {
    try {
      return JSON.parse(readFileSync(localPath(key), "utf8"));
    } catch {
      return null;
    }
  }
  return getJson(client, key);
}
async function storePut(client, key, body, contentType) {
  if (LOCAL_OUT) {
    mkdirSync(dirname(localPath(key)), { recursive: true });
    writeFileSync(localPath(key), body);
    return;
  }
  await putObject(client, key, body, contentType);
}

/** Run `fn` over `list` with at most `limit` in flight. */
async function eachLimit(list, limit, fn) {
  let next = 0;
  const workers = Array.from({ length: Math.min(limit, list.length) }, async () => {
    while (next < list.length) await fn(list[next++]);
  });
  await Promise.all(workers);
}

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------

async function main() {
  if (!existsSync(DEFAULT_SETUP_DIR)) throw new Error(`Not found: ${DEFAULT_SETUP_DIR}`);
  const manifest = loadManifest();

  const items = collectItems({ manifest });
  if (items.length === 0) throw new Error("Không tìm thấy AI item nào để publish.");

  // Pack + hash every item (cheap: a few MB of text), so the index always reflects disk.
  for (const item of items) {
    packItem(item);
    const key = `${R2_AI_PREFIX}/files/${item.category}/${item.fileName}`;
    item.url = publicUrl(key);
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
    if (!LOCAL_OUT) client = await makeClient();
    const live = await storeGetJson(client, indexKey);
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
      await storePut(client, item._key, item._zip, "application/zip");
      console.log(`+ uploaded ${item._key}  (${item.fileCount} files, ${state})`);
      uploaded++;
    }
    if (skipped > 0)
      console.log(`= ${skipped} item không đổi so với catalog live — bỏ qua payload.`);
  }

  // 2. history.json + blobs/<nsha> — every version ever published for every path, content-addressed.
  //    /update-ai and Feature Hub use them to tell "an older base version" from "edited by the
  //    project" (a hash in history = untouched), and to fetch the exact baseline for a 3-way diff.
  //    Uploaded BEFORE index.json so a client reading the new index always finds them.
  const history = SKIP_FILES ? null : await publishHistory(client, items, manifest);

  // 3. index.json — always covers EVERY item on disk so the catalog never drifts.
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
    items: items.map(({ _zip, _key, _rootAbs, _stagingPrefix, _contents, ...pub }) => pub),
  };
  const indexBody = `${JSON.stringify(index, null, 2)}\n`;
  const indexLocal = join(EXTRA_DIR, "index.json");

  if (DRY_RUN) {
    console.log(`~ dry-run index ${indexKey} (${items.length} items, ${categories.length} categories)`);
  } else {
    if (!LOCAL_OUT) {
      mkdirSync(EXTRA_DIR, { recursive: true });
      writeFileSync(indexLocal, indexBody);
    }
    await storePut(client, indexKey, indexBody, "application/json");
    console.log(`+ index    ${indexKey} (${items.length} items)`);
  }

  console.log("\n--- summary ---");
  for (const cat of categories) console.log(`  ${cat.id.padEnd(10)} ${cat.count} item`);
  console.log(`  total: ${items.length} item`);
  if (!DRY_RUN && !SKIP_FILES)
    console.log(`  payload: ${uploaded} uploaded, ${skipped} unchanged`);
  if (history)
    console.log(`  history: ${history.paths} path, ${history.versions} phiên bản, ${history.uploaded} blob mới`);
}

/**
 * Merge the current catalog files + git history into the live history.json and upload every blob
 * the bucket does not have yet. Idempotent: a re-run with nothing new uploads nothing.
 */
async function publishHistory(client, items, manifest) {
  const historyKey = `${R2_AI_PREFIX}/history.json`;
  let live = null;
  if (client || LOCAL_OUT) {
    try {
      live = await storeGetJson(client, historyKey);
    } catch (err) {
      if (!DRY_RUN) throw err;
    }
  }
  const files = { ...(live?.files || {}) };
  const known = new Set(Object.values(files).flat());
  const before = JSON.stringify(files);

  const versions = collectGitHistory(manifest) || [];
  for (const item of items)
    for (const [path, data] of item._contents) versions.push({ path, hash: item.fileHashes[path], data });

  const toUpload = new Map();
  for (const { path, hash, data } of versions) {
    const list = (files[path] ||= []);
    if (!list.includes(hash)) list.push(hash);
    if (!known.has(hash)) {
      known.add(hash);
      toUpload.set(hash, data);
    }
  }
  // Blobs are stored normalized (CRLF→LF for text), so nsha(blob) is its own key.
  const paths = Object.keys(files).length;
  const count = Object.values(files).reduce((n, l) => n + l.length, 0);

  if (DRY_RUN) {
    console.log(`~ dry-run history: ${paths} path, ${count} phiên bản, ${toUpload.size} blob mới`);
    return { paths, versions: count, uploaded: toUpload.size };
  }
  let done = 0;
  await eachLimit([...toUpload], BLOB_CONCURRENCY, async ([hash, data]) => {
    await storePut(client, `${R2_AI_PREFIX}/blobs/${hash}`, normalizeContent(data), "application/octet-stream");
    if (++done % 200 === 0) console.log(`  … ${done}/${toUpload.size} blob`);
  });
  if (JSON.stringify(files) !== before) {
    const sorted = Object.fromEntries(Object.keys(files).sort().map((k) => [k, files[k]]));
    const body = `${JSON.stringify({ schemaVersion: 1, files: sorted })}\n`;
    await storePut(client, historyKey, body, "application/json");
    console.log(`+ history  ${historyKey} (${paths} path, ${toUpload.size} blob mới)`);
  }
  return { paths, versions: count, uploaded: toUpload.size };
}

main().catch((err) => {
  console.error(err.message || err);
  process.exit(1);
});
