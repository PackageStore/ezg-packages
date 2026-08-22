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

// The two bootstrap files go up as well, so a developer whose copy is out of date can fetch a fresh
// one with a single curl instead of waiting for someone to hand them the file. They are the only
// artefacts the gateway serves without a token -- the bootstrap has no session yet when it runs.
const BOOTSTRAP_FILES = ["build_unity_template.sh", "build_unity_template.command"];

const LOGIC_KEY = (process.env.UNITY_TEMPLATE_SCRIPT_R2_KEY || "unity-template/build_unity_template.logic.sh").replace(/^\/+/, "");
const SHA_KEY = `${LOGIC_KEY}.sha256`;
const PUBLIC_URL = process.env.UNITY_TEMPLATE_SCRIPT_PUBLIC_URL ||
  "https://upm-registry-worker.developer-a1f.workers.dev/boot/build_unity_template.logic.sh";

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

  for (const fileName of BOOTSTRAP_FILES) {
    const key = `${LOGIC_KEY.replace(/[^/]+$/, "")}${fileName}`;
    await putObject(client, key, readFileSync(join(TEMPLATE_DIR, fileName)), "text/x-shellscript");
    console.log(`+ uploaded bootstrap -> r2://${BUCKET}/${key}`);
  }
  console.log(`\nLive: ${PUBLIC_URL}`);
  console.log("End users will pick up this version on their next run.");
  console.log("\nA developer with an outdated bootstrap can refresh it with:");
  for (const fileName of BOOTSTRAP_FILES) {
    console.log(`  curl -fLO ${PUBLIC_URL.replace(/[^/]+$/, "")}${fileName} && chmod +x ${fileName}`);
  }
}

main().catch((err) => {
  console.error(err.message || err);
  process.exit(1);
});
