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
 * The tarball also carries DefaultSetup/.claude/ezg-base.lock.json — generated here, never committed:
 * the per-file hashes of every base file exactly as shipped (same items/hashes as the AI catalog,
 * via ai-catalog-lib.mjs). A freshly generated project therefore knows from day one which files are
 * the base's, so the edit guard works immediately and /update-ai can tell its own edits from upstream.
 *
 * Run:
 *   node --env-file=.env upload-unity-template-defaultsetup.mjs --dry-run
 *   node --env-file=.env upload-unity-template-defaultsetup.mjs
 *
 * Needs the same R2_* env vars as the other publish tooling (see scripts/.env).
 */
import { createHash } from "node:crypto";
import { execFileSync } from "node:child_process";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { tmpdir } from "node:os";
import { BUCKET, REPO_ROOT, makeClient, putObject, hasFlag } from "./registry-lib.mjs";
import { LOCK_REL, buildLock, collectItems, packItem } from "./ai-catalog-lib.mjs";

const TEMPLATE_DIR = join(REPO_ROOT, "templates", "unity-project");
const DEFAULT_SETUP_DIR = join(TEMPLATE_DIR, "DefaultSetup");

const TGZ_KEY = (process.env.UNITY_TEMPLATE_DEFAULT_SETUP_R2_KEY || "unity-template/defaultsetup.tgz").replace(/^\/+/, "");
const SHA_KEY = `${TGZ_KEY}.sha256`;
const PUBLIC_URL = process.env.UNITY_TEMPLATE_DEFAULT_SETUP_PUBLIC_URL ||
  "https://upm-registry-worker.developer-a1f.workers.dev/template/defaultsetup.tgz";

const DRY_RUN = hasFlag("--dry-run");

function buildTarball() {
  if (!existsSync(join(DEFAULT_SETUP_DIR, "ProjectSettings"))) {
    throw new Error(`DefaultSetup/ProjectSettings not found at: ${DEFAULT_SETUP_DIR}`);
  }
  const work = mkdtempSync(join(tmpdir(), "ezg-defaultsetup-"));
  const out = join(work, "defaultsetup.tgz");

  // The base lock lives beside the real tree in a scratch folder and joins the archive under the
  // same DefaultSetup/ prefix (a second -C below), so the repo itself never holds a generated file.
  const lockRoot = join(work, "lock");
  const lockFile = join(lockRoot, "DefaultSetup", LOCK_REL);
  const items = collectItems({ onlyDefaultSetup: true }).map(packItem);
  const lock = buildLock(items,
    "Baseline của các file thuộc base EZG — /update-ai và Feature Hub ghi file này, đừng sửa tay. " +
    "Commit cùng repo. Phần riêng của project: CLAUDE_PROJECT.md, .claude/project/ (xem rule base-ownership).");
  mkdirSync(dirname(lockFile), { recursive: true });
  writeFileSync(lockFile, `${JSON.stringify(lock, null, 2)}\n`);
  console.log(`lock  : ${Object.keys(lock.files).length} base files in ${Object.keys(lock.items).length} items`);
  // Archive root is the DefaultSetup/ folder itself, so the logic extracts DefaultSetup/ (ProjectSettings/
  // plus the baked-in tooling like .claude/, .agents/, .mcp.json).
  //   --no-xattrs + COPYFILE_DISABLE : keep macOS resource forks (._*) out of the tarball.
  //   --exclude=.agents              : the .agents/ link view is NOT shipped. Symlinks don't reliably
  //                                     extract on Windows, and dereferencing them (what this script
  //                                     used to do) ships a full COPY of .claude/ that silently
  //                                     diverges the moment anyone edits a skill. bootstrap.sh /
  //                                     bootstrap.ps1 create real links in the generated project.
  //   --exclude                      : drop junk / per-developer files that must not be distributed.
  //   --exclude=CLAUDE_PROJECT.md    : a game project's own half of CLAUDE.md never ships — the
  //                                     builder must not overwrite it, and bootstrap seeds a fresh
  //                                     one from .claude/docs/CLAUDE_PROJECT.template.md instead.
  execFileSync("tar", [
    "--no-xattrs",
    "--exclude=.agents",
    "--exclude=CLAUDE_PROJECT.md",
    "--exclude=.DS_Store", "--exclude=settings.local.json",
    "-czf", out, "-C", TEMPLATE_DIR, "DefaultSetup",
    "-C", lockRoot, `DefaultSetup/${LOCK_REL}`,
  ], {
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
