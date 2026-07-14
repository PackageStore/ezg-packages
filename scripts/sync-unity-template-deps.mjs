#!/usr/bin/env node
/**
 * Sync packages/*'s published version into templates/unity-project/unity-template.json's
 * "dependencies", then re-publish that file to R2 so Feature Hub's "UPM Packages" tab picks
 * it up. Registry publish (publish.mjs) and template curation (this file) are separate steps —
 * this closes the gap so a package published to the registry doesn't stay invisible in Feature
 * Hub because nobody remembered to add/bump its entry in the template.
 *
 * Run:
 *   node sync-unity-template-deps.mjs --dry-run              preview only, no writes/uploads
 *   node --env-file=.env sync-unity-template-deps.mjs        write + upload to R2 (needs R2_* creds)
 *   node --env-file=.env sync-unity-template-deps.mjs --skip-upload   only write the local file
 */
import { readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { dirname } from "node:path";
import { execFileSync } from "node:child_process";
import { REPO_ROOT, listPackageDirs, hasFlag } from "./registry-lib.mjs";

const HERE = dirname(fileURLToPath(import.meta.url));
const TEMPLATE_PATH = join(REPO_ROOT, "templates", "unity-project", "unity-template.json");
const DRY_RUN = hasFlag("--dry-run");
const SKIP_UPLOAD = hasFlag("--skip-upload");

// New (not-yet-templated) packages are only auto-added when they're under this scope.
// Non-EZG packages under packages/ (e.g. the com.google.play.* / appbundle vendor copies)
// are NOT auto-added: those exact plugins already ship via files.unityPackages in this same
// template, so declaring them again as a UPM dependency would install the same SDK twice.
const AUTO_ADD_SCOPE = "com.ezg.";

// Known com.ezg.* packages that are intentionally not part of the base template (e.g.
// throwaway smoke-test fixtures). Extend this if another such package shows up.
const SKIP_NEW_PACKAGES = new Set(["com.ezg.sample"]);

// Insert [key, value] right before the first entry that sorts after `key`, leaving every
// other entry's position untouched. The file isn't fully alphabetical top to bottom (e.g.
// com.unity.modules.* is grouped at the very end out of sequence) — a blind full re-sort
// would "fix" that grouping as an unwanted side effect, so we only place the new entry.
function insertSorted(entries, key, value) {
  const idx = entries.findIndex(([k]) => k.localeCompare(key) > 0);
  entries.splice(idx === -1 ? entries.length : idx, 0, [key, value]);
}

function main() {
  const template = JSON.parse(readFileSync(TEMPLATE_PATH, "utf8"));
  template.dependencies = template.dependencies || {};
  const entries = Object.entries(template.dependencies);

  const changes = [];
  const skipped = [];
  for (const dir of listPackageDirs()) {
    const pkg = JSON.parse(readFileSync(join(dir, "package.json"), "utf8"));
    if (!pkg.name || !pkg.version) continue;
    const current = template.dependencies[pkg.name];
    if (current === pkg.version) continue;

    const isNew = current === undefined;
    if (isNew && (!pkg.name.startsWith(AUTO_ADD_SCOPE) || SKIP_NEW_PACKAGES.has(pkg.name))) {
      skipped.push(pkg.name);
      continue;
    }

    changes.push({ name: pkg.name, from: current, to: pkg.version });
    if (isNew) {
      insertSorted(entries, pkg.name, pkg.version);
    } else {
      entries[entries.findIndex(([k]) => k === pkg.name)][1] = pkg.version;
    }
  }

  if (skipped.length > 0) {
    console.log("Not auto-added (outside com.ezg.* scope or explicitly excluded) — add by hand if intended:");
    for (const name of skipped) console.log(`  - ${name}`);
    console.log("");
  }

  if (changes.length === 0) {
    console.log("Nothing to sync — unity-template.json already matches packages/*.");
    return;
  }

  template.dependencies = Object.fromEntries(entries);

  console.log("Changes:");
  for (const c of changes) {
    console.log(c.from ? `  ~ ${c.name}: ${c.from} -> ${c.to}` : `  + ${c.name}: ${c.to} (new)`);
  }

  if (DRY_RUN) {
    console.log("\n~ dry-run — unity-template.json not written, nothing uploaded.");
    return;
  }

  writeFileSync(TEMPLATE_PATH, `${JSON.stringify(template, null, 2)}\n`, "utf8");
  console.log(`\n+ wrote ${TEMPLATE_PATH}`);

  if (SKIP_UPLOAD) {
    console.log("~ --skip-upload set, not publishing to R2 (run upload-unity-template-assets.mjs manually).");
    return;
  }

  execFileSync(process.execPath, [join(HERE, "upload-unity-template-assets.mjs")], {
    stdio: "inherit",
    env: process.env,
  });
}

main();
