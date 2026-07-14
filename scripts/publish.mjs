#!/usr/bin/env node
/**
 * Publish UPM packages from packages/* to the Easygoing scoped registry (Cloudflare R2).
 *
 * For each packages/<dir>/package.json:
 *   1. If the version's tarball already exists in R2 -> skip (immutable, idempotent).
 *   2. `upm pack` -> signed .tgz (Unity 6.3+ signature, embeds .attestation.p7m),
 *      then compute integrity (sha512) + shasum (sha1) from the signed tarball.
 *      `--no-sign` falls back to plain `npm pack` (unsigned).
 *   3. Merge the new version into the package's packument metadata (full history kept).
 *   4. Upload tarball + metadata to R2 with the exact keys the Worker expects:
 *        metadata -> key `<name>`                 (content-type application/json)
 *        tarball  -> key `<name>/-/<file>.tgz`     (content-type application/octet-stream)
 *
 * Then, once for the whole run:
 *   5. Chain sync-unity-template-deps.mjs, which mirrors every packages/* version into
 *      unity-template.json's `dependencies` and re-publishes it to unity-template/latest.json.
 *      Feature Hub reads only that file, so without this a published package stays invisible
 *      in its "UPM Packages" tab. Anything that failed above is excluded by name so the
 *      template only ever names live versions. The re-publish happens even when the template
 *      already matches (see the comment on that call); `--no-template-sync` opts out.
 *
 * Run:  node --env-file=.env publish.mjs            real publish + sign + template sync;
 *                                                    needs R2_* creds and UPM_SERVICE_ACCOUNT_KEY_ID /
 *                                                    UPM_SERVICE_ACCOUNT_KEY_SECRET / ORGANIZATION_ID
 *       node --env-file=.env publish.mjs --no-sign   real publish, unsigned (npm pack)
 *       node publish.mjs --dry-run                   pack + build metadata + preview template
 *                                                    diff only, no R2/signing
 *       node --env-file=.env publish.mjs --no-template-sync   registry only, leave template alone
 */
import { execSync, execFileSync } from "node:child_process";
import { readFileSync, mkdtempSync, existsSync, readdirSync } from "node:fs";
import { createHash } from "node:crypto";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { tmpdir } from "node:os";
import semver from "semver";
import {
  REGISTRY_URL,
  makeClient,
  listPackageDirs,
  objectExists,
  getJson,
  putObject,
  tarballKey,
  recomputeLatest,
  hasFlag,
} from "./registry-lib.mjs";

const DRY_RUN = hasFlag("--dry-run");
// Sign with the Unity Package Manager CLI by default (Unity 6.3+ flags unsigned
// packages). `--no-sign` falls back to plain `npm pack`; dry-runs never sign so
// they stay dependency-free (no upm CLI / service-account creds needed).
const SIGN = !hasFlag("--no-sign");
const USE_SIGN = SIGN && !DRY_RUN;
// Publishing to the registry and declaring the version in the base template are two
// different destinations, and Feature Hub only ever reads the template — so the sync runs
// automatically here rather than relying on anyone remembering it. `--no-template-sync`
// opts out for the rare case of a registry-only publish.
const SYNC_TEMPLATE = !hasFlag("--no-template-sync");

// SRI integrity (sha512 base64) + npm shasum (sha1 hex), computed from tarball bytes.
function hashesFor(buf) {
  return {
    integrity: "sha512-" + createHash("sha512").update(buf).digest("base64"),
    shasum: createHash("sha1").update(buf).digest("hex"),
  };
}

function npmPack(pkgDir, destDir) {
  // Run through the shell so `npm` resolves to npm.cmd on Windows (execFile of a
  // .cmd throws EINVAL on Node >=18.20 / 20 / 24). Quote the destination for spaces.
  const out = execSync(`npm pack --json --pack-destination "${destDir}"`, {
    cwd: pkgDir, encoding: "utf8",
  });
  const info = JSON.parse(out.slice(out.indexOf("[")))[0];
  return {
    filename: info.filename,
    path: join(destDir, info.filename),
    integrity: info.integrity,
    shasum: info.shasum,
    unpackedSize: info.unpackedSize,
    fileCount: info.entryCount ?? info.fileCount,
  };
}

