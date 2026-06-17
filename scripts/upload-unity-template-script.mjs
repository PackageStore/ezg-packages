#!/usr/bin/env node
/**
 * Publish the Unity template builder LOGIC to Cloudflare R2.
 *
 * The end users keep a tiny, near-immutable bootstrap (build_unity_template.sh). On every run that
 * bootstrap downloads the real logic (build_unity_template.logic.sh) from R2 and verifies it against
 * a published SHA-256 sidecar. So whenever you edit the logic, run this to push the new version --
 * every user picks it up automatically on their next run, without replacing any local file.
 *
 * Uploads:
 *   unity-template/build_unity_template.logic.sh         (the logic, text/x-shellscript)
 *   unity-template/build_unity_template.logic.sh.sha256  (its SHA-256, text/plain)
 *
 * Run:
 *   node --env-file=.env upload-unity-template-script.mjs --dry-run
 *   node --env-file=.env upload-unity-template-script.mjs
 *
 * Needs the same R2_* env vars as the other publish tooling (see scripts/.env).
 */
import { createHash } from "node:crypto";
import { readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { BUCKET, REPO_ROOT, makeClient, putObject, hasFlag } from "./registry-lib.mjs";

const TEMPLATE_DIR = join(REPO_ROOT, "templates", "unity-project");
const LOGIC_PATH = join(TEMPLATE_DIR, "build_unity_template.logic.sh");
const SHA_PATH = `${LOGIC_PATH}.sha256`;

const LOGIC_KEY = (process.env.UNITY_TEMPLATE_SCRIPT_R2_KEY || "unity-template/build_unity_template.logic.sh").replace(/^\/+/, "");
const SHA_KEY = `${LOGIC_KEY}.sha256`;
const PUBLIC_URL = process.env.UNITY_TEMPLATE_SCRIPT_PUBLIC_URL ||
  "https://pub-d76b7e028ac14f9bb044ebd65bccd3d9.r2.dev/unity-template/build_unity_template.logic.sh";

const DRY_RUN = hasFlag("--dry-run");

async function main() {
  const logic = readFileSync(LOGIC_PATH);
  const sha = createHash("sha256").update(logic).digest("hex");
  // Refresh the local sidecar so the repo always records the published hash.
  writeFileSync(SHA_PATH, `${sha}\n`, "utf8");

  console.log(`logic : ${LOGIC_PATH}`);
  console.log(`sha256: ${sha}`);
  console.log(`keys  : ${LOGIC_KEY}`);
  console.log(`        ${SHA_KEY}`);

  if (DRY_RUN) {
    console.log("\n~ dry-run: nothing uploaded.");
    return;
  }

  const client = await makeClient();
  await putObject(client, LOGIC_KEY, logic, "text/x-shellscript");
  await putObject(client, SHA_KEY, `${sha}\n`, "text/plain");
  console.log(`\n+ uploaded logic  -> r2://${BUCKET}/${LOGIC_KEY}`);
  console.log(`+ uploaded sha256 -> r2://${BUCKET}/${SHA_KEY}`);
  console.log(`\nLive: ${PUBLIC_URL}`);
  console.log("End users will pick up this version on their next run.");
}

main().catch((err) => {
  console.error(err.message || err);
  process.exit(1);
});
