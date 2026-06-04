#!/usr/bin/env node
/**
 * Inspect the Easygoing UPM registry (read-only). Needs R2 credentials.
 *
 * Run:  node list.mjs                  table of every local packages/* + its R2 state
 *       node list.mjs --remote         list packages by scanning R2 (ignores local dirs)
 *       node list.mjs <package>        detailed version history for one package
 */
import { readFileSync } from "node:fs";
import { join, basename } from "node:path";
import semver from "semver";
import {
  BUCKET,
  makeClient,
  listPackageDirs,
  getJson,
  objectExists,
  tarballKey,
  positionalArgs,
  hasFlag,
} from "./registry-lib.mjs";

function pad(s, n) {
  s = String(s ?? "");
  return s.length >= n ? s : s + " ".repeat(n - s.length);
}

async function listRemotePackageNames(client) {
  const { ListObjectsV2Command } = await import("@aws-sdk/client-s3");
  const names = new Set();
  let token;
  do {
    const res = await client.send(new ListObjectsV2Command({
      Bucket: BUCKET, Delimiter: "/", ContinuationToken: token,
    }));
    // Packument metadata is stored at the top-level key `<name>`; tarballs live
    // under `<name>/-/...` (surfaced as CommonPrefixes). Union both, strip `/`.
    for (const o of res.Contents || []) if (o.Key && !o.Key.includes("/")) names.add(o.Key);
    for (const p of res.CommonPrefixes || []) if (p.Prefix) names.add(p.Prefix.replace(/\/$/, ""));
    token = res.IsTruncated ? res.NextContinuationToken : undefined;
  } while (token);
  return [...names].sort();
}

async function detail(client, name) {
  const meta = await getJson(client, name);
  if (!meta) {
    console.log(`${name}: not found on registry`);
    return;
  }
  const latest = meta["dist-tags"]?.latest;
  console.log(`${name}`);
  console.log(`  latest: ${latest ?? "(none)"}`);
  const versions = Object.keys(meta.versions || {}).sort(semver.rcompare);
  console.log(`  versions (${versions.length}):`);
  for (const v of versions) {
    const entry = meta.versions[v];
    const exists = await objectExists(client, tarballKey(name, v));
    const tags = [];
    if (v === latest) tags.push("latest");
    if (entry.deprecated) tags.push(`deprecated: ${entry.deprecated}`);
    if (!exists) tags.push("TARBALL MISSING");
    console.log(`    ${pad(v, 12)} ${meta.time?.[v] ?? ""}${tags.length ? "  [" + tags.join(", ") + "]" : ""}`);
  }
}

async function table(client, names) {
  console.log(`${pad("package", 36)}${pad("latest", 12)}${pad("versions", 10)}tarball(latest)`);
  console.log("-".repeat(72));
  for (const name of names) {
    const meta = await getJson(client, name);
    if (!meta) {
      console.log(`${pad(name, 36)}${pad("-", 12)}${pad("0", 10)}not published`);
      continue;
    }
    const latest = meta["dist-tags"]?.latest;
    const count = Object.keys(meta.versions || {}).length;
    const exists = latest ? await objectExists(client, tarballKey(name, latest)) : false;
    console.log(`${pad(name, 36)}${pad(latest ?? "-", 12)}${pad(count, 10)}${exists ? "ok" : "MISSING"}`);
  }
}

async function main() {
  const client = await makeClient();
  const args = positionalArgs();

  if (args.length === 1) {
    await detail(client, args[0]);
    return;
  }

  const names = hasFlag("--remote")
    ? await listRemotePackageNames(client)
    : listPackageDirs().map((dir) => JSON.parse(readFileSync(join(dir, "package.json"), "utf8")).name || basename(dir));

  if (names.length === 0) {
    console.log("No packages found.");
    return;
  }
  await table(client, names.sort());
}

main().catch((err) => { console.error(err); process.exit(1); });
