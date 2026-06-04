#!/usr/bin/env node
/**
 * Validate every packages/* against UPM + registry conventions BEFORE publish.
 * Runs as a CI gate: exits non-zero on any error so a bad package never reaches R2.
 *
 * Checks per package.json:
 *   - name matches ^com\.ezg\.[a-z0-9-]+$
 *   - version is valid semver
 *   - `unity` field present
 *   - at least one .asmdef somewhere in the package
 *   - (warn) missing displayName / description
 *   - (warn) version not greater than the current R2 `latest` -> "forgot to bump?"
 *     (only checked when R2 credentials are available; skipped otherwise)
 *
 * Run:  node validate.mjs           local lint (R2 bump-check skipped without creds)
 */
import { readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";
import semver from "semver";
import { makeClient, listPackageDirs, getJson } from "./registry-lib.mjs";

const NAME_RE = /^com\.ezg\.[a-z0-9-]+$/;

function hasAsmdef(dir) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.name === "node_modules" || entry.name.startsWith(".")) continue;
    const full = join(dir, entry.name);
    if (entry.isDirectory()) {
      if (hasAsmdef(full)) return true;
    } else if (entry.name.endsWith(".asmdef")) {
      return true;
    }
  }
  return false;
}

async function main() {
  const dirs = listPackageDirs();
  if (dirs.length === 0) {
    console.log("No packages found under packages/. Nothing to validate.");
    return;
  }

  // R2 bump-check is best-effort: only run it when creds are present.
  let client = null;
  if (process.env.R2_ACCOUNT_ID && process.env.R2_ACCESS_KEY_ID && process.env.R2_SECRET_ACCESS_KEY) {
    try { client = await makeClient(); } catch { client = null; }
  }

  let errors = 0;
  let warnings = 0;
  const summary = [];

  for (const dir of dirs) {
    let pkgJson;
    try {
      pkgJson = JSON.parse(readFileSync(join(dir, "package.json"), "utf8"));
    } catch (err) {
      errors++;
      console.error(`! ${dir}: unreadable package.json: ${err.message}`);
      summary.push(`ERROR   ${dir}: bad package.json`);
      continue;
    }

    const { name, version } = pkgJson;
    const label = `${name || "(no name)"}@${version || "(no version)"}`;
    const problems = [];
    const warns = [];

    if (!name || !NAME_RE.test(name)) problems.push(`name must match ${NAME_RE} (got ${JSON.stringify(name)})`);
    if (!version || !semver.valid(version)) problems.push(`invalid semver version: ${JSON.stringify(version)}`);
    if (!pkgJson.unity) problems.push("missing `unity` field");
    if (!hasAsmdef(dir)) problems.push("no .asmdef found in package");
    if (!pkgJson.displayName) warns.push("missing displayName");
    if (!pkgJson.description) warns.push("missing description");

    // Bump check: a brand-new tarball whose version isn't above current latest.
    if (client && name && semver.valid(version)) {
      try {
        const meta = await getJson(client, name);
        const latest = meta?.["dist-tags"]?.latest;
        if (latest) {
          const known = !!meta.versions?.[version];
          if (!known && !semver.gt(version, latest)) {
            warns.push(`version ${version} is not greater than published latest ${latest} — forgot to bump?`);
          }
        }
      } catch (err) {
        warns.push(`could not check R2 latest: ${err.message}`);
      }
    }

    for (const p of problems) console.error(`! ${label}: ${p}`);
    for (const w of warns) console.warn(`~ ${label}: ${w}`);

    if (problems.length) {
      errors += problems.length;
      summary.push(`ERROR   ${label} (${problems.length})`);
    } else {
      console.log(`+ ok ${label}${warns.length ? ` (${warns.length} warning${warns.length > 1 ? "s" : ""})` : ""}`);
      summary.push(`ok      ${label}${warns.length ? ` (${warns.length} warn)` : ""}`);
    }
    warnings += warns.length;
  }

  console.log("\n--- summary ---");
  for (const line of summary) console.log("  " + line);
  console.log(`\n${errors} error(s), ${warnings} warning(s)`);
  if (errors > 0) process.exit(1);
}

main().catch((err) => { console.error(err); process.exit(1); });
