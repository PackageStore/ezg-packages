// Viết báo cáo khảo sát (Markdown), file luật riêng của project (GameIPProject.json) và ghi chú (GameIPProject.md).
import { licenseInfo } from "./licenses.mjs";

const yes = (b) => (b ? "có" : "không");
const cell = (s) => String(s ?? "").replace(/\|/g, "\\|").replace(/\r?\n/g, " ").slice(0, 160);

function table(head, rows) {
  if (!rows.length) return "_(không có)_\n";
  return `| ${head.join(" | ")} |\n|${head.map(() => "---").join("|")}|\n${rows.map((r) => `| ${r.map(cell).join(" | ")} |`).join("\n")}\n`;
}

function limit(list, n) {
  return { rows: list.slice(0, n), more: Math.max(0, list.length - n) };
}

function more(n) {
  return n ? `\n_… và ${n} mục nữa (xem file .json)._\n` : "";
}

/** Mức của font theo QC 4.3: ✓ / ! / ✗. */
export function fontVerdict(f, entry = null) {
  if (f.embedding === "restricted" && !["OFL-1.1", "Apache-2.0", "UFL-1.0"].includes(f.license)) return ["✗", "fsType Restricted: không được nhúng khi chưa có license app"];
  if (["OFL-1.1", "Apache-2.0", "UFL-1.0", "CC0-1.0", "MIT"].includes(f.license)) return ["✓", `${licenseInfo(f.license).name}; ghi công trong màn Credits`];
  if (/owned by|all rights reserved|proprietary|custom font/i.test(`${f.licenseText || ""} ${f.copyright || ""}`)) return ["!", "font độc quyền: cần license app hoặc hợp đồng"];
  if (f.embedding === "preview-print" || f.embedding === "editable") return ["!", `fsType ${f.embedding}: font thương mại, cần license app`];
  if (entry && ["OFL-1.1", "Apache-2.0", "UFL-1.0", "CC0-1.0", "MIT"].includes(entry.license)) return ["✓", `sổ ghi ${licenseInfo(entry.license).name} (\`${entry.id}\`)`];
  if (entry && entry.license && entry.license !== "unknown") return ["!",`file không ghi license, sổ ghi ${licenseInfo(entry.license).name} (\`${entry.id}\`): cần bằng chứng (\`proof\`)`];
  return ["✗", entry ?`không thấy license trong file, mục sổ \`${entry.id}\` chưa ghi license` : "không thấy license trong file, chưa có mục sổ"];
}

