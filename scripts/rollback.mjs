#!/usr/bin/env node
/**
 * Roll back `dist-tags.latest` to an older, already-published version WITHOUT
 * deleting anything. This is the safest reaction to a broken release: consumers
 * resolving "latest" fall back to the target version immediately, and the change
 * is fully reversible — run rollback again to point latest elsewhere.
 *
 * Needs R2 credentials.
 *
 * Run:  node rollback.mjs <package> <target-version> [--dry-run] [--yes]
 *
 *   node rollback.mjs com.ezg.foo 1.2.2     point latest at 1.2.2 (1.2.3 stays published)
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

async function main() {
  const [name, target] = positionalArgs();
  if (!name || !target) {
    console.error("usage: node rollback.mjs <package> <target-version> [--dry-run] [--yes]");
    process.exit(2);
  }

  const client = await makeClient();
  const meta = await getJson(client, name);
  if (!meta) {
    console.error(`! ${name} is not published.`);
    process.exit(1);
  }
  if (!meta.versions?.[target]) {
    console.error(`! ${name}@${target} is not a published version — cannot point latest at it.`);
    console.error(`  known versions: ${Object.keys(meta.versions || {}).join(", ") || "(none)"}`);
    process.exit(1);
  }

  const current = meta["dist-tags"]?.latest;
  if (current === target) {
    console.log(`= ${name} latest is already ${target}. Nothing to do.`);
    return;
  }

  console.log(`Rollback ${name}: dist-tags.latest ${current ?? "(none)"} -> ${target}`);
  console.log(`  (no versions or tarballs are deleted; reversible)`);

  if (DRY_RUN) { console.log(`~ dry-run: would set latest to ${target}.`); return; }
  if (!(await confirm(`Point ${name} latest at ${target}?`))) { console.log("aborted."); return; }

  meta["dist-tags"] = meta["dist-tags"] || {};
  meta["dist-tags"].latest = target;
  if (meta.time) meta.time.modified = new Date().toISOString();
  await putObject(client, name, JSON.stringify(meta, null, 2), "application/json");
  console.log(`+ ${name} latest is now ${target}.`);
}

main().catch((err) => { console.error(err); process.exit(1); });
