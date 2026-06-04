#!/usr/bin/env node
/**
 * Publish UPM packages from packages/* to the Easygoing scoped registry (Cloudflare R2).
 *
 * For each packages/<dir>/package.json:
 *   1. If the version's tarball already exists in R2 -> skip (immutable, idempotent).
 *   2. `npm pack --json` -> .tgz + integrity (sha512) + shasum (sha1).
 *   3. Merge the new version into the package's packument metadata (full history kept).
 *   4. Upload tarball + metadata to R2 with the exact keys the Worker expects:
 *        metadata -> key `<name>`                 (content-type application/json)
 *        tarball  -> key `<name>/-/<file>.tgz`     (content-type application/octet-stream)
 *
 * Run:  node publish.mjs            real publish; needs R2_ACCOUNT_ID / R2_ACCESS_KEY_ID / R2_SECRET_ACCESS_KEY
 *       node publish.mjs --dry-run  pack + build metadata only, no R2 calls (no AWS creds needed)
 */
import semver from "semver";
import { execSync } from "node:child_process";
import { readFileSync, readdirSync, existsSync, mkdtempSync } from "node:fs";
import { join, dirname } from "node:path";
import { tmpdir } from "node:os";
import { fileURLToPath } from "node:url";

const DRY_RUN = process.argv.includes("--dry-run");
const HERE = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = join(HERE, "..");
const PACKAGES_DIR = join(REPO_ROOT, "packages");

const REGISTRY_URL = (process.env.REGISTRY_URL ||
  "https://upm-registry-worker.developer-a1f.workers.dev").replace(/\/+$/, "");
const BUCKET = process.env.R2_BUCKET || "company-upm-registry";

function requireEnv(name) {
  const v = process.env[name];
  if (!v) throw new Error(`Missing required env var: ${name}`);
  return v;
}

// AWS SDK is imported lazily so --dry-run runs with zero cloud deps installed.
async function makeClient() {
  const { S3Client } = await import("@aws-sdk/client-s3");
  const accountId = requireEnv("R2_ACCOUNT_ID");
  return new S3Client({
    region: "auto",
    endpoint: `https://${accountId}.r2.cloudflarestorage.com`,
    credentials: {
      accessKeyId: requireEnv("R2_ACCESS_KEY_ID"),
      secretAccessKey: requireEnv("R2_SECRET_ACCESS_KEY"),
    },
  });
}

function listPackageDirs() {
  if (!existsSync(PACKAGES_DIR)) return [];
  return readdirSync(PACKAGES_DIR, { withFileTypes: true })
    .filter((d) => d.isDirectory())
    .map((d) => join(PACKAGES_DIR, d.name))
    .filter((dir) => existsSync(join(dir, "package.json")));
}

async function objectExists(client, key) {
  const { HeadObjectCommand } = await import("@aws-sdk/client-s3");
  try {
    await client.send(new HeadObjectCommand({ Bucket: BUCKET, Key: key }));
    return true;
  } catch (err) {
    if (err?.$metadata?.httpStatusCode === 404 || err?.name === "NotFound") return false;
    throw err;
  }
}

async function getJson(client, key) {
  const { GetObjectCommand } = await import("@aws-sdk/client-s3");
  try {
    const res = await client.send(new GetObjectCommand({ Bucket: BUCKET, Key: key }));
    return JSON.parse(await res.Body.transformToString());
  } catch (err) {
    if (err?.$metadata?.httpStatusCode === 404 || err?.name === "NoSuchKey") return null;
    throw err;
  }
}

async function putObject(client, key, body, contentType) {
  const { PutObjectCommand } = await import("@aws-sdk/client-s3");
  await client.send(new PutObjectCommand({ Bucket: BUCKET, Key: key, Body: body, ContentType: contentType }));
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
    integrity: info.integrity,
    shasum: info.shasum,
    unpackedSize: info.unpackedSize,
    fileCount: info.entryCount ?? info.fileCount,
  };
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
  const latest = Object.keys(meta.versions).filter((v) => semver.valid(v)).sort(semver.rcompare)[0] || version;
  meta["dist-tags"].latest = latest;
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

  const client = DRY_RUN ? null : await makeClient();
  const tmp = mkdtempSync(join(tmpdir(), "ezg-upm-"));
  const summary = [];
  let failures = 0;

  for (const dir of dirs) {
    const pkgJson = JSON.parse(readFileSync(join(dir, "package.json"), "utf8"));
    const { name, version } = pkgJson;
    const label = `${name}@${version}`;
    try {
      if (!name || !version) throw new Error("package.json missing name or version");
      if (!semver.valid(version)) throw new Error(`invalid semver version: ${version}`);

      const tarballKey = `${name}/-/${name}-${version}.tgz`;

      if (!DRY_RUN && (await objectExists(client, tarballKey))) {
        console.log(`= skip ${label} (already published, immutable)`);
        summary.push(`skip    ${label}`);
        continue;
      }

      const packed = npmPack(dir, tmp);
      const tarballUrl = `${REGISTRY_URL}/${name}/-/${packed.filename}`;
      const versionEntry = buildVersionEntry(pkgJson, tarballUrl, packed);
      const existing = DRY_RUN ? null : await getJson(client, name);
      const meta = mergeMetadata(existing, name, version, versionEntry, new Date().toISOString());

      if (DRY_RUN) {
        console.log(`~ dry-run ${label}`);
        console.log(`    tarball key : ${tarballKey}`);
        console.log(`    tarball url : ${tarballUrl}`);
        console.log(`    integrity   : ${packed.integrity}`);
        console.log(`    shasum      : ${packed.shasum}`);
        console.log(`    dist-tags   : latest=${meta["dist-tags"].latest}`);
        summary.push(`dry-run ${label}`);
        continue;
      }

      await putObject(client, tarballKey, readFileSync(join(tmp, packed.filename)), "application/octet-stream");
      await putObject(client, name, JSON.stringify(meta, null, 2), "application/json");
      console.log(`+ published ${label}`);
      summary.push(`publish ${label}`);
    } catch (err) {
      failures++;
      console.error(`! failed ${label}: ${err.message}`);
      summary.push(`FAIL    ${label}: ${err.message}`);
    }
  }

  console.log("\n--- summary ---");
  for (const line of summary) console.log("  " + line);
  if (failures > 0) process.exit(1);
}

main().catch((err) => { console.error(err); process.exit(1); });
