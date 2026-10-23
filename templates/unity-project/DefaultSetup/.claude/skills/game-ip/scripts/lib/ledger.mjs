// Sổ nguồn gốc (QC 3): ProjectSettings/GameIPLedger.json. Đọc, tính độ phủ trên asset vào build, sinh mục draft từ khảo sát (init)
// và gộp với sổ có sẵn mà không đụng mục cũ. Skill không bao giờ tự đặt status "confirmed".
import path from "node:path";
import { commonDir, globToRegExp, readText } from "./fsutil.mjs";
import { licenseInfo } from "./licenses.mjs";

export const LEDGER_FILE = path.join("ProjectSettings", "GameIPLedger.json");

export function loadLedger(root, file = null) {
  const txt = readText(file || path.join(root, LEDGER_FILE));
  if (!txt) return null;
  try {
    const j = JSON.parse(txt);
    return Array.isArray(j.entries) ? j : null;
  } catch {
    return null;
  }
}

/** Độ cụ thể của một glob: số ký tự trước ký tự đại diện đầu tiên (đường dẫn đầy đủ thắng folder). */
function specificity(glob) {
  const i = glob.search(/[*?]/);
  return i < 0 ? 100000 + glob.length : i;
}

function compile(entries) {
  return entries.map((e) => ({
    e,
    globs: (e.paths || []).map((g) => ({ re: globToRegExp(g), spec: specificity(g) })),
    guids: new Set(e.guids || []),
  }));
}

/** Mục sổ phủ một asset: GUID khớp thắng; không thì glob cụ thể nhất. */
function matchEntry(compiled, p, guid) {
  let best = null;
  let bestSpec = -1;
  for (const c of compiled) {
    if (guid && c.guids.has(guid)) return c.e;
    for (const g of c.globs) if (g.spec > bestSpec && g.re.test(p)) (best = c.e), (bestSpec = g.spec);
  }
  return best;
}

/** Hàm (path, guid) → mục sổ phủ asset đó, hoặc null. */
export function makeMatcher(entries) {
  const compiled = compile(entries);
  return (p, guid) => matchEntry(compiled, p, guid);
}

/** Nhóm đường dẫn chưa có nguồn theo folder (3 đoạn đầu) để báo cáo gọn. */
function groupDir(p) {
  const parts = p.split("/");
  return parts.slice(0, Math.min(parts.length - 1, 3)).join("/");
}

/**
 * Độ phủ của sổ trên asset vào build: confirmed / draft / chưa có, nhóm chưa có theo folder, số asset mỗi mục phủ,
 * mục không phủ gì (đường dẫn cũ).
 */
export function coverage(ledger, index, inBuild) {
  const compiled = compile(ledger.entries);
  const byEntry = new Map();
  const uncovered = new Map();
  let confirmed = 0;
  let draft = 0;
  for (const p of inBuild.keys()) {
    const f = index.byPath.get(p);
    const e = matchEntry(compiled, p, f && f.guid);
    if (!e) {
      const d = groupDir(p);
      if (!uncovered.has(d)) uncovered.set(d, []);
      uncovered.get(d).push(p);
      continue;
    }
    byEntry.set(e.id, (byEntry.get(e.id) || 0) + 1);
    if (e.status === "confirmed") confirmed++;
    else draft++;
  }
  const total = inBuild.size;
  return {
    total,
    confirmed,
    draft,
    uncovered: total - confirmed - draft,
    percentCovered: total ? Math.round(((confirmed + draft) * 1000) / total) / 10 : 100,
    uncoveredGroups: [...uncovered.entries()].map(([dir, list]) => ({ dir, count: list.length, sample: list.slice(0, 3) })).sort((a, b) => b.count - a.count),
    byEntry: Object.fromEntries(byEntry),
    unusedEntries: ledger.entries.filter((e) => !byEntry.has(e.id)).map((e) => e.id),
  };
}

// ---------- init: sinh mục draft ----------

function slug(s) {
  return (s || "nguon")
    .normalize("NFD")
    .replace(/[̀-ͯ]/g, "")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 60) || "nguon";
}