export function surveyReport(s) {
  const L = [];
  const p = s.project;
  L.push(`# Khảo sát Game IP: ${p.productName || "(chưa đặt tên)"}`);
  L.push("");
  L.push(`Sinh bởi skill game-ip ${s.tool.version} lúc ${s.generatedAt} (${s.seconds} giây). Chỉ đọc, không sửa project. Ghi đè mỗi lần chạy, không sửa tay.`);
  L.push("Đây là dữ kiện cho sổ nguồn gốc và gate (GameIP_QuyChuan.md); máy chỉ đoán, người xác nhận. Không phải tư vấn pháp lý.");
  L.push("");
  L.push("## 1. Tổng quan");
  L.push("");
  const c = s.counts;
  L.push(table(["", "Giá trị"], [
    ["Unity", p.unityVersion || "?"],
    ["Công ty / sản phẩm (PlayerSettings)", `${p.companyName || "?"} / ${p.productName || "?"}`],
    ["Tên coi là của mình (metadata)", p.ownNames.join(", ") || "(chưa có: thêm ownNames vào GameIPProject.json)"],
    ["File (Assets + package)", c.files],
    ["Asset vào build", `${c.inBuild} (trong Assets: ${c.inBuildAssets})`],
    ["Lý do vào build", Object.entries(c.byReason).map(([k, v]) => `${k} ${v}`).join(", ")],
    ["Scene trong Build Settings", `${c.scenesEnabled} bật / ${c.scenes}`],
    ["Danh sách build thật (gói Unity)", c.buildReport ? `lần build ${c.buildReport.generatedAt} (${c.buildReport.target}${c.buildReport.development ? ", Development" : ""}), ${c.buildReport.assets} asset; thêm ${c.buildReport.added} asset ước lượng bỏ sót` : "chưa có (cài gói com.ezg.game-ip rồi build một lần để có Library/GameIP/build_assets.json); đang dùng ước lượng theo YAML"],
    ["File GameIPProject.json", yes(p.hasProjectFile)],
    ["Sổ GameIPLedger.json", yes(p.hasLedger)],
  ]));
  if (s.coverage) {
    const cv = s.coverage;
    L.push("");
    L.push(`**Độ phủ của sổ trên asset vào build**: ${cv.percentCovered}% (${cv.confirmed} đã xác nhận, ${cv.draft} draft, ${cv.uncovered} chưa có nguồn).`);
    if (cv.uncoveredGroups.length) {
      const { rows, more: m } = limit(cv.uncoveredGroups, 20);
      L.push("");
      L.push(table(["Folder chưa có nguồn", "Số asset", "Ví dụ"], rows.map((g) => [g.dir, g.count, g.sample[0]])) + more(m));
    }
    if (cv.unusedEntries.length) L.push(`\nMục sổ không phủ asset vào build nào (đường dẫn cũ, hoặc chưa dùng): ${cv.unusedEntries.join(", ")}`);
  }
  L.push("");
  L.push("## 2. Pack Asset Store khớp GUID");
  L.push("");
  L.push(`Cache đã đọc: ${s.assetStoreCaches.length ? s.assetStoreCaches.join("; ") : "(không có trên máy này; chạy lại trên máy đã tải pack, hoặc --asset-store-cache <folder>)"}`);
  L.push("");
  L.push(table(["Publisher", "Gói", "Khớp / mục của gói", "Đã dời chỗ", "Vào build", "Ví dụ"], s.assetStore.filter((a) => !a.error).map((a) => [a.publisher, a.name, `${a.matched} / ${a.packageEntries}`, a.moved, a.inBuild, (a.samplePaths || [])[0]])));
  L.push("");
  L.push("## 3. Folder bên thứ ba (đoán)");
  L.push("");
  L.push(table(["Folder", "Nhận ra", "Bằng chứng", "License tìm thấy", "Vào build"], s.vendorFolders.map((v) => [
    v.path,
    v.known ? `${v.known.title} (${v.known.kind})` : "",
    v.evidence.join("; ") + (v.storeUrl ? `; ${v.storeUrl}` : ""),
    (v.licenseFiles || []).map((l) => `${l.license || "?"} (${l.file.split("/").pop()})`).join(", "),
    `${v.inBuild} / ${v.files}`,
  ])));
  L.push("");
  L.push("## 4. Package");
  L.push("");
  const pk = s.packages.filter((x) => !x.name.startsWith("com.unity."));
  L.push(`${s.packages.length - pk.length} package com.unity.* (Unity Companion License, gộp một mục). Package khác:`);
  L.push("");
  L.push(table(["Package", "Bản", "Nguồn", "License", "File license", "Vào build"], pk.map((x) => [x.name, x.version, x.source, x.license || "?", x.licenseFile || "", x.inBuildFiles])));
  L.push("");
  L.push("## 5. Font (QC 4.3)");
  L.push("");
  L.push(table(["", "Font", "Họ", "License", "fsType", "Vào build", "Ghi chú"], s.fonts.map((f) => {
    const [mark, why] = fontVerdict(f);
    return [mark, f.path, f.family, f.license || (f.licenseText ? f.licenseText.slice(0, 60) : "?"), f.embedding ? `${f.fsType} ${f.embedding}` : "", yes(f.inBuild), why];
  })));
  L.push("");
  L.push("## 6. Âm thanh (QC 4.4)");
  L.push("");
  L.push(`${s.audio.total} file (${s.audio.inBuild} vào build); ${s.audio.tagged.length} file có tag, ${s.audio.tagged.filter((a) => a.sourceLike && !a.own).length} file có tag nói về nguồn khác.`);
  L.push("");
  const tagRows = s.audio.tagged.filter((a) => a.sourceLike && !a.own);
  const at = limit(tagRows, 40);
  L.push(table(["File", "Vào build", "Tag"], at.rows.map((a) => [a.path, yes(a.inBuild), Object.entries(a.tags).map(([k, v]) => `${k}: ${v}`).join("; ")])) + more(at.more));
  L.push("");
  L.push("## 7. Ảnh (QC 4.7, 4.8)");
  L.push("");
  L.push(`${s.images.total} ảnh PNG / JPEG / PSD (${s.images.inBuild} vào build). Metadata ghi tác giả, bản quyền, nguồn không phải của mình: ${s.images.foreign.length}. Dấu hiệu AI: ${s.images.ai.length}.`);
  L.push("");
  const fi = limit(s.images.foreign, 40);
  L.push(table(["Ảnh", "Vào build", "Tác giả", "Bản quyền", "Credit / nguồn"], fi.rows.map((i) => [i.path, yes(i.inBuild), i.creator, i.rights, [i.credit, i.source, i.webStatement].filter(Boolean).join("; ")])) + more(fi.more));
  L.push("");
  const ai = limit(s.images.ai, 40);
  L.push(table(["Ảnh có dấu hiệu AI", "Vào build", "Dấu hiệu"], ai.rows.map((i) => [i.path, yes(i.inBuild), i.signal])) + more(ai.more));
  L.push("");
  L.push("## 8. Tên file đáng ngờ (QC 4.8)");
  L.push("");
  const su = limit(s.suspicious.filter((x) => !x.vendor), 60);
  L.push(`${s.suspicious.length} file (${s.suspicious.filter((x) => x.vendor).length} nằm trong folder bên thứ ba, không liệt kê).`);
  L.push("");
  L.push(table(["File", "Vào build", "Vì sao"], su.rows.map((x) => [x.path, yes(x.inBuild), x.why])) + more(su.more));
  L.push("");
  L.push("## 9. Code dịch ngược, file cài đặt game (QC 4.18)");
  L.push("");
  L.push(table(["File", "Vào build", "Dấu vết", "Trong folder bên thứ ba"], s.decompiled.map((d) => [d.path, yes(d.inBuild), d.marker, d.vendor || ""])));
  L.push("");
  L.push(table(["File cài đặt", "MB", "Tên giống sản phẩm của mình"], s.installers.map((i) => [i.path, Math.round(i.size / 1e5) / 10, yes(i.mayBeOwnBuild)])));
  L.push("");
  L.push("## 10. Tên game khác trong đường dẫn (QC 4.13)");
  L.push("");
  L.push("Tín hiệu một phần của project bám một game cụ thể: chạy thẻ so sánh (QC 6) cho phần đó. Không đổi tên để che (UngPho mục 0).");
  L.push("");
  L.push(table(["Tên game", "Từ", "Đoạn đường dẫn", "Số file", "Ví dụ", "Trong folder bên thứ ba"], s.gameNames.flatMap((g) => g.segments.map((x) => [g.name, g.from, x.segment, x.count, x.sample[0], x.vendor || ""]))));
  L.push("");
  L.push("## 11. File license, yêu cầu ghi công (QC 4.5, 4.6)");
  L.push("");
  const lf = limit(s.licenseFiles, 60);
  L.push(table(["File", "License nhận ra", "Folder bên thứ ba"], lf.rows.map((l) => [l.path, l.license || "?", l.vendor || ""])) + more(lf.more));
  L.push("");
  L.push(table(["File yêu cầu ghi công", "Đoạn trích"], s.creditDemands.map((d) => [d.path, d.snippet])));
  L.push("");
  L.push("## 12. Màn Credits / Licenses trong build (QC 4.6)");
  L.push("");
  if (!s.credits.found) L.push("**Không thấy** chữ, script hay file Credits / Licenses nào vào build. Nếu sổ có nghĩa vụ ghi công thì IP-6 sẽ hỏng.");
  else {
    L.push(table(["Chỗ", "Nội dung"], [
      ...s.credits.texts.map((t) => [t.path, t.text]),
      ...s.credits.scripts.map((x) => [x, "script"]),
      ...s.credits.files.map((x) => [x, "file"]),
    ]));
  }
  L.push("");
  L.push("## 13. Thương hiệu thật trong nội dung (QC 4.15)");
  L.push("");
  L.push("Chữ trong CSV / JSON localize, asset, prefab, scene (không tính code, folder bên thứ ba). Logo, hình dạng đặc trưng: người xem.");
  L.push("");
  L.push(table(["Thương hiệu", "Nhóm", "Số lần", "Vào build", "Ví dụ"], (s.brands || []).map((b) => [b.brand, b.category, b.count, yes(b.hits.some((h) => h.inBuild)), `${b.hits[0].path}:${b.hits[0].line} ${b.hits[0].snippet}`])));
  L.push("");
  return L.join("\n");
}

