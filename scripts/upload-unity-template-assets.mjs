#!/usr/bin/env node
/**
 * Upload Unity project template assets to Cloudflare R2.
 *
 * Reads templates/unity-project/unity-template.json and uploads every file listed
 * in files.localPackages and files.unityPackages under a dedicated R2 prefix.
 *
 * Run:
 *   node upload-unity-template-assets.mjs --dry-run
 *   node upload-unity-template-assets.mjs
 *
 * Optional:
 *   UNITY_TEMPLATE_R2_PREFIX=unity-template/files
 *   UNITY_TEMPLATE_MANIFEST_R2_KEY=unity-template/latest.json
 *   UNITY_TEMPLATE_PUBLIC_BASE_URL=https://example.com/unity-template/files
 *   UNITY_TEMPLATE_MANIFEST_PUBLIC_URL=https://example.com/unity-template/latest.json
 *   node upload-unity-template-assets.mjs --update-urls
 */
import { createHash } from "node:crypto";
import { existsSync, readFileSync, writeFileSync } from "node:fs";
import { basename, join } from "node:path";
import {
  BUCKET,
  REPO_ROOT,
  makeClient,
  objectExists,
  putObject,
  hasFlag,
} from "./registry-lib.mjs";

const TEMPLATE_PATH = join(REPO_ROOT, "templates", "unity-project", "unity-template.json");
const PACKAGE_TEMPLATE_DIR = join(REPO_ROOT, "templates", "unity-project", "PackageTemplate");
const PREFIX = (process.env.UNITY_TEMPLATE_R2_PREFIX || "unity-template/files").replace(/^\/+|\/+$/g, "");
const MANIFEST_KEY = (process.env.UNITY_TEMPLATE_MANIFEST_R2_KEY || "unity-template/latest.json").replace(/^\/+/, "");
const PUBLIC_BASE_URL = (process.env.UNITY_TEMPLATE_PUBLIC_BASE_URL || "").replace(/\/+$/, "");
const MANIFEST_PUBLIC_URL = process.env.UNITY_TEMPLATE_MANIFEST_PUBLIC_URL || "";
const DRY_RUN = hasFlag("--dry-run");
const FORCE = hasFlag("--force");
const UPDATE_URLS = hasFlag("--update-urls");
const SKIP_MANIFEST = hasFlag("--skip-manifest");

function contentTypeFor(fileName) {
  if (fileName.endsWith(".json")) return "application/json";
  if (fileName.endsWith(".tgz")) return "application/gzip";
  if (fileName.endsWith(".unitypackage")) return "application/octet-stream";
  return "application/octet-stream";
}

function sha256(path) {
  return createHash("sha256").update(readFileSync(path)).digest("hex");
}

function encodeKeyForUrl(key) {
  return key.split("/").map(encodeURIComponent).join("/");
}

function collectEntries(template) {
  const files = template.files || {};
  const groups = [
    ["localPackages", files.localPackages || []],
    ["unityPackages", files.unityPackages || []],
  ];

  const entries = [];
  for (const [kind, items] of groups) {
    if (!Array.isArray(items)) throw new Error(`files.${kind} must be an array`);
    for (const entry of items) {
      if (!entry || typeof entry !== "object") throw new Error(`files.${kind} entries must be objects`);
      if (!entry.fileName) throw new Error(`files.${kind} entry missing fileName`);
      entries.push({ kind, entry });
    }
  }
  return entries;
}

async function main() {
  const template = JSON.parse(readFileSync(TEMPLATE_PATH, "utf8"));
  const entries = collectEntries(template);
  const client = DRY_RUN ? null : await makeClient();
  const summary = [];

  for (const { kind, entry } of entries) {
    const fileName = entry.fileName;
    const sourcePath = join(PACKAGE_TEMPLATE_DIR, fileName);
    if (!existsSync(sourcePath)) throw new Error(`Missing local template file: ${sourcePath}`);

    const actualSha = sha256(sourcePath);
    if (entry.sha256 && entry.sha256.toLowerCase() !== actualSha) {
      throw new Error(`SHA-256 mismatch for ${fileName}: expected ${entry.sha256}, got ${actualSha}`);
    }
    entry.sha256 = actualSha;

    const key = `${PREFIX}/${basename(fileName)}`;
    const url = PUBLIC_BASE_URL ? `${PUBLIC_BASE_URL}/${encodeURIComponent(basename(fileName))}` : "";
    if (UPDATE_URLS && url) entry.url = url;

    if (DRY_RUN) {
      console.log(`~ dry-run ${kind}: ${fileName}`);
      console.log(`    key    : ${key}`);
      console.log(`    sha256 : ${actualSha}`);
      if (url) console.log(`    url    : ${url}`);
      summary.push(`dry-run ${fileName}`);
      continue;
    }

    const exists = await objectExists(client, key);
    if (exists && !FORCE) {
      console.log(`= skip ${fileName} (already exists at ${key})`);
      summary.push(`skip    ${fileName}`);
      continue;
    }

    await putObject(client, key, readFileSync(sourcePath), contentTypeFor(fileName));
    console.log(`+ uploaded ${fileName} -> r2://${BUCKET}/${key}`);
    summary.push(`upload  ${fileName}`);
  }

  if (UPDATE_URLS) {
    if (!PUBLIC_BASE_URL) throw new Error("--update-urls requires UNITY_TEMPLATE_PUBLIC_BASE_URL");
    if (DRY_RUN) {
      console.log("~ dry-run update unity-template.json urls");
    } else {
      writeFileSync(TEMPLATE_PATH, `${JSON.stringify(template, null, 2)}\n`, "utf8");
      console.log(`+ updated ${TEMPLATE_PATH}`);
    }
  }

  if (!SKIP_MANIFEST) {
    const manifestBody = `${JSON.stringify(template, null, 2)}\n`;
    if (DRY_RUN) {
      console.log(`~ dry-run manifest: ${MANIFEST_KEY}`);
      if (MANIFEST_PUBLIC_URL) console.log(`    url    : ${MANIFEST_PUBLIC_URL}`);
      summary.push(`dry-run ${MANIFEST_KEY}`);
    } else {
      await putObject(client, MANIFEST_KEY, manifestBody, "application/json");
      console.log(`+ uploaded manifest -> r2://${BUCKET}/${MANIFEST_KEY}`);
      summary.push(`upload  ${MANIFEST_KEY}`);
    }
  }

  console.log("\n--- summary ---");
  for (const line of summary) console.log(`  ${line}`);
  if (!PUBLIC_BASE_URL) {
    console.log("\nNo UNITY_TEMPLATE_PUBLIC_BASE_URL was set, so unity-template.json urls were not updated.");
    console.log(`Uploaded keys can be served later from: ${encodeKeyForUrl(PREFIX)}/<encoded-file-name>`);
  }
  if (!MANIFEST_PUBLIC_URL && !SKIP_MANIFEST) {
    console.log(`Manifest key: ${MANIFEST_KEY}`);
  }
}

main().catch((err) => {
  console.error(err.message || err);
  process.exit(1);
});
