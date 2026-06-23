#!/usr/bin/env node
/**
 * Upload per-project game-feature catalogs (.unitypackage) to Cloudflare R2.
 *
 * Source layout (one folder per project, one subfolder per category):
 *   templates/Features/<PROJECT>/<Category>/<Feature>.unitypackage
 *
 * For every project it scans, this script:
 *   1. Computes SHA-256 of each .unitypackage.
 *   2. Derives a markerPath (the feature's root folder inside the package, e.g.
 *      "Assets/_Project/Features/Events/BattleRoyale") so the Feature Hub can tell
 *      a feature is already installed even without an install-record.
 *   3. Uploads each package to  unity-template/features/<PROJECT>/files/<Category>/<file>
 *      (skips if the object already exists with the same key; --force to overwrite).
 *   4. Generates + uploads  unity-template/features/<PROJECT>/catalog.json
 *   5. Regenerates + uploads unity-template/features/index.json  (covers ALL projects
 *      found on disk so the index never drifts from reality).
 *
 * The generated catalog.json / index.json are also written next to the sources
 * (templates/Features/...) so they are inspectable and committable.
 *
 * Run:
 *   node --env-file=.env upload-unity-template-features.mjs --dry-run
 *   node --env-file=.env upload-unity-template-features.mjs              # all projects
 *   node --env-file=.env upload-unity-template-features.mjs --project M001
 *
 * Flags:
 *   --project <ID>  only (re)upload this project's packages + catalog (index still
 *                   covers every project on disk)
 *   --dry-run       print what would happen, touch nothing on R2 or disk
 *   --force         re-upload packages even if the key already exists
 *   --skip-files    upload catalogs + index only, not the .unitypackage payloads
 */
import { execFileSync } from "node:child_process";
import { createHash } from "node:crypto";
import { existsSync, readdirSync, readFileSync, writeFileSync } from "node:fs";
import { basename, extname, join } from "node:path";
import {
  BUCKET,
  REPO_ROOT,
  makeClient,
  objectExists,
  putObject,
  hasFlag,
} from "./registry-lib.mjs";

const FEATURES_DIR = join(REPO_ROOT, "templates", "Features");
// Public read root, e.g. https://pub-xxxx.r2.dev/unity-template
const PUBLIC_ROOT = (
  process.env.UNITY_TEMPLATE_PUBLIC_BASE_URL ||
  "https://pub-d76b7e028ac14f9bb044ebd65bccd3d9.r2.dev/unity-template/files"
).replace(/\/files\/?$/, "");
const R2_FEATURES_PREFIX = (process.env.UNITY_TEMPLATE_FEATURES_PREFIX || "unity-template/features")
  .replace(/^\/+|\/+$/g, "");

const DRY_RUN = hasFlag("--dry-run");
const FORCE = hasFlag("--force");
const SKIP_FILES = hasFlag("--skip-files");
const ONLY_PROJECT = (() => {
  const i = process.argv.indexOf("--project");
  return i !== -1 ? process.argv[i + 1] : null;
})();

const sha256 = (path) => createHash("sha256").update(readFileSync(path)).digest("hex");

/** Public URL for an R2 key (path-encoded, spaces & friends escaped per segment). */
function publicUrl(key) {
  const encoded = key
    .split("/")
    .map((seg) => encodeURIComponent(seg))
    .join("/");
  // key already starts with "unity-template/..."; PUBLIC_ROOT ends with "/unity-template"
  return `${PUBLIC_ROOT.replace(/\/unity-template$/, "")}/${encoded}`;
}

/**
 * Read the asset paths recorded inside a .unitypackage (each asset has a
 * `<guid>/pathname` member whose content is a project-relative path) and return
 * the feature's root folder. Every path begins with "Assets/", so splitting the
 * concatenated blob on that marker is robust even when pathname files lack a
 * trailing newline. We pick the shallowest path (the feature root).
 */
function deriveMarkerPath(pkgPath) {
  let blob;
  try {
    // bsdtar (macOS) matches glob members by default; GNU tar also accepts this form.
    blob = execFileSync("tar", ["-xzOf", pkgPath, "*/pathname"], {
      maxBuffer: 256 * 1024 * 1024,
    }).toString("utf8");
  } catch {
    return null;
  }
  const paths = blob
    .split("Assets/")
    .slice(1)
    .map((tail) => ("Assets/" + tail).split(/[\r\n]/)[0].trim())
    .filter(Boolean);
  if (paths.length === 0) return null;
  // Shallowest path = feature root (fewest segments, then shortest string).
  paths.sort((a, b) => a.split("/").length - b.split("/").length || a.length - b.length);
  return paths[0];
}

