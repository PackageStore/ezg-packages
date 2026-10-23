// Đọc danh sách GUID của file .unitypackage (tar.gz, mỗi asset là <guid>/asset, <guid>/asset.meta, <guid>/pathname), và tìm các
// gói trong cache Asset Store của máy (nơi Unity lưu gói đã tải: <cache>/<publisher>/<loại>/<gói>.unitypackage).
// Unity giữ nguyên GUID khi import, nên GUID khớp là bằng chứng chắc chắn asset đến từ gói đó, kể cả khi đã dời folder.
// Gói lớn giải nén lâu: kết quả mỗi gói được nhớ trong <tmp>/game-ip-cache theo đường dẫn + kích thước + ngày sửa.
import crypto from "node:crypto";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import zlib from "node:zlib";
import { walkFiles } from "./fsutil.mjs";

/** Folder cache Asset Store mặc định của máy (có thể không tồn tại), cộng biến môi trường ASSETSTORE_CACHE_PATH nếu có. */
export function defaultAssetStoreCaches() {
  const home = os.homedir();
  const out = [];
  if (process.env.ASSETSTORE_CACHE_PATH) out.push(process.env.ASSETSTORE_CACHE_PATH);
  if (process.platform === "win32") out.push(path.join(process.env.APPDATA || path.join(home, "AppData", "Roaming"), "Unity", "Asset Store-5.x"));
  else if (process.platform === "darwin") out.push(path.join(home, "Library", "Unity", "Asset Store-5.x"));
  else out.push(path.join(home, ".local", "share", "unity3d", "Asset Store-5.x"));
  return out.filter((d) => fs.existsSync(d));
}

/** Gói trong một folder cache: { file, publisher, category, name }. */
export function listCachedPackages(cacheDir) {
  return walkFiles(cacheDir, { maxDepth: 6, skipDir: () => false })
    .filter((f) => f.toLowerCase().endsWith(".unitypackage"))
    .map((file) => {
      const parts = path.relative(cacheDir, file).split(path.sep);
      return {
        file,
        publisher: parts.length > 1 ? parts[0] : "",
        category: parts.slice(1, -1).join("/"),
        name: path.basename(file, path.extname(file)),
      };
    });
}

function cacheFile(file, stat) {
  const key = crypto.createHash("sha1").update(`${file}|${stat.size}|${stat.mtimeMs}`).digest("hex");
  return path.join(os.tmpdir(), "game-ip-cache", `${key}.json`);
}

/** Map guid → pathname của một .unitypackage. Đọc dạng stream, chỉ giữ nội dung các mục `pathname`. */
export async function readUnityPackageGuids(file) {
  const stat = fs.statSync(file);
  const cf = cacheFile(file, stat);
  try {
    return new Map(Object.entries(JSON.parse(fs.readFileSync(cf, "utf8"))));
  } catch {
    /* chưa có trong cache */
  }
  const map = new Map();
  await new Promise((resolve, reject) => {
    let buf = Buffer.alloc(0);
    let skip = 0; // byte dữ liệu (kể cả đệm) còn phải bỏ qua
    let want = null; // { guid, size, padded } khi đang đọc nội dung pathname
    let longName = null;
    const gunzip = zlib.createGunzip();
    gunzip.on("data", (chunk) => {
      buf = buf.length ? Buffer.concat([buf, chunk]) : chunk;
      for (;;) {
        if (skip > 0) {
          const n = Math.min(skip, buf.length);
          buf = buf.subarray(n);
          skip -= n;
          if (skip > 0) return;
        }
        if (want) {
          if (buf.length < want.padded) return;
          map.set(want.guid, buf.toString("utf8", 0, want.size).split("\n")[0].trim());
          buf = buf.subarray(want.padded);
          want = null;
          continue;
        }
        if (buf.length < 512) return;
        const h = buf.subarray(0, 512);
        buf = buf.subarray(512);
        if (h.every((x) => x === 0)) continue;
        let name = longName || h.toString("utf8", 0, 100).replace(/\0.*$/s, "");
        const prefix = h.toString("utf8", 345, 500).replace(/\0.*$/s, "");
        if (!longName && prefix) name = `${prefix}/${name}`;
        longName = null;
        const size = parseInt(h.toString("latin1", 124, 136).replace(/\0.*$/s, "").trim() || "0", 8) || 0;
        const padded = Math.ceil(size / 512) * 512;
        const type = String.fromCharCode(h[156] || 48);
        if (type === "L" || type === "x") {
          // Tên dài (GNU) hoặc PAX: đọc nội dung để lấy tên cho mục kế tiếp.
          if (buf.length < padded) {
            buf = Buffer.concat([h, buf]);
            return;
          }
          const body = buf.toString("utf8", 0, size);
          buf = buf.subarray(padded);
          if (type === "L") longName = body.replace(/\0.*$/s, "");
          else {
            const m = body.match(/\d+ path=([^\n]*)\n/);
            if (m) longName = m[1];
          }
          continue;
        }
        const m = name.replace(/^\.\//, "").match(/^([0-9a-f]{32})\/pathname$/);
        if (m) want = { guid: m[1], size, padded };
        else skip = padded;
      }
    });
    gunzip.on("end", resolve);
    gunzip.on("error", reject);
    fs.createReadStream(file).on("error", reject).pipe(gunzip);
  });
  try {
    fs.mkdirSync(path.dirname(cf), { recursive: true });
    fs.writeFileSync(cf, JSON.stringify(Object.fromEntries(map)));
  } catch {
    /* không ghi được cache: lần sau đọc lại */
  }
  return map;
}
