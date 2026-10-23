// Tiện ích file: tìm gốc project Unity, duyệt folder theo luật bỏ qua của Unity, đọc file an toàn, glob. Không phụ thuộc gói ngoài.
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

export function writeText(file, text) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, text);
}

export function exists(p) {
  try {
    fs.accessSync(p);
    return true;
  } catch {
    return false;
  }
}

/** Đường dẫn dạng Assets/… (dấu /) từ đường dẫn tuyệt đối. */
export function rel(root, abs) {
  return path.relative(root, abs).split(path.sep).join("/");
}

// Unity bỏ qua trong Assets và Packages: tên bắt đầu bằng '.', kết thúc bằng '~', tên 'cvs', đuôi '.tmp'.
export const unityIgnored = (name) => name.startsWith(".") || name.endsWith("~") || name.toLowerCase() === "cvs" || name.endsWith(".tmp");

/** Mọi file dưới `dir` (đệ quy), bỏ folder Unity bỏ qua. `maxDepth` để chặn cây quá sâu. */
export function walkFiles(dir, { maxDepth = 64, skipDir = unityIgnored } = {}) {
  const out = [];
  (function walk(d, depth) {
    let ents;
    try {
      ents = fs.readdirSync(d, { withFileTypes: true });
    } catch {
      return;
    }
    for (const e of ents) {
      const p = path.join(d, e.name);
      if (e.isDirectory()) {
        if (!skipDir(e.name) && depth < maxDepth) walk(p, depth + 1);
      } else out.push(p);
    }
  })(dir, 0);
  return out;
}

/** Đọc `length` byte từ `pos` của file đang mở; trả về Buffer đúng số byte đọc được. */
export function readAt(fd, pos, length) {
  const buf = Buffer.alloc(length);
  const n = fs.readSync(fd, buf, 0, length, pos);
  return n === length ? buf : buf.subarray(0, n);
}

/** Mở file, gọi fn(fd, size), luôn đóng. Lỗi đọc → null. */
export function withFile(file, fn) {
  let fd;
  try {
    fd = fs.openSync(file, "r");
    const size = fs.fstatSync(fd).size;
    return fn(fd, size);
  } catch {
    return null;
  } finally {
    if (fd !== undefined) fs.closeSync(fd);
  }
}

/** Glob đường dẫn (dấu /): `**` = mọi thứ kể cả /, `*` = mọi thứ trong một đoạn, `?` = một ký tự. Không phân biệt hoa thường. */
export function globToRegExp(glob) {
  let re = "";
  for (let i = 0; i < glob.length; i++) {
    const c = glob[i];
    if (c === "*") {
      if (glob[i + 1] === "*") {
        i++;
        if (glob[i + 1] === "/") {
          i++;
          re += "(?:.*/)?";
        } else re += ".*";
      } else re += "[^/]*";
    } else if (c === "?") re += "[^/]";
    else re += c.replace(/[.+^${}()|[\]\\]/g, "\\$&");
  }
  return new RegExp(`^${re}$`, "i");
}

/** Gốc chung dài nhất (theo đoạn /) của các đường dẫn. */
export function commonDir(paths) {
  if (!paths.length) return "";
  let parts = paths[0].split("/").slice(0, -1);
  for (const p of paths) {
    const q = p.split("/");
    let i = 0;
    while (i < parts.length && i < q.length - 1 && parts[i] === q[i]) i++;
    parts = parts.slice(0, i);
  }
  return parts.join("/");
}
