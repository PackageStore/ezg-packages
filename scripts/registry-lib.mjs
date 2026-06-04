/**
 * Shared helpers for the Easygoing UPM registry tooling (Cloudflare R2).
 *
 * Used by publish.mjs (add versions) and the admin scripts
 * (list / validate / unpublish / rollback / deprecate). Centralises the R2
 * client, object read/write/delete, package discovery, and the packument
 * "latest" computation so every tool agrees on the exact keys the Worker expects:
 *   metadata -> key `<name>`              (application/json)
 *   tarball  -> key `<name>/-/<file>.tgz` (application/octet-stream)
 */
import semver from "semver";
import readline from "node:readline";
import { readdirSync, existsSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { dirname } from "node:path";

const HERE = dirname(fileURLToPath(import.meta.url));
export const REPO_ROOT = join(HERE, "..");
export const PACKAGES_DIR = join(REPO_ROOT, "packages");

export const REGISTRY_URL = (process.env.REGISTRY_URL ||
  "https://upm-registry-worker.developer-a1f.workers.dev").replace(/\/+$/, "");
export const BUCKET = process.env.R2_BUCKET || "company-upm-registry";

export function requireEnv(name) {
  const v = process.env[name];
  if (!v) throw new Error(`Missing required env var: ${name}`);
  return v;
}

// AWS SDK is imported lazily so dry-runs work with zero cloud deps installed.
export async function makeClient() {
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

export function listPackageDirs() {
  if (!existsSync(PACKAGES_DIR)) return [];
  return readdirSync(PACKAGES_DIR, { withFileTypes: true })
    .filter((d) => d.isDirectory())
    .map((d) => join(PACKAGES_DIR, d.name))
    .filter((dir) => existsSync(join(dir, "package.json")));
}

export async function objectExists(client, key) {
  const { HeadObjectCommand } = await import("@aws-sdk/client-s3");
  try {
    await client.send(new HeadObjectCommand({ Bucket: BUCKET, Key: key }));
    return true;
  } catch (err) {
    if (err?.$metadata?.httpStatusCode === 404 || err?.name === "NotFound") return false;
    throw err;
  }
}

export async function getJson(client, key) {
  const { GetObjectCommand } = await import("@aws-sdk/client-s3");
  try {
    const res = await client.send(new GetObjectCommand({ Bucket: BUCKET, Key: key }));
    return JSON.parse(await res.Body.transformToString());
  } catch (err) {
    if (err?.$metadata?.httpStatusCode === 404 || err?.name === "NoSuchKey") return null;
    throw err;
  }
}

export async function putObject(client, key, body, contentType) {
  const { PutObjectCommand } = await import("@aws-sdk/client-s3");
  await client.send(new PutObjectCommand({ Bucket: BUCKET, Key: key, Body: body, ContentType: contentType }));
}

export async function deleteObject(client, key) {
  const { DeleteObjectCommand } = await import("@aws-sdk/client-s3");
  await client.send(new DeleteObjectCommand({ Bucket: BUCKET, Key: key }));
}

/** Tarball R2 key for a given package name + version. */
export function tarballKey(name, version) {
  return `${name}/-/${name}-${version}.tgz`;
}

/**
 * Recompute and set `dist-tags.latest` to the highest valid semver currently in
 * `meta.versions`. Returns the chosen version, or null if no versions remain.
 */
export function recomputeLatest(meta) {
  const valid = Object.keys(meta.versions || {}).filter((v) => semver.valid(v));
  if (valid.length === 0) {
    if (meta["dist-tags"]) delete meta["dist-tags"].latest;
    return null;
  }
  const latest = valid.sort(semver.rcompare)[0];
  meta["dist-tags"] = meta["dist-tags"] || {};
  meta["dist-tags"].latest = latest;
  return latest;
}

/** True when the arg list contains the given flag. */
export function hasFlag(flag) {
  return process.argv.slice(2).includes(flag);
}

/** Positional (non-flag) args, in order. */
export function positionalArgs() {
  return process.argv.slice(2).filter((a) => !a.startsWith("--"));
}

/**
 * Ask the user to confirm a destructive action. Auto-confirms when `--yes` is
 * passed, when running in CI, or when stdin is not a TTY (e.g. piped). `prompt`
 * is the y/N question to show.
 */
export async function confirm(prompt) {
  if (hasFlag("--yes") || process.env.CI || !process.stdin.isTTY) return true;
  const rl = readline.createInterface({ input: process.stdin, output: process.stdout });
  try {
    const answer = await new Promise((resolve) => rl.question(`${prompt} [y/N] `, resolve));
    return /^y(es)?$/i.test(answer.trim());
  } finally {
    rl.close();
  }
}
