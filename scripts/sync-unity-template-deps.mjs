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
 *   node --env-file=.env sync-unity-template-deps.mjs --exclude=com.ezg.foo,com.ezg.bar
 *                                                           leave those packages' entries untouched
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

// Packages whose entry must be left exactly as-is. publish.mjs passes the ones that failed
// to publish in this run: the template must never name a version that isn't live on the
// registry, and a failed publish means exactly that. Excluding them by name (rather than
// skipping the whole sync) keeps the packages that published fine from being held hostage
// by a broken sibling — a retry would not save them either, since they get skipped as
// already-published while the broken one fails again.
const EXCLUDE = new Set(
  process.argv
    .slice(2)
    .filter((a) => a.startsWith("--exclude="))
    .flatMap((a) => a.slice("--exclude=".length).split(","))
    .map((s) => s.trim())
    .filter(Boolean)
);

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
  const excluded = [];
  for (const dir of listPackageDirs()) {
    const pkg = JSON.parse(readFileSync(join(dir, "package.json"), "utf8"));
    if (!pkg.name || !pkg.version) continue;
    if (EXCLUDE.has(pkg.name)) {
      excluded.push(pkg.name);
      continue;
    }
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

  if (excluded.length > 0) {
    console.log("Excluded — template keeps whatever version it already names for these:");
    for (const name of excluded) console.log(`  - ${name}`);
    console.log("");
  }

  if (skipped.length > 0) {
    console.log("Not auto-added (outside com.ezg.* scope or explicitly excluded) — add by hand if intended:");
    for (const name of skipped) console.log(`  - ${name}`);
    console.log("");
  }

  if (changes.length === 0) {
    console.log("unity-template.json already matches packages/* — no edit needed.");
  } else {
    template.dependencies = Object.fromEntries(entries);
    console.log("Changes:");
    for (const c of changes) {
      console.log(c.from ? `  ~ ${c.name}: ${c.from} -> ${c.to}` : `  + ${c.name}: ${c.to} (new)`);
    }
  }

  if (DRY_RUN) {
    console.log("\n~ dry-run — unity-template.json not written, nothing uploaded.");
    return;
  }

  if (changes.length > 0) {
    writeFileSync(TEMPLATE_PATH, `${JSON.stringify(template, null, 2)}\n`, "utf8");
    console.log(`\n+ wrote ${TEMPLATE_PATH}`);
  }

  if (SKIP_UPLOAD) {
    console.log("~ --skip-upload set, not publishing to R2 (run upload-unity-template-assets.mjs manually).");
    return;
  }

  // Uploads even when nothing changed locally. The local file matching packages/* is no
  // proof R2 got it: the write above happens before the upload, so any upload that died
  // (missing R2_* creds, network, a SHA-256 mismatch) leaves the file already correct on
  // disk. Gating the upload on a local diff made that state permanent — every later run
  // saw "already matches", exited 0, and never re-sent, so Feature Hub kept serving the
  // old versions while the tool reported success. Re-uploading an unchanged manifest is a
  // ~12KB idempotent PUT, so making this unconditional costs nothing and self-heals.
  execFileSync(process.execPath, [join(HERE, "upload-unity-template-assets.mjs")], {
    stdio: "inherit",
    env: process.env,
  });
}

main();
