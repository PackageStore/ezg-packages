#!/usr/bin/env node
/**
 * Tạo ezg.base.default.unitypackage từ toàn bộ thư mục DefaultSetup/.
 *
 * .unitypackage format = gzip tarball với cấu trúc:
 *   {guid}/asset      ← nội dung file (chỉ với file, không có với folder)
 *   {guid}/pathname   ← đường dẫn Unity
 *
 * Toàn bộ các file trong DefaultSetup/ (bao gồm ProjectSettings/, .gitignore, v.v...)
 * được đặt ở project root path.
 *
 * Usage:
 *   node --env-file=.env create-unity-default-package.mjs --dry-run
 *   node --env-file=.env create-unity-default-package.mjs
 *
 * Sau đó upload lên R2 bằng upload-unity-template-catalog.mjs.
 */

import { createHash, randomBytes } from "node:crypto";
import {
  existsSync,
  readFileSync,
  readdirSync,
  statSync,
  mkdirSync,
  writeFileSync,
  rmSync,
} from "node:fs";
import { join, relative, dirname } from "node:path";
import { execSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const __dirname = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = join(__dirname, "..");

// Paths
const DEFAULT_SETUP_DIR = join(
  REPO_ROOT,
  "templates",
  "unity-project",
  "DefaultSetup"
);
const PACKAGE_TEMPLATE_DIR = join(
  REPO_ROOT,
  "templates",
  "unity-project",
  "PackageTemplate"
);
const OUTPUT_PACKAGE = join(PACKAGE_TEMPLATE_DIR, "ezg.base.default.unitypackage");

const DRY_RUN = process.argv.includes("--dry-run");

// Files/patterns cần loại bỏ (tên chính xác)
const EXCLUDE_NAMES = new Set([
  ".DS_Store",
  "settings.local.json",
  "settings.json", // file Claude cá nhân, không phân phối
  "worktrees", // thư mục worktree git cá nhân
]);

function newGuid() {
  return randomBytes(16).toString("hex");
}

/**
 * Collect tất cả files và dirs đệ quy từ srcPath.
 * Dereference symlinks tự động (followSymlinks).
 * @param {string} srcPath  absolute path của file/dir nguồn
 * @param {string} targetPrefix  prefix đường dẫn trong package (ví dụ ".agents")
 * @returns {Array<{type: 'file'|'dir', srcPath: string, packagePath: string}>}
 */
function collectEntries(srcPath, targetPrefix) {
  const entries = [];
  const stat = statSync(srcPath, { followSymlinks: true });

  if (stat.isDirectory()) {
    // Thêm entry cho chính thư mục này
    entries.push({ type: "dir", srcPath, packagePath: targetPrefix });

    const children = readdirSync(srcPath);
    for (const child of children) {
      if (EXCLUDE_NAMES.has(child)) continue;
      const childSrc = join(srcPath, child);
      const childTarget = targetPrefix ? `${targetPrefix}/${child}` : child;
      entries.push(...collectEntries(childSrc, childTarget));
    }
  } else {
    entries.push({ type: "file", srcPath, packagePath: targetPrefix });
  }
  return entries;
}

async function main() {
  console.log("=== Create ezg.base.default.unitypackage ===\n");

  if (!existsSync(DEFAULT_SETUP_DIR)) {
    throw new Error(`DefaultSetup directory not found at: ${DEFAULT_SETUP_DIR}`);
  }

  // Quét toàn bộ nội dung trong DefaultSetup
  const allEntries = [];
  const topLevelChildren = readdirSync(DEFAULT_SETUP_DIR);

  for (const child of topLevelChildren) {
    if (EXCLUDE_NAMES.has(child)) continue;
    const srcPath = join(DEFAULT_SETUP_DIR, child);
    const collected = collectEntries(srcPath, child);
    allEntries.push(...collected);
  }

  const files = allEntries.filter((e) => e.type === "file");
  const dirs = allEntries.filter((e) => e.type === "dir");
  console.log(`  Dirs : ${dirs.length}`);
  console.log(`  Files: ${files.length}`);
  console.log(`  Total: ${allEntries.length} entries\n`);

  if (DRY_RUN) {
    for (const entry of allEntries) {
      const prefix = entry.type === "dir" ? "d" : "f";
      console.log(`  ${prefix} ${entry.packagePath}`);
    }
    console.log(`\n[dry-run] Would write: ${OUTPUT_PACKAGE}`);
    return { sha256: null };
  }

  // Tạo temp dir
  const tmpDir = `/tmp/ezg-default-pkg-${Date.now()}`;
  mkdirSync(tmpDir, { recursive: true });

  try {
    // Build GUID-based structure
    for (const entry of allEntries) {
      const guid = newGuid();
      const guidDir = join(tmpDir, guid);
      mkdirSync(guidDir, { recursive: true });

      // pathname dùng forward slashes
      const pathname = entry.packagePath.replace(/\\/g, "/");
      writeFileSync(join(guidDir, "pathname"), pathname, "utf8");

      if (entry.type === "file") {
        const content = readFileSync(entry.srcPath);
        writeFileSync(join(guidDir, "asset"), content);
        console.log(`  + ${pathname}`);
      } else {
        console.log(`  d ${pathname}/`);
      }
    }

    // Tạo gzip tarball
    console.log(`\nPacking...`);
    execSync(`tar -czf "${OUTPUT_PACKAGE}" -C "${tmpDir}" .`, {
      stdio: "inherit",
    });

    const sha256 = createHash("sha256")
      .update(readFileSync(OUTPUT_PACKAGE))
      .digest("hex");
    const sizeBytes = statSync(OUTPUT_PACKAGE).size;

    console.log(`\n✓ Created: ${OUTPUT_PACKAGE}`);
    console.log(`  Size   : ${(sizeBytes / 1024).toFixed(1)} KB`);
    console.log(`  SHA-256: ${sha256}`);

    return { sha256, sizeBytes };
  } finally {
    rmSync(tmpDir, { recursive: true, force: true });
  }
}

main().catch((err) => {
  console.error(err.message || err);
  process.exit(1);
});