// Verify the upm CLI and signing credentials are present before the publish loop.
function ensureSigningReady() {
  try {
    execSync("upm --version", { stdio: "ignore" });
  } catch {
    throw new Error(
      "upm CLI not found on PATH. Install it once with:\n" +
      "  curl -fsSL https://cdn.packages.unity.com/upm-cli/install.sh | bash   (macOS/Linux)\n" +
      "  irm https://cdn.packages.unity.com/upm-cli/install.ps1 | iex          (Windows)\n" +
      "then restart your terminal. Or run with --no-sign to skip signing."
    );
  }
  for (const v of ["UPM_SERVICE_ACCOUNT_KEY_ID", "UPM_SERVICE_ACCOUNT_KEY_SECRET", "ORGANIZATION_ID"]) {
    if (!process.env[v]) throw new Error(`Missing env var for signing: ${v} (set it in scripts/.env)`);
  }
}

// Pack + sign with the Unity Package Manager CLI -> signed .tgz (embeds the
// .attestation.p7m signature). Credentials are read by upm from the inherited
// UPM_SERVICE_ACCOUNT_KEY_ID / UPM_SERVICE_ACCOUNT_KEY_SECRET env vars.
function upmPack(pkgDir, destDir, pkgJson) {
  const orgId = process.env.ORGANIZATION_ID;
  execSync(`upm pack "${pkgDir}" --organization-id "${orgId}" --destination "${destDir}"`, {
    stdio: "inherit", // surface upm's own progress / signing errors
  });
  const filename = `${pkgJson.name}-${pkgJson.version}.tgz`;
  let path = join(destDir, filename);
  if (!existsSync(path)) {
    // Fall back to whatever signed .tgz upm produced, in case its naming differs.
    const match = readdirSync(destDir).find(
      (f) => f.endsWith(".tgz") && f.includes(pkgJson.version) && f.startsWith(pkgJson.name)
    );
    if (!match) throw new Error(`upm pack produced no tarball for ${filename} in ${destDir}`);
    path = join(destDir, match);
  }
  return { filename: path.split(/[\\/]/).pop(), path, ...hashesFor(readFileSync(path)) };
}

function packPackage(pkgDir, destDir, pkgJson) {
  return USE_SIGN ? upmPack(pkgDir, destDir, pkgJson) : npmPack(pkgDir, destDir);
}

function buildVersionEntry(pkgJson, tarballUrl, packed) {
  return {
    ...pkgJson,
    _id: `${pkgJson.name}@${pkgJson.version}`,
    dist: {
      tarball: tarballUrl,
      shasum: packed.shasum,
      integrity: packed.integrity,
      ...(packed.unpackedSize != null ? { unpackedSize: packed.unpackedSize } : {}),
      ...(packed.fileCount != null ? { fileCount: packed.fileCount } : {}),
    },
  };
}

function mergeMetadata(existing, name, version, versionEntry, iso) {
  const meta = existing || { name, "dist-tags": {}, versions: {}, time: {} };
  meta.name = name;
  meta["dist-tags"] = meta["dist-tags"] || {};
  meta.versions = meta.versions || {};
  meta.time = meta.time || {};
  meta.versions[version] = versionEntry;
  meta.time[version] = iso;
  if (!meta.time.created) meta.time.created = iso;
  meta.time.modified = iso;
  const latest = recomputeLatest(meta) || version;
  const latestEntry = meta.versions[latest];
  meta.description = latestEntry.description || meta.description || "";
  meta.author = latestEntry.author || meta.author || "EZG Studio";
  return meta;
}