// Phạm vi mặc định theo loại nguồn (QC 3.4).
function defaultScope(kind, license) {
  const L = licenseInfo(license);
  const unknown = "unknown";
  switch (kind) {
    case "inhouse":
      return { game: true, ads: true, trailer: true, store: true, redistributeSource: false };
    case "asset-store":
    case "marketplace":
    case "stock":
    case "third-party":
      return { game: L.commercial === true ? true : unknown, ads: L.ads === true ? true : unknown, trailer: L.ads === true ? true : unknown, store: L.ads === true ? true : unknown, redistributeSource: L.redistributeSource === true };
    case "publisher-sdk":
      return { game: unknown, ads: unknown, trailer: unknown, store: unknown, redistributeSource: false };
    default:
      return { game: L.commercial === true ? true : unknown, ads: L.ads === true ? true : unknown, trailer: L.ads === true ? true : unknown, store: L.ads === true ? true : unknown, redistributeSource: L.redistributeSource === true };
  }
}

function entry(fields) {
  const kind = fields.kind;
  const license = fields.license || "unknown";
  return {
    id: fields.id,
    kind,
    title: fields.title || "",
    author: fields.author || "",
    source: fields.source || "",
    license,
    licenseFile: fields.licenseFile || "",
    scope: fields.scope || defaultScope(kind, license),
    proof: "",
    attribution: fields.attribution || "",
    limits: fields.limits || "",
    expires: null,
    paths: fields.paths || [],
    status: "draft",
    evidence: fields.evidence || [],
    notes: fields.notes || "",
  };
}

const VENDOR_KIND_FROM_LICENSE = (lic) => (["MIT", "Apache-2.0", "BSD-2-Clause", "BSD-3-Clause", "Zlib", "ISC", "Unlicense", "MPL-2.0", "LGPL-2.1", "LGPL-3.0", "GPL-2.0", "GPL-3.0", "AGPL-3.0"].includes(lic) ? "oss" : lic && lic.startsWith("CC") ? "marketplace" : null);

