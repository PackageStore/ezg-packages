// Tiện ích file cho script khảo sát: tìm gốc project Unity, duyệt Assets, đọc file an toàn. Không phụ thuộc gói ngoài.
import fs from "node:fs";
import path from "node:path";

/** Đi lên từ `start` tới khi gặp ProjectSettings/ProjectVersion.txt (gốc project Unity). null nếu không có. */
export function findProjectRoot(start) {
  let cur = path.resolve(start || process.cwd());
  for (;;) {
    if (fs.existsSync(path.join(cur, "ProjectSettings", "ProjectVersion.txt")) && fs.existsSync(path.join(cur, "Assets"))) return cur;
    const parent = path.dirname(cur);
    if (parent === cur) return null;
    cur = parent;
  }
}

/** Đọc text (UTF-8, bỏ BOM); null nếu không đọc được. */
export function readText(file) {
  try {
    const s = fs.readFileSync(file, "utf8");
    return s.charCodeAt(0) === 0xfeff ? s.slice(1) : s;
  } catch {
    return null;
  }
}

export function exists(p) {
  try {
    fs.accessSync(p);
    return true;
  } catch {
    return false;
  }
}

/** Đường dẫn project dạng Assets/… (dấu /) từ đường dẫn tuyệt đối. */
export function rel(root, abs) {
  return path.relative(root, abs).split(path.sep).join("/");
}

// Folder không bao giờ đi vào: Unity bỏ qua (~, dấu chấm đầu) hoặc không phải nguồn của project.
const SKIP_DIR = new Set(["Library", "Temp", "Logs", "obj", "Build", "Builds", "UserSettings", "MemoryCaptures", "Recordings"]);

/**
 * Duyệt cây, gọi `visit(abs, name)` cho mỗi file có đuôi trong `exts` (vd [".cs", ".prefab"]). Bỏ folder Unity không import
 * (kết thúc bằng ~, bắt đầu bằng .) và folder sinh ra.
 */
export function walk(dir, exts, visit) {
  let entries;
  try {
    entries = fs.readdirSync(dir, { withFileTypes: true });
  } catch {
    return;
  }
  for (const e of entries) {
    const name = e.name;
    const abs = path.join(dir, name);
    if (e.isDirectory()) {
      if (name.startsWith(".") || name.endsWith("~") || SKIP_DIR.has(name)) continue;
      walk(abs, exts, visit);
    } else if (e.isFile()) {
      const lower = name.toLowerCase();
      for (const x of exts) {
        if (lower.endsWith(x)) {
          visit(abs, name);
          break;
        }
      }
    }
  }
}

/** Ghi file, tạo folder cha. */
export function writeText(file, text) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, text, "utf8");
}

/** Đếm dòng hữu ích cho báo cáo: giữ tối đa n phần tử. */
export function take(list, n) {
  return list.length > n ? list.slice(0, n) : list;
}