/** List <project>/<category>/<file>.unitypackage as catalog entries. */
function scanProject(projectId) {
  const projDir = join(FEATURES_DIR, projectId);
  const entries = [];
  for (const cat of readdirSync(projDir, { withFileTypes: true })) {
    if (!cat.isDirectory()) continue;
    const catDir = join(projDir, cat.name);
    for (const f of readdirSync(catDir)) {
      if (extname(f).toLowerCase() !== ".unitypackage") continue;
      const filePath = join(catDir, f);
      const fileName = f;
      const key = `${R2_FEATURES_PREFIX}/${projectId}/files/${cat.name}/${fileName}`;
      const marker = deriveMarkerPath(filePath);
      entries.push({
        name: basename(f, extname(f)),
        fileName,
        category: cat.name,
        url: publicUrl(key),
        sha256: sha256(filePath),
        markerPaths: marker ? [marker] : [],
        _localPath: filePath,
        _key: key,
      });
    }
  }
  entries.sort((a, b) => a.category.localeCompare(b.category) || a.name.localeCompare(b.name));
  return entries;
}

/** Projects on disk = subdirs of FEATURES_DIR that contain at least one .unitypackage. */
function listProjects() {
  if (!existsSync(FEATURES_DIR)) throw new Error(`Not found: ${FEATURES_DIR}`);
  return readdirSync(FEATURES_DIR, { withFileTypes: true })
    .filter((d) => d.isDirectory())
    .map((d) => d.name)
    .filter((id) => {
      const sub = join(FEATURES_DIR, id);
      return readdirSync(sub, { withFileTypes: true }).some(
        (c) => c.isDirectory() &&
          readdirSync(join(sub, c.name)).some((f) => extname(f).toLowerCase() === ".unitypackage")
      );
    })
    .sort();
}

async function main() {
  const allProjects = listProjects();
  if (allProjects.length === 0) throw new Error(`No projects with .unitypackage under ${FEATURES_DIR}`);

  const uploadTargets = ONLY_PROJECT ? [ONLY_PROJECT] : allProjects;
  for (const p of uploadTargets) {
    if (!allProjects.includes(p)) throw new Error(`Project not found on disk: ${p}`);
  }

  const client = DRY_RUN ? null : await makeClient();
  const summary = [];
  const indexProjects = [];

  // Build the index from EVERY project on disk (scan is cheap relative to upload),
  // but only push package payloads + catalog for the requested targets.
  for (const projectId of allProjects) {
    const entries = scanProject(projectId);
    const catalog = {
      schemaVersion: 1,
      project: projectId,
      description: `${projectId} game features — install per-feature via EZG Feature Hub.`,
      assets: entries.map(({ _localPath, _key, ...pub }) => pub),
    };

    const catalogKey = `${R2_FEATURES_PREFIX}/${projectId}/catalog.json`;
    indexProjects.push({
      id: projectId,
      name: projectId,
      catalogUrl: publicUrl(catalogKey),
      featureCount: entries.length,
      categories: [...new Set(entries.map((e) => e.category))].sort(),
    });

    const isTarget = uploadTargets.includes(projectId);
    if (!isTarget) {
      summary.push(`index-only ${projectId} (${entries.length} features)`);
      continue;
    }

    // 1. payloads
    if (!SKIP_FILES) {
      for (const e of entries) {
        if (DRY_RUN) {
          console.log(`~ dry-run ${projectId}/${e.category}/${e.fileName}`);
          console.log(`    key    : ${e._key}`);
          console.log(`    sha256 : ${e.sha256}`);
          console.log(`    marker : ${e.markerPaths[0] || "(none)"}`);
          continue;
        }
        if ((await objectExists(client, e._key)) && !FORCE) {
          console.log(`= skip ${e._key} (exists)`);
          continue;
        }
        await putObject(client, e._key, readFileSync(e._localPath), "application/octet-stream");
        console.log(`+ uploaded ${e._key}`);
      }
    }

    // 2. catalog.json (write local + upload)
    const catalogBody = `${JSON.stringify(catalog, null, 2)}\n`;
    const catalogLocal = join(FEATURES_DIR, projectId, "catalog.json");
    if (DRY_RUN) {
      console.log(`~ dry-run catalog ${catalogKey} (${entries.length} features)`);
    } else {
      writeFileSync(catalogLocal, catalogBody);
      await putObject(client, catalogKey, catalogBody, "application/json");
      console.log(`+ catalog  ${catalogKey} (${entries.length} features)`);
    }
    summary.push(`upload ${projectId} (${entries.length} features)`);
  }

  // 3. index.json (write local + upload) — always reflects all projects on disk
  const index = {
    schemaVersion: 1,
    description: "EZG Feature Hub — registry of per-project feature catalogs.",
    projects: indexProjects.sort((a, b) => a.id.localeCompare(b.id)),
  };
  const indexBody = `${JSON.stringify(index, null, 2)}\n`;
  const indexKey = `${R2_FEATURES_PREFIX}/index.json`;
  const indexLocal = join(FEATURES_DIR, "index.json");
  if (DRY_RUN) {
    console.log(`~ dry-run index ${indexKey} (${indexProjects.length} projects)`);
  } else {
    writeFileSync(indexLocal, indexBody);
    await putObject(client, indexKey, indexBody, "application/json");
    console.log(`+ index    ${indexKey} (${indexProjects.length} projects)`);
  }

  console.log("\n--- summary ---");
  for (const line of summary) console.log(`  ${line}`);
  console.log(`  index: ${indexProjects.map((p) => `${p.id}(${p.featureCount})`).join(", ")}`);
}

main().catch((err) => {
  console.error(err.message || err);
  process.exit(1);
});
