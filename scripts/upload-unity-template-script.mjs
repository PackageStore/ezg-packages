#!/usr/bin/env node
/**
 * Publish the Unity template builder LOGIC to Cloudflare R2.
 *
 * The end users keep a tiny, near-immutable bootstrap (build_unity_template.sh). On every run that
 * bootstrap downloads the real logic (build_unity_template.logic.sh) from R2 and verifies it against
 * a published SHA-256 sidecar. So whenever you edit the logic, run this to push the new version --
 * every user picks it up automatically on their next run, without replacing any local file.
 *
 * Two copies go up, from one source file. They differ by exactly one line -- AUTH_FORCE -- and which
 * door serves them:
 *   unity-template/build_unity_template.logic.sh              AUTH_FORCE=0, reached over the public
 *                                                             r2.dev domain by the OLD bootstrap.
 *                                                             Sign-in follows the server's
 *                                                             /auth/config, so today it is optional.
 *   unity-template/build_unity_template.logic.auth.sh         AUTH_FORCE=1, what the Worker hands to
 *                                                             /boot/build_unity_template.logic.sh,
 *                                                             i.e. the NEW bootstrap. Always signs in.
 * Each gets its own .sha256 sidecar, because the bootstrap verifies whatever it downloaded against
 * "<the url it used>.sha256" -- the two hashes must not be mixed up.
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
// The /boot/ variant. Same bytes apart from the AUTH_FORCE line, so it is generated here rather than
// kept as a second file that would quietly drift out of sync with the first.
const AUTH_LOGIC_KEY = LOGIC_KEY.replace(/\.sh$/, ".auth.sh");
const AUTH_SHA_KEY = `${AUTH_LOGIC_KEY}.sha256`;
const AUTH_FORCE_OFF = "\nAUTH_FORCE=0\n";
const AUTH_FORCE_ON = "\nAUTH_FORCE=1\n";
const PUBLIC_URL = process.env.UNITY_TEMPLATE_SCRIPT_PUBLIC_URL ||
  "https://upm-registry-worker.developer-a1f.workers.dev/boot/build_unity_template.logic.sh";

const DRY_RUN = hasFlag("--dry-run");

/** The /boot/ copy: identical source with sign-in forced on. Throws rather than publishing a variant
 *  that silently behaves like the open one. */
function buildAuthVariant(logicText) {
  const hits = logicText.split(AUTH_FORCE_OFF).length - 1;
  if (hits !== 1) {
    throw new Error(
      `Expected exactly one "AUTH_FORCE=0" line in build_unity_template.logic.sh, found ${hits}. ` +
      "The auth variant cannot be generated -- fix the marker line before publishing.",
    );
  }
  return logicText.replace(AUTH_FORCE_OFF, AUTH_FORCE_ON);
}

async function main() {
  const logic = readFileSync(LOGIC_PATH);
  const sha = createHash("sha256").update(logic).digest("hex");
  // Refresh the local sidecar so the repo always records the published hash.
  writeFileSync(SHA_PATH, `${sha}\n`, "utf8");

  const authLogic = Buffer.from(buildAuthVariant(logic.toString("utf8")), "utf8");
  const authSha = createHash("sha256").update(authLogic).digest("hex");

  console.log(`logic : ${LOGIC_PATH}`);
  console.log(`sha256: ${sha}`);
  console.log(`keys  : ${LOGIC_KEY}`);
  console.log(`        ${SHA_KEY}`);
  console.log(`auth variant (AUTH_FORCE=1, served at /boot/):`);
  console.log(`sha256: ${authSha}`);
  console.log(`keys  : ${AUTH_LOGIC_KEY}`);
  console.log(`        ${AUTH_SHA_KEY}`);

  if (DRY_RUN) {
    console.log("\n~ dry-run: nothing uploaded.");
    return;
  }

  const client = await makeClient();
  await putObject(client, LOGIC_KEY, logic, "text/x-shellscript");
  await putObject(client, SHA_KEY, `${sha}\n`, "text/plain");
  console.log(`\n+ uploaded logic  -> r2://${BUCKET}/${LOGIC_KEY}`);
  console.log(`+ uploaded sha256 -> r2://${BUCKET}/${SHA_KEY}`);

  await putObject(client, AUTH_LOGIC_KEY, authLogic, "text/x-shellscript");
  await putObject(client, AUTH_SHA_KEY, `${authSha}\n`, "text/plain");
  console.log(`+ uploaded logic  -> r2://${BUCKET}/${AUTH_LOGIC_KEY}`);
  console.log(`+ uploaded sha256 -> r2://${BUCKET}/${AUTH_SHA_KEY}`);

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
