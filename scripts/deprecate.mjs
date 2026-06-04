#!/usr/bin/env node
/**
 * Mark a published version as deprecated (npm-compatible `deprecated` field) so
 * consumers can still install it but see a warning. Nothing is deleted; latest
 * is untouched. The gentler alternative to unpublish.
 *
 * Needs R2 credentials.
 *
 * Run:  node deprecate.mjs <package> <version> [message] [--undo] [--dry-run] [--yes]
 *
 *   node deprecate.mjs com.ezg.foo 1.2.3 "use 1.2.4 — fixes serialization bug"
 *   node deprecate.mjs com.ezg.foo 1.2.3 --undo        clear the deprecation
 */
import {
  makeClient,
  getJson,
  putObject,
  positionalArgs,
  hasFlag,
  confirm,
} from "./registry-lib.mjs";

const DRY_RUN = hasFlag("--dry-run");
const UNDO = hasFlag("--undo");

async function main() {
  const [name, version, ...rest] = positionalArgs();
  if (!name || !version) {
    console.error("usage: node deprecate.mjs <package> <version> [message] [--undo] [--dry-run] [--yes]");
    process.exit(2);
  }
  const message = rest.join(" ").trim() || "This version is deprecated.";

  const client = await makeClient();
  const meta = await getJson(client, name);
  if (!meta) {
    console.error(`! ${name} is not published.`);
    process.exit(1);
  }
  const entry = meta.versions?.[version];
  if (!entry) {
    console.error(`! ${name}@${version} is not a published version.`);
    console.error(`  known versions: ${Object.keys(meta.versions || {}).join(", ") || "(none)"}`);
    process.exit(1);
  }

  if (UNDO) {
    if (!entry.deprecated) { console.log(`= ${name}@${version} is not deprecated. Nothing to do.`); return; }
    console.log(`Un-deprecate ${name}@${version} (was: "${entry.deprecated}")`);
    if (DRY_RUN) { console.log(`~ dry-run: would clear deprecated.`); return; }
    if (!(await confirm(`Clear deprecation on ${name}@${version}?`))) { console.log("aborted."); return; }
    delete entry.deprecated;
  } else {
    console.log(`Deprecate ${name}@${version}: "${message}"`);
    if (DRY_RUN) { console.log(`~ dry-run: would set deprecated.`); return; }
    if (!(await confirm(`Deprecate ${name}@${version}?`))) { console.log("aborted."); return; }
    entry.deprecated = message;
  }

  if (meta.time) meta.time.modified = new Date().toISOString();
  await putObject(client, name, JSON.stringify(meta, null, 2), "application/json");
  console.log(`+ ${UNDO ? "un-deprecated" : "deprecated"} ${name}@${version}.`);
}

main().catch((err) => { console.error(err); process.exit(1); });
