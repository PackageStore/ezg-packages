#!/usr/bin/env node
/**
 * Upload Unity optional-asset catalog + its .unitypackage files to Cloudflare R2.
 *
 * Reads templates/unity-project/asset-catalog.json and uploads every asset that has
 * a local file in PackageTemplate/ under unity-template/files/<fileName>, then uploads
 * the catalog itself to unity-template/asset-catalog.json. Files already declared in
 * unity-template.json (installedByDefault) typically live on R2/external URLs already
 * and are skipped when no local file is present.
 *
 * Run:
 *   node --env-file=.env upload-unity-template-catalog.mjs --dry-run
 *   node --env-file=.env upload-unity-template-catalog.mjs
 *
 * Flags: --dry-run, --force (re-upload existing), --skip-catalog (assets only)
 */
import { createHash } from "node:crypto";
import { existsSync, readFileSync } from "node:fs";
import { basename, join } from "node:path";
import {
  BUCKET,
  REPO_ROOT,
  makeClient,
  objectExists,
  putObject,
  hasFlag,
} from "./registry-lib.mjs";

const CATALOG_PATH = join(REPO_ROOT, "templates", "unity-project", "asset-catalog.json");
const PACKAGE_TEMPLATE_DIR = join(REPO_ROOT, "templates", "unity-project", "PackageTemplate");
const PREFIX = (process.env.UNITY_TEMPLATE_R2_PREFIX || "unity-template/files").replace(/^\/+|\/+$/g, "");
const CATALOG_KEY = (process.env.UNITY_TEMPLATE_CATALOG_R2_KEY || "unity-template/asset-catalog.json").replace(/^\/+/, "");
const DRY_RUN = hasFlag("--dry-run");
const FORCE = hasFlag("--force");
const SKIP_CATALOG = hasFlag("--skip-catalog");

function sha256(path) {
  return createHash("sha256").update(readFileSync(path)).digest("hex");
}

async function main() {
  const catalog = JSON.parse(readFileSync(CATALOG_PATH, "utf8"));
  if (!Array.isArray(catalog.assets)) throw new Error("asset-catalog.json: missing assets[]");
  const client = DRY_RUN ? null : await makeClient();
  const summary = [];

  for (const asset of catalog.assets) {
    if (!asset.fileName) throw new Error("catalog entry missing fileName");
    const fileName = asset.fileName;
    const sourcePath = join(PACKAGE_TEMPLATE_DIR, fileName);

    // No local file → assume it is already hosted (external/default deps). Keep as-is.
    if (!existsSync(sourcePath)) {
      console.log(`~ external: ${fileName} (no local file, keeping url)`);
      summary.push(`external ${fileName}`);
      continue;
    }

    const actualSha = sha256(sourcePath);
    if (asset.sha256 && asset.sha256.toLowerCase() !== actualSha) {
      throw new Error(`SHA-256 mismatch for ${fileName}: catalog ${asset.sha256}, got ${actualSha}`);
    }

    const key = `${PREFIX}/${basename(fileName)}`;
    if (DRY_RUN) {
      console.log(`~ dry-run: ${fileName}`);
      console.log(`    key    : ${key}`);
      console.log(`    sha256 : ${actualSha}`);
      summary.push(`dry-run ${fileName}`);
      continue;
    }

    if ((await objectExists(client, key)) && !FORCE) {
      console.log(`= skip ${fileName} (already at ${key})`);
      summary.push(`skip    ${fileName}`);
      continue;
    }

    await putObject(client, key, readFileSync(sourcePath), "application/octet-stream");
    console.log(`+ uploaded ${fileName} -> r2://${BUCKET}/${key}`);
    summary.push(`upload  ${fileName}`);
  }

  if (!SKIP_CATALOG) {
    const body = `${JSON.stringify(catalog, null, 2)}\n`;
    if (DRY_RUN) {
      console.log(`~ dry-run catalog: ${CATALOG_KEY}`);
      summary.push(`dry-run ${CATALOG_KEY}`);
    } else {
      await putObject(client, CATALOG_KEY, body, "application/json");
      console.log(`+ uploaded catalog -> r2://${BUCKET}/${CATALOG_KEY}`);
      summary.push(`upload  ${CATALOG_KEY}`);
    }
  }

  console.log("\n--- summary ---");
  for (const line of summary) console.log(`  ${line}`);
}

main().catch((err) => {
  console.error(err.message || err);
  process.exit(1);
});