async function main() {
  const dirs = listPackageDirs();
  if (dirs.length === 0) {
    console.log("No packages found under packages/. Nothing to publish.");
    return;
  }

  if (USE_SIGN) ensureSigningReady();
  const client = DRY_RUN ? null : await makeClient();
  const tmp = mkdtempSync(join(tmpdir(), "ezg-upm-"));
  const summary = [];
  const failedNames = [];
  let failures = 0;

  for (const dir of dirs) {
    const pkgJson = JSON.parse(readFileSync(join(dir, "package.json"), "utf8"));
    const { name, version } = pkgJson;
    const label = `${name}@${version}`;
    try {
      if (!name || !version) throw new Error("package.json missing name or version");
      if (!semver.valid(version)) throw new Error(`invalid semver version: ${version}`);

      const key = tarballKey(name, version);

      if (!DRY_RUN && (await objectExists(client, key))) {
        console.log(`= skip ${label} (already published, immutable)`);
        summary.push(`skip    ${label}`);
        continue;
      }

      const packed = packPackage(dir, tmp, pkgJson);
      const tarballUrl = `${REGISTRY_URL}/${name}/-/${packed.filename}`;
      const versionEntry = buildVersionEntry(pkgJson, tarballUrl, packed);
      const existing = DRY_RUN ? null : await getJson(client, name);
      const meta = mergeMetadata(existing, name, version, versionEntry, new Date().toISOString());

      if (DRY_RUN) {
        console.log(`~ dry-run ${label} (unsigned preview; real publish signs via upm)`);
        console.log(`    tarball key : ${key}`);
        console.log(`    tarball url : ${tarballUrl}`);
        console.log(`    integrity   : ${packed.integrity}`);
        console.log(`    shasum      : ${packed.shasum}`);
        console.log(`    dist-tags   : latest=${meta["dist-tags"].latest}`);
        summary.push(`dry-run ${label}`);
        continue;
      }

      await putObject(client, key, readFileSync(packed.path), "application/octet-stream");
      await putObject(client, name, JSON.stringify(meta, null, 2), "application/json");
      console.log(`+ published ${label}${USE_SIGN ? " (signed)" : ""}`);
      summary.push(`publish ${label}`);
    } catch (err) {
      failures++;
      // Nameless package.json can't match a template entry, so there's nothing to exclude.
      if (name) failedNames.push(name);
      console.error(`! failed ${label}: ${err.message}`);
      summary.push(`FAIL    ${label}: ${err.message}`);
    }
  }

  console.log("\n--- summary ---");
  for (const line of summary) console.log("  " + line);

  // Feature Hub's "UPM Packages" tab reads unity-template.json (published as
  // unity-template/latest.json) and never queries the registry — npm's protocol has no
  // "list all packages" endpoint, and Feature Hub runs on a user's machine with no
  // registry credentials anyway. A package published above therefore stays invisible in
  // Feature Hub until its version is mirrored into the template, so mirror it here instead
  // of leaving it to a step someone has to remember. The sync is a no-op when the template
  // already matches, so running it on every publish costs nothing.
  //
  // A package that failed to publish is excluded by name rather than skipping the entire
  // sync: the template must never name a version that isn't live on the registry, but one
  // broken package must not keep its healthy siblings out of Feature Hub — and it would
  // keep them out for good, since a retry skips them as already-published while the broken
  // one fails again, so `failures === 0` would never come back. Dry-runs forward --dry-run
  // so they preview the template diff without writing or uploading.
  if (SYNC_TEMPLATE) {
    console.log("\n--- sync unity-template.json ---");
    const args = [fileURLToPath(new URL("./sync-unity-template-deps.mjs", import.meta.url))];
    if (DRY_RUN) args.push("--dry-run");
    if (failedNames.length > 0) args.push(`--exclude=${failedNames.join(",")}`);
    // Inherits R2_* creds from this process — the template manifest lives in the same
    // bucket as the registry, so a working publish already has everything the sync needs.
    execFileSync(process.execPath, args, { stdio: "inherit", env: process.env });
  }

  if (failures > 0) process.exit(1);
}

main().catch((err) => { console.error(err); process.exit(1); });