/** Mục draft từ kết quả survey. Thứ tự: pack Asset Store (GUID), folder bên thứ ba, package, font, yêu cầu ghi công, phần còn lại (tự làm?). */
export function candidatesFromSurvey(s) {
  const out = [];
  const ids = new Set();
  const add = (fields) => {
    let id = slug(fields.id || fields.title);
    for (let n = 2; ids.has(id); n++) id = `${slug(fields.id || fields.title)}-${n}`;
    ids.add(id);
    const e = entry({ ...fields, id });
    out.push(e);
    return e;
  };

  // 1. Pack Asset Store khớp GUID
  const coveredByStore = new Set();
  for (const a of s.assetStore) {
    const paths = s.assetStorePaths[`${a.publisher}/${a.name}`] || [];
    if (!paths.length) continue;
    const dir = commonDir(paths);
    const inside = paths.filter((p) => p.startsWith(`${dir}/`)).length;
    const useDir = dir.split("/").length >= 2 && inside / paths.length >= 0.9;
    for (const p of paths) coveredByStore.add(p);
    add({
      id: `as-${a.publisher}-${a.name}`,
      kind: "asset-store",
      title: a.name,
      author: a.publisher,
      license: "unity-asset-store-eula",
      paths: useDir ? [`${dir}/**`] : paths,
      evidence: [`GUID khớp gói Asset Store trong cache: ${a.matched}/${a.packageEntries} mục của gói${a.moved ? `, ${a.moved} file đã dời chỗ` : ""}`],
      notes: a.category ? `Loại trên Asset Store: ${a.category}` : "",
    });
  }

  // 2. Folder bên thứ ba
  for (const v of s.vendorFolders) {
    if (v.files && [...coveredByStore].filter((p) => p.startsWith(`${v.path}/`)).length >= 0.9 * v.files) continue;
    const licFound = (v.licenseFiles || []).find((l) => l.license);
    const known = v.known || (v.sampleOf ? { title: `Mẫu của package ${v.sampleOf.title}`, author: v.sampleOf.author || "", license: v.sampleOf.license } : null);
    let kind = v.known ? v.known.kind : null;
    let license = known ? known.license : null;
    if (!license && licFound) license = licFound.license;
    if (!kind) kind = v.storeUrl || v.evidence.includes("readme nhắc Asset Store") ? "asset-store" : VENDOR_KIND_FROM_LICENSE(license) || "third-party";
    if (!license && kind === "asset-store") license = "unity-asset-store-eula";
    const tail = v.path.split("/").slice(v.generic ? -2 : -1);
    add({
      id: `vendor-${tail.join("-")}`,
      kind,
      title: known ? known.title : v.generic ? `${tail.join("/")} (chưa rõ pack)` : tail[0],
      author: known ? known.author : "",
      source: v.storeUrl || "",
      license: license || "unknown",
      licenseFile: licFound ? licFound.file : "",
      paths: [`${v.path}/**`],
      limits: v.known ? v.known.limits || "" : "",
      evidence: v.evidence,
      notes: v.known && v.known.notes ? v.known.notes : "",
    });
  }

  // 3. Package
  const unityPkgs = s.packages.filter((p) => p.name.startsWith("com.unity."));
  if (unityPkgs.length) add({ id: "unity-packages", kind: "unity", title: "Package của Unity (com.unity.*)", author: "Unity", license: "unity-companion", paths: ["Packages/com.unity.*/**"], evidence: [`${unityPkgs.length} package com.unity.*`] });
  // Gói của công ty: một mục inhouse mỗi tác giả, người duyệt xác nhận.
  const ownPkgs = new Map();
  for (const p of s.packages.filter((x) => x.own)) {
    if (!ownPkgs.has(p.author)) ownPkgs.set(p.author, []);
    ownPkgs.get(p.author).push(p);
  }
  for (const [author, list] of ownPkgs) {
    add({
      id: `inhouse-packages-${author}`,
      kind: "inhouse",
      title: `Package của công ty (${author})`,
      author,
      license: "inhouse",
      paths: list.map((p) => `Packages/${p.name}/**`),
      evidence: [`${list.length} package: ${list[0].own}`],
      notes: "Xác nhận đây là code công ty viết; gói nào chép từ bên ngoài (vd bản sửa của thư viện mã nguồn mở) thì tách thành mục riêng có license gốc",
    });
  }
  for (const p of s.packages) {
    if (p.name.startsWith("com.unity.") || p.own) continue;
    add({
      id: `pkg-${p.name}`,
      kind: (p.known && p.known.kind) || VENDOR_KIND_FROM_LICENSE(p.license) || "package",
      title: p.displayName || p.name,
      author: p.author || (p.known && p.known.author) || "",
      license: p.license || (p.known && p.known.license) || "unknown",
      licenseFile: p.licenseFile || "",
      paths: [`Packages/${p.name}/**`],
      evidence: [`package ${p.name}${p.version ? ` ${p.version}` : ""}${p.source ? ` (${p.source})` : ""}`, ...(p.known ? [`package không ghi license; nhà cung cấp quen: ${p.known.title}`] : [])],
      notes: p.known && p.known.notes ? p.known.notes : "",
    });
  }

  // 4. Font: gộp theo họ + license + dòng bản quyền; chỉ font vào build hoặc nằm ngoài folder bên thứ ba.
  const fontGroups = new Map();
  for (const f of s.fonts) {
    const inVendor = s.vendorFolders.some((v) => f.path.startsWith(`${v.path}/`));
    if (!f.inBuild && inVendor) continue;
    if (f.path.startsWith("Packages/com.unity.")) continue; // mục unity-packages phủ
    const key = `${f.family || f.path}|${f.license}|${f.copyright || ""}`;
    if (!fontGroups.has(key)) fontGroups.set(key, { ...f, paths: [] });
    fontGroups.get(key).paths.push(f.path);
  }
  for (const g of fontGroups.values()) {
    const L = licenseInfo(g.license || "unknown");
    const license = g.license || (g.embedding === "restricted" ? "commercial-font" : "unknown");
    add({
      id: `font-${g.family || path.basename(g.path)}`,
      kind: "font",
      title: g.family || path.basename(g.path),
      author: g.vendor || "",
      source: g.licenseUrl || "",
      license,
      licenseFile: g.licenseFilesBeside[0] || "",
      paths: g.paths,
      attribution: L.attribution && g.copyright ? `${g.copyright} — ${L.name}` : "",
      evidence: [`bảng name của font${g.embedding ? `, fsType ${g.fsType} (${g.embedding})` : ""}${g.reservedName ? ", có Reserved Font Name" : ""}`],
      notes: g.licenseText && !g.license ? `Mô tả license trong font: ${g.licenseText.slice(0, 200)}` : "",
    });
  }

  // 5. Yêu cầu ghi công nằm ngoài folder bên thứ ba: gộp theo nội dung yêu cầu
  const demandGroups = new Map();
  for (const d of s.creditDemands) {
    if (d.vendor) {
      const ve = out.find((e) => e.paths.includes(`${d.vendor}/**`));
      if (ve && !ve.attribution) ve.attribution = `Xem ${d.path}: ${d.snippet.slice(0, 200)}`;
      continue;
    }
    const key = d.snippet.replace(/[^a-z]/gi, "").toLowerCase().slice(0, 200);
    if (!demandGroups.has(key)) demandGroups.set(key, { snippet: d.snippet, files: [] });
    demandGroups.get(key).files.push(d.path);
  }
  for (const g of demandGroups.values()) {
    const dirs = g.files.map((f) => f.slice(0, f.lastIndexOf("/")));
    const base = dirs.length > 1 ? commonDir(dirs.map((d) => `${d}/x`)) : dirs[0];
    add({
      id: `credit-${base.split("/").pop()}`,
      kind: "third-party",
      title: `Nguồn có yêu cầu ghi công (${base})`,
      license: "custom",
      licenseFile: g.files[0],
      paths: dirs.length > 1 ? dirs.map((d) => `${d}/**`) : [`${base}/**`],
      attribution: `Xem ${g.files[0]}: ${g.snippet.slice(0, 200)}`,
      evidence: [`${g.files.length} file yêu cầu ghi công`],
    });
  }

  // 6. Phần còn lại vào build: tự làm? theo folder 3 đoạn đầu
  const compiled = compile(out);
  const rest = new Map();
  for (const { path: p } of s.inBuildList) {
    if (!p.startsWith("Assets/")) continue;
    if (matchEntry(compiled, p, null)) continue;
    const d = groupDir(p);
    if (!rest.has(d)) rest.set(d, 0);
    rest.set(d, rest.get(d) + 1);
  }
  for (const [d, n] of [...rest.entries()].sort()) {
    add({
      id: `inhouse-${d.split("/").slice(1).join("-")}`,
      kind: "inhouse",
      title: `Tự làm? (${d})`,
      license: "inhouse",
      paths: [`${d}/**`],
      evidence: [`${n} asset vào build chưa khớp nguồn nào khác; máy đoán là tự làm, người phải xác nhận`],
    });
  }
  return out;
}

/** Gộp: giữ nguyên mọi mục cũ; thêm mục mới khi id chưa có và mục đó phủ được asset vào build mà sổ cũ chưa phủ. */
export function mergeLedger(existing, candidates, inBuildList, byPath) {
  const entries = existing ? [...existing.entries] : [];
  const ids = new Set(entries.map((e) => e.id));
  const added = [];
  let compiled = compile(entries);
  for (const c of candidates) {
    if (ids.has(c.id)) continue;
    const cc = compile([c]);
    const useful = inBuildList.some(({ path: p }) => {
      const f = byPath ? byPath.get(p) : null;
      return !matchEntry(compiled, p, f && f.guid) && matchEntry(cc, p, f && f.guid);
    });
    if (!useful && existing) continue;
    entries.push(c);
    ids.add(c.id);
    added.push(c.id);
    compiled = compile(entries);
  }
  return { ledger: { version: 1, generator: existing ? existing.generator : "game-ip init", entries }, added };
}
