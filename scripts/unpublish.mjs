#!/usr/bin/env node
/**
 * Remove a version (or a whole package) from the Easygoing UPM registry.
 *
 * DEFAULT IS SAFE: only the packument metadata is rewritten — the .tgz tarball
 * is KEPT on R2, so the operation can be undone by re-publishing the metadata.
 * Pass --purge-tarball to also delete the .tgz (NOT undoable).
 *
 * NOTE: published versions are normally immutable; this is an admin override.
 * Always run with --dry-run first. Needs R2 credentials.
 *
 * Run:  node unpublish.mjs <package> [version] [--purge-tarball] [--dry-run] [--yes]
 *
 *   node unpublish.mjs com.ezg.foo 1.2.3        remove version 1.2.3 (keep tarball)
 *   node unpublish.mjs com.ezg.foo              remove the WHOLE package metadata
 *   node unpublish.mjs com.ezg.foo 1.2.3 --purge-tarball   also delete the .tgz
 */
import semver from "semver";
import {
  makeClient,
  getJson,
  putObject,
  deleteObject,
  objectExists,
  tarballKey,
  recomputeLatest,
  positionalArgs,
  hasFlag,
  confirm,
} from "./registry-lib.mjs";

const DRY_RUN = hasFlag("--dry-run");
const PURGE = hasFlag("--purge-tarball");

async function main() {
  const [name, version] = positionalArgs();
  if (!name) {
    console.error("usage: node unpublish.mjs <package> [version] [--purge-tarball] [--dry-run] [--yes]");
    process.exit(2);
  }

  const client = await makeClient();
  const meta = await getJson(client, name);
  if (!meta) {
    console.error(`! ${name} is not published — nothing to unpublish.`);
    process.exit(1);
  }

  // ---- whole-package removal -------------------------------------------------
  if (!version) {
    const versions = Object.keys(meta.versions || {}).sort(semver.rcompare);
    console.log(`This will remove the ENTIRE package metadata for ${name} (${versions.length} version(s)).`);
    if (PURGE) console.log(`!! --purge-tarball will also DELETE all ${versions.length} tarball(s). This is NOT undoable.`);
    console.log(`   tarball(s) ${PURGE ? "WILL be deleted" : "will be kept (default — undoable)"}.`);

    if (DRY_RUN) { console.log(`~ dry-run: would delete metadata key '${name}'${PURGE ? " + all tarballs" : ""}.`); return; }
    if (!(await confirm(`Delete the entire ${name} package?`))) { console.log("aborted."); return; }

    await deleteObject(client, name);
    if (PURGE) {
      for (const v of versions) {
        const key = tarballKey(name, v);
        if (await objectExists(client, key)) { await deleteObject(client, key); console.log(`  - deleted ${key}`); }
      }
    }
    console.log(`+ removed package ${name}${PURGE ? " (metadata + tarballs)" : " (metadata only; tarballs kept)"}.`);
    return;
  }

  // ---- single-version removal ------------------------------------------------
  if (!meta.versions?.[version]) {
    console.error(`! ${name}@${version} not found in registry metadata.`);
    console.error(`  known versions: ${Object.keys(meta.versions || {}).join(", ") || "(none)"}`);
    process.exit(1);
  }

  const remaining = Object.keys(meta.versions).filter((v) => v !== version);
  const oldLatest = meta["dist-tags"]?.latest;

  // Compute the resulting latest WITHOUT mutating meta yet (for the preview).
  let newLatest = oldLatest;
  if (oldLatest === version) {
    const valid = remaining.filter((v) => semver.valid(v)).sort(semver.rcompare);
    newLatest = valid[0] ?? null;
  }

  console.log(`Unpublish ${name}@${version}`);
  console.log(`  remaining versions: ${remaining.length}`);
  if (oldLatest === version) console.log(`  dist-tags.latest: ${oldLatest} -> ${newLatest ?? "(none — package becomes empty)"}`);
  console.log(`  tarball ${PURGE ? "WILL be deleted (NOT undoable)" : "kept (default — undoable)"}.`);

  if (DRY_RUN) {
    console.log(remaining.length === 0
      ? `~ dry-run: would delete metadata key '${name}' (last version removed)${PURGE ? " + tarball" : ""}.`
      : `~ dry-run: would rewrite metadata without ${version}${PURGE ? " + delete tarball" : ""}.`);
    return;
  }

  if (!(await confirm(`Unpublish ${name}@${version}?`))) { console.log("aborted."); return; }

  delete meta.versions[version];
  if (meta.time) delete meta.time[version];

  if (remaining.length === 0) {
    await deleteObject(client, name);
    console.log(`+ removed last version; deleted metadata key '${name}'.`);
  } else {
    recomputeLatest(meta);
    if (meta.time) meta.time.modified = new Date().toISOString();
    await putObject(client, name, JSON.stringify(meta, null, 2), "application/json");
    console.log(`+ unpublished ${name}@${version}; latest is now ${meta["dist-tags"].latest}.`);
  }

  if (PURGE) {
    const key = tarballKey(name, version);
    if (await objectExists(client, key)) { await deleteObject(client, key); console.log(`  - deleted ${key}`); }
  }
}

main().catch((err) => { console.error(err); process.exit(1); });