/** Mẫu GameIPProject.json (QC 2). */
export function projectTemplate(s) {
  return {
    markets: ["VN", "US", "EU", "UK", "JP", "KR", "SEA"],
    stores: ["appstore", "googleplay"],
    adNetworks: [],
    ownNames: [s.project.companyName, s.project.productName].filter((n) => n && n !== "DefaultCompany"),
    referenceGames: [],
    competitors: [],
    vendors: [],
    credits: { screen: "", noticesFile: "" },
    strict: false,
  };
}

/** Ghi chú của project (GameIPProject.md): người sửa được, init không ghi đè. */
export function projectNotes(s, ledgerAdded, date) {
  const L = [];
  L.push(`# Game IP: ghi chú của ${s.project.productName || "project"}`);
  L.push("");
  L.push(`Tạo bởi \`game-ip init\` ngày ${date}. File này của người: ghi quyết định, ai xác nhận, ngoại lệ đã được duyệt. Luật chung ở`);
  L.push("GameIP_QuyChuan.md; dữ kiện máy quét ở Logs/GameIPSurvey.md.");
  L.push("");
  L.push("## Cần xác nhận");
  L.push("");
  L.push(`- [ ] Thị trường phát hành trong GameIPProject.json (\`markets\`) đúng chưa.`);
  L.push(`- [ ] \`ownNames\`: tên công ty, studio, game của mình (để metadata ghi tên mình không bị báo).`);
  L.push(`- [ ] \`referenceGames\`, \`competitors\`: game mà project học theo, đối thủ (gate IP-10, IP-13).`);
  L.push(`- [ ] ${ledgerAdded} mục sổ draft trong GameIPLedger.json: người có trách nhiệm (QC 3.5) xác nhận từng mục, điền \`proof\`, phạm vi \`ads\`, rồi đổi \`status\` thành \`confirmed\`.`);
  if (!s.credits.found) L.push("- [ ] Game chưa có màn Credits / Licenses (QC 4.6).");
  if (!s.assetStoreCaches.length) L.push("- [ ] Máy chạy init không có cache Asset Store: chạy lại survey trên máy art đã tải pack để nhận pack theo GUID.");
  L.push("");
  L.push("## Quyết định");
  L.push("");
  L.push("| Ngày | Việc | Ai | Ghi chú |");
  L.push("|---|---|---|---|");
  L.push("");
  return L.join("\n");
}
