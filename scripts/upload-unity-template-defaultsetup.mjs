#!/usr/bin/env node
/**
 * Publish the Unity template DefaultSetup folder to Cloudflare R2.
 *
 * DefaultSetup/ProjectSettings holds the baked-in tags/layers/build settings that the builder
 * force-copies on top of every generated project. End users only keep the thin bootstrap + manifest,
 * so this folder must travel with the build: the logic (build_unity_template.logic.sh) downloads
 * defaultsetup.tgz from R2 whenever there is no local DefaultSetup/ beside the bootstrap.
 *
 * Uploads:
 *   unity-template/defaultsetup.tgz         (gzipped tar of the DefaultSetup/ folder)
 *   unity-template/defaultsetup.tgz.sha256  (its SHA-256, text/plain -- logic verifies against this)
 *
 * Run:
 *   node --env-file=.env upload-unity-template-defaultsetup.mjs --dry-run
 *   node --env-file=.env upload-unity-template-defaultsetup.mjs
 *
 * Needs the same R2_* env vars as the other publish tooling (see scripts/.env).
 */
import { createHash } from "node:crypto";
import { execFileSync } from "node:child_process";
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { tmpdir } from "node:os";
import { BUCKET, REPO_ROOT, makeClient, putObject, hasFlag } from "./registry-lib.mjs";

const TEMPLATE_DIR = join(REPO_ROOT, "templates", "unity-project");
const DEFAULT_SETUP_DIR = join(TEMPLATE_DIR, "DefaultSetup");

const TGZ_KEY = (process.env.UNITY_TEMPLATE_DEFAULT_SETUP_R2_KEY || "unity-template/defaultsetup.tgz").replace(/^\/+/, "");
const SHA_KEY = `${TGZ_KEY}.sha256`;
const PUBLIC_URL = process.env.UNITY_TEMPLATE_DEFAULT_SETUP_PUBLIC_URL ||
  "https://pub-d76b7e028ac14f9bb044ebd65bccd3d9.r2.dev/unity-template/defaultsetup.tgz";

const DRY_RUN = hasFlag("--dry-run");

function buildTarball() {
  if (!existsSync(join(DEFAULT_SETUP_DIR, "ProjectSettings"))) {
    throw new Error(`DefaultSetup/ProjectSettings not found at: ${DEFAULT_SETUP_DIR}`);
  }
  const work = mkdtempSync(join(tmpdir(), "ezg-defaultsetup-"));
  const out = join(work, "defaultsetup.tgz");
  // Archive root is the DefaultSetup/ folder itself, so the logic extracts DefaultSetup/ProjectSettings/.
  // COPYFILE_DISABLE + --no-xattrs keep macOS resource forks (._*) out of the tarball.
  execFileSync("tar", ["--no-xattrs", "-czf", out, "-C", TEMPLATE_DIR, "DefaultSetup"], {
    env: { ...process.env, COPYFILE_DISABLE: "1" },
    stdio: ["ignore", "ignore", "inherit"],
  });
  const body = readFileSync(out);
  rmSync(work, { recursive: true, force: true });
  return body;
}

async function main() {
  const tgz = buildTarball();
  const sha = createHash("sha256").update(tgz).digest("hex");

  console.log(`source: ${DEFAULT_SETUP_DIR}`);
  console.log(`size  : ${tgz.length} bytes`);
  console.log(`sha256: ${sha}`);
  console.log(`keys  : ${TGZ_KEY}`);
  console.log(`        ${SHA_KEY}`);

  if (DRY_RUN) {
    console.log("\n~ dry-run: nothing uploaded.");
    return;
  }

  const client = await makeClient();
  await putObject(client, TGZ_KEY, tgz, "application/gzip");
  await putObject(client, SHA_KEY, `${sha}\n`, "text/plain");
  console.log(`\n+ uploaded tarball -> r2://${BUCKET}/${TGZ_KEY}`);
  console.log(`+ uploaded sha256  -> r2://${BUCKET}/${SHA_KEY}`);
  console.log(`\nLive: ${PUBLIC_URL}`);
  console.log("Fresh machines will fetch this DefaultSetup on their next run.");
}

main().catch((err) => {
  console.error(err.message || err);
  process.exit(1);
});
