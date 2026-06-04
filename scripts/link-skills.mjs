#!/usr/bin/env node
/**
 * Link `.claude/skills` -> `.agents/skills` so Claude Code picks up the repo's
 * skills. The skills themselves live (and are committed) under `.agents/skills/`;
 * `.claude/skills` is a per-machine junction (Windows) / symlink (macOS, Linux)
 * that git can't represent, so it is gitignored and recreated by this script.
 *
 * Run:  node scripts/link-skills.mjs
 */
import { mkdirSync, existsSync, lstatSync, rmSync, symlinkSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { platform } from "node:os";
import { execFileSync } from "node:child_process";

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = join(HERE, "..");
const target = join(REPO_ROOT, ".agents", "skills");
const claudeDir = join(REPO_ROOT, ".claude");
const link = join(claudeDir, "skills");

if (!existsSync(target)) {
  console.error(`! source not found: ${target} — nothing to link.`);
  process.exit(1);
}

mkdirSync(claudeDir, { recursive: true });

// Remove any stale link/dir at the destination so we always rebuild it cleanly.
if (existsSync(link) || isLink(link)) {
  rmSync(link, { recursive: true, force: true });
}

if (platform() === "win32") {
  // Junctions need no admin rights (unlike symlinks on Windows); use mklink /J.
  execFileSync("cmd", ["/c", "mklink", "/J", link, target], { stdio: "inherit" });
} else {
  symlinkSync(target, link, "dir");
}

console.log(`+ linked ${link} -> ${target}`);

function isLink(p) {
  try { return lstatSync(p).isSymbolicLink(); } catch { return false; }
}
