// Gate máy kiểm được của quy chuẩn Game IP (QC 4) trên kết quả survey + sổ nguồn gốc + nợ, và nội dung màn Credits (QC 4.6).
// Ký hiệu như validate-release: ✓ đạt · ✗ hỏng (Chặn) · ! cảnh báo · · thông tin · – không áp dụng / chưa kiểm bằng máy.
// Dòng ✓ quan trọng ngang ✗: không có nó thì không biết gate đã kiểm hay chưa.
import path from "node:path";
import { readText } from "./fsutil.mjs";
import { licenseInfo } from "./licenses.mjs";
import { makeMatcher } from "./ledger.mjs";
import { fontVerdict } from "./report.mjs";

// QC 10.2: không có nợ cho các mục này.
const NO_DEBT = new Set(["IP-1:uncovered", "IP-5:copyleft", "IP-15:logo", "IP-17:footage"]);

/** Nợ còn hạn khớp một mục (gate + đường dẫn hoặc id mục sổ). */
function debtFor(debts, gate, key, today) {
  return debts.find((d) => d.gate === gate && (d.path === key || d.entry === key) && (!d.until || d.until >= today)) || null;
}

/**
 * Chạy gate. Trả về [{ gate, title, mark, lines: [{ mark, text, key? }] }] và tổng số.
 * `debts`: mảng GameIPDebt.json; `noticesText`: nội dung file notice của project (null nếu chưa có).
 */
export function runGates(s, { debts = [], noticesText = null, today = new Date().toISOString().slice(0, 10) } = {}) {
  const ledger = s.ledger || { entries: [] };
  const match = makeMatcher(ledger.entries);
  const guidOf = (p) => {
    const f = s.index && s.index.byPath.get(p);
    return f ? f.guid : null;
  };
  const entryOf = (p) => match(p, guidOf(p));
  const inBuild = s.inBuildList.map((x) => x.path);
  const usedEntries = new Map(); // id → số asset vào build
  for (const p of inBuild) {
    const e = entryOf(p);
    if (e) usedEntries.set(e.id, (usedEntries.get(e.id) || 0) + 1);
  }
  const used = ledger.entries.filter((e) => usedEntries.has(e.id));
  const gates = [];
  const gate = (id, title) => {
    const g = { gate: id, title, lines: [] };
    gates.push(g);
    return g;
  };
  const line = (g, mark, text, debtKey = null) => {
    if (mark === "✗" && debtKey && NO_DEBT.has(`${g.gate}:${debtKey.kind}`)) text += " (không được ghi nợ, QC 10.2: phải sửa)";
    if ((mark === "✗" || mark === "!") && debtKey && !NO_DEBT.has(`${g.gate}:${debtKey.kind}`)) {
      const d = debtFor(debts, g.gate, debtKey.key, today);
      if (d) {
        g.lines.push({ mark: "·", text: `${text} (nợ tới ${d.until || "không hạn"}, ${d.approvedBy || "?"} duyệt: ${d.reason || ""})` });
        return;
      }
    }
    g.lines.push({ mark, text });
  };
  const ok = (g, text) => g.lines.push({ mark: "✓", text });

  // IP-1 nguồn gốc
  {
    const g = gate("IP-1", "Mọi asset vào build có nguồn trong sổ");
    if (!s.ledger) line(g, "✗", "Chưa có sổ GameIPLedger.json: chạy `init`.");
    else {
      const cv = s.coverage;
      for (const u of cv.uncoveredGroups) line(g, "✗", `${u.count} asset vào build chưa có nguồn: ${u.dir} (vd ${u.sample[0]})`, { kind: "uncovered", key: u.dir });
      const drafts = used.filter((e) => e.status !== "confirmed");
      for (const e of drafts) line(g, "!", `Mục \`${e.id}\` còn draft (${usedEntries.get(e.id)} asset vào build): người có trách nhiệm xác nhận (QC 3.5)`, { kind: "draft", key: e.id });
      if (!cv.uncovered && !drafts.length) ok(g, `${cv.total} asset vào build, mọi asset có mục sổ đã xác nhận`);
      else if (!cv.uncovered) ok(g, `${cv.total} asset vào build đều có mục sổ (${cv.draft} trong mục draft)`);
    }
  }

  // IP-2 pack, marketplace, license theo bảng (trừ font, code)
  {
    const g = gate("IP-2", "Pack mua, thư viện asset: license, bằng chứng mua");
    let n = 0;
    for (const e of used) {
      if (["font", "oss", "package", "unity", "middleware", "sdk", "inhouse"].includes(e.kind)) continue;
      n++;
      const L = licenseInfo(e.license);
      if (/editorial/i.test(`${e.license} ${e.notes} ${e.title}`)) line(g, "✗", `\`${e.id}\`: license Editorial, cấm dùng thương mại`, { kind: "license", key: e.id });
      else if (e.license === "unknown") line(g, "✗", `\`${e.id}\`: chưa rõ license`, { kind: "license", key: e.id });
      else if (L.gate === "block") line(g, "✗", `\`${e.id}\`: ${L.name} không cho dùng trong game có kiếm tiền`, { kind: "license", key: e.id });
      else if (L.gate === "warn") line(g, "!", `\`${e.id}\`: ${L.name}${L.copyleft === "share-alike" ? " (share-alike: [DUYỆT] quyết)" : ""}`, { kind: "license", key: e.id });
      if (["asset-store", "marketplace", "stock"].includes(e.kind) && !e.proof) line(g, "!", `\`${e.id}\`: chưa ghi bằng chứng mua (\`proof\`: tài khoản công ty, số đơn)`, { kind: "proof", key: e.id });
    }
    if (n && !g.lines.length) ok(g, `${n} nguồn mua / thư viện có license và bằng chứng`);
    if (!n) g.lines.push({ mark: "–", text: "Không có nguồn mua / thư viện asset nào vào build" });
    g.lines.push({ mark: "·", text: "Phân phối lại file gốc của pack (repo công khai, registry): chưa kiểm bằng máy, xem QC 4.2" });
  }

  // IP-3 font
  {
    const g = gate("IP-3", "Font được phép nhúng vào app");
    const fonts = s.fonts.filter((f) => f.inBuild);
    for (const f of fonts) {
      const e = entryOf(f.path);
      if (e && e.kind === "unity") continue; // font trong package Unity, theo Unity Companion License
      const [mark, why] = fontVerdict(f, e);
      if (mark === "✓") continue;
      if (e && e.status === "confirmed" && e.proof) line(g, "·", `${f.path}: ${why}; sổ ghi đã có license (\`${e.id}\`, ${e.proof})`);
      else line(g, mark, `${f.path} (${f.family || "?"}): ${why}`, { kind: "font", key: f.path });
    }
    if (fonts.length && !g.lines.length) ok(g, `${fonts.length} font vào build đều cho nhúng`);
    if (!fonts.length) g.lines.push({ mark: "–", text: "Không có file font vào build" });
  }

  // IP-4 âm thanh
  {
    const g = gate("IP-4", "Âm thanh, nhạc có nguồn, phạm vi, còn hạn");
    for (const a of s.audio.tagged.filter((x) => x.inBuild && x.sourceLike && !x.own)) {
      const e = entryOf(a.path);
      const tags = Object.entries(a.tags).map(([k, v]) => `${k}: ${v}`).join("; ").slice(0, 160);
      if (e && e.status === "confirmed") line(g, "·", `${a.path}: tag ghi nguồn khác (${tags}); sổ \`${e.id}\` đã xác nhận`);
      else line(g, "✗", `${a.path}: tag ghi nguồn khác (${tags}), sổ chưa xác nhận nguồn`, { kind: "tag", key: a.path });
    }
    for (const e of used) if (e.expires && e.expires < today) line(g, "✗", `\`${e.id}\`: license hết hạn ${e.expires}`, { kind: "expired", key: e.id });
    for (const e of used) if (e.expires && e.expires >= today && e.expires <= addDays(today, 90)) line(g, "!", `\`${e.id}\`: license hết hạn ${e.expires} (trong 90 ngày)`);
    if (!g.lines.length) ok(g, `${s.audio.inBuild} file âm thanh vào build, không tag nào ghi nguồn lạ, không license nào hết hạn`);
  }

  // IP-5 code, middleware
  {
    const g = gate("IP-5", "Code, middleware, SDK: license, copyleft, ngưỡng");
    const codeEntries = used.filter((e) => ["oss", "package", "middleware", "sdk", "publisher-sdk", "third-party", "asset-store"].includes(e.kind));
    for (const e of codeEntries) {
      const L = licenseInfo(e.license);
      if (L.copyleft === "strong") line(g, "✗", `\`${e.id}\`: ${L.name} (copyleft mạnh) trong app`, { kind: "copyleft", key: e.id });
      else if (L.copyleft === "weak" && e.kind !== "font" && !e.license.startsWith("OFL")) line(g, "!", `\`${e.id}\`: ${L.name}`, { kind: "weak", key: e.id });
      if (["oss", "package"].includes(e.kind) && e.license === "unknown") line(g, "✗", `\`${e.id}\`: chưa rõ license`, { kind: "license", key: e.id });
      if (e.limits) line(g, "!", `\`${e.id}\`: ngưỡng license: ${e.limits}. [REL] kiểm doanh thu / seat mỗi quý`, { kind: "limits", key: e.id });
    }
    for (const p of s.packages.filter((x) => x.license && licenseInfo(x.license).copyleft === "strong")) line(g, "✗", `package ${p.name}: ${licenseInfo(p.license).name}`, { kind: "copyleft", key: p.name });
    // Chưa có mục sổ cho folder: dựa vào file license khảo sát thấy.
    for (const v of s.vendorFolders.filter((x) => x.inBuild)) {
      const strong = (v.licenseFiles || []).find((l) => l.license && licenseInfo(l.license).copyleft === "strong");
      const covered = codeEntries.some((e) => (e.paths || []).includes(`${v.path}/**`));
      if (strong && !covered) line(g, "✗", `${v.path}: ${licenseInfo(strong.license).name} (${strong.file}), ${v.inBuild} file vào build`, { kind: "copyleft", key: v.path });
    }
    if (!g.lines.length) ok(g, `${codeEntries.length} nguồn code / middleware vào build, không copyleft mạnh, không ngưỡng cần theo dõi`);
  }

  // IP-6 ghi công
  {
    const g = gate("IP-6", "Nghĩa vụ ghi công có trong bản build");
    let need = used.filter((e) => e.attribution || (licenseInfo(e.license).attribution && e.kind !== "inhouse"));
    if (!s.ledger) {
      // Chưa có sổ: ước từ khảo sát (font, package, folder bên thứ ba vào build có license đòi ghi công; file yêu cầu ghi công).
      const att = (id) => id && licenseInfo(id).attribution;
      need = [
        ...s.fonts.filter((f) => f.inBuild && att(f.license)).map((f) => ({ id: f.path, title: f.family || f.path })),
        ...s.packages.filter((p) => p.inBuildFiles && att(p.license) && !p.name.startsWith("com.unity.")).map((p) => ({ id: p.name, title: p.displayName || p.name })),
        ...s.vendorFolders.filter((v) => v.inBuild && (v.licenseFiles || []).some((l) => att(l.license))).map((v) => ({ id: v.path, title: v.path })),
        ...s.creditDemands.map((d) => ({ id: d.path, title: d.path })),
      ];
    }
    if (!need.length) g.lines.push({ mark: "–", text: "Không nguồn nào vào build đòi ghi công" });
    else if (!s.credits.found && !noticesText) line(g, "✗", `${need.length} nguồn đòi ghi công nhưng game chưa có màn Credits / Licenses, chưa có file notice (chạy \`notices\`)`, { kind: "screen", key: "credits" });
    else if (noticesText !== null) {
      const low = noticesText.toLowerCase();
      for (const e of need) if (!low.includes((e.title || e.id).toLowerCase())) line(g, "✗", `\`${e.id}\` (${e.title}) chưa có trong file notice`, { kind: "missing", key: e.id });
      if (!g.lines.length) ok(g, `${need.length} nguồn đòi ghi công đều có trong file notice`);
    } else g.lines.push({ mark: "!", text: `Có dấu vết màn Credits nhưng chưa khai \`credits.noticesFile\` trong GameIPProject.json để máy đối chiếu ${need.length} nguồn` });
  }

  // IP-7 asset AI
  {
    const g = gate("IP-7", "Asset AI có hồ sơ");
    for (const a of s.images.ai.filter((x) => x.inBuild)) {
      const e = entryOf(a.path);
      if (!e || e.kind !== "ai") line(g, "!", `${a.path}: ${a.signal}; chưa có mục sổ kind ai`, { kind: "ai", key: a.path });
    }
    for (const e of used.filter((x) => x.kind === "ai")) {
      const ai = e.ai || {};
      const miss = ["tool", "plan", "date", "prompt"].filter((k) => !ai[k]);
      if (miss.length) line(g, "!", `\`${e.id}\`: thiếu ${miss.join(", ")} (QC 4.7)`, { kind: "aifields", key: e.id });
    }
    if (!g.lines.length) ok(g, "Không thấy asset AI vào build thiếu hồ sơ");
  }

  // IP-8 asset lấy từ web / game khác
  {
    const g = gate("IP-8", "Không asset lấy từ web, từ game khác");
    for (const x of s.suspicious.filter((y) => y.inBuild && !y.vendor)) line(g, "✗", `${x.path}: ${x.why}`, { kind: "name", key: x.path });
    for (const i of s.images.foreign.filter((y) => y.inBuild)) {
      const e = entryOf(i.path);
      const who = [i.creator, i.rights, i.credit, i.source].filter(Boolean).join("; ");
      if (e && e.status === "confirmed" && e.kind !== "inhouse") line(g, "·", `${i.path}: metadata ghi ${who}; sổ \`${e.id}\` đã xác nhận nguồn`);
      else if (e && e.kind !== "inhouse") line(g, "!", `${i.path}: metadata ghi ${who}; nằm trong mục \`${e.id}\` (draft), khi duyệt mục xác nhận đúng là của nguồn này`, { kind: "meta", key: i.path });
      else line(g, "✗", `${i.path}: metadata ghi ${who}${e && e.kind === "inhouse" ? " nhưng sổ coi là tự làm" : ""}`, { kind: "meta", key: i.path });
    }
    if (!g.lines.length) ok(g, "Không ảnh vào build nào có tên kiểu ảnh web / tách asset hay metadata của bên khác");
  }

  // IP-9 người làm đã chuyển quyền
  {
    const g = gate("IP-9", "Vẽ thuê, outsource có hợp đồng chuyển quyền");
    const com = used.filter((e) => e.kind === "commission");
    for (const e of com) if (!e.proof) line(g, "!", `\`${e.id}\`: chưa ghi hợp đồng (\`proof\`)`, { kind: "proof", key: e.id });
    if (!com.length) g.lines.push({ mark: "–", text: "Sổ chưa có mục commission vào build (hợp đồng nhân viên: checklist QC 4.9)" });
    else if (!g.lines.length) ok(g, `${com.length} mục commission có hợp đồng`);
  }

  // IP-13 tên game khác
  {
    const g = gate("IP-13", "Tên game khác trong project");
    for (const h of s.gameNames) {
      const own = h.segments.filter((x) => !x.vendor);
      if (own.length) line(g, "!", `"${h.name}" (${h.from}): ${own.map((x) => `${x.segment} (${x.count} file)`).slice(0, 5).join(", ")} → làm thẻ so sánh (QC 6)`, { kind: "name", key: h.name });
    }
    if (!g.lines.length) ok(g, "Không thấy tên game trong danh sách ở đường dẫn của project");
  }

  // IP-15 thương hiệu thật trong nội dung
  {
    const g = gate("IP-15", "Thương hiệu thật trong nội dung game");
    for (const b of (s.brands || []).filter((x) => x.hits.some((h) => h.inBuild))) {
      const h = b.hits.find((x) => x.inBuild);
      line(g, "!", `"${b.brand}" (${b.category}), ${b.count} lần, vd ${h.path}:${h.line} «${h.snippet}» → tên hư cấu, hoặc [DUYỆT] quyết (QC 4.15)`, { kind: "name", key: b.brand });
    }
    if (!g.lines.length) ok(g, "Không thấy tên thương hiệu trong danh sách ở nội dung vào build");
    g.lines.push({ mark: "·", text: "Logo thật, hình dạng đặc trưng (xe, súng, máy bay), người nổi tiếng ngoài danh sách: người xem" });
  }

  // IP-18 tài sản công ty khác
  {
    const g = gate("IP-18", "Không mang tài sản của công ty khác vào repo");
    for (const d of s.decompiled) line(g, "✗", `${d.path}: dấu vết code dịch ngược (${d.marker})${d.vendor ? ` trong ${d.vendor}` : ""}`, { kind: "code", key: d.path });
    for (const i of s.installers.filter((x) => !x.mayBeOwnBuild)) line(g, "✗", `${i.path}: file cài đặt game không phải của mình`, { kind: "installer", key: i.path });
    if (!g.lines.length) ok(g, "Không có code dịch ngược, không file cài đặt game lạ");
  }

  // IP-19 tài liệu mật trong build
  {
    const g = gate("IP-19", "Bản build không mang tài liệu thiết kế");
    for (const p of inBuild.filter((x) => /\.(md|docx?|xlsx?|pptx?|pdf|odt|ods)$/i.test(x) && x.startsWith("Assets/"))) line(g, "!", `${p}: tài liệu nằm trong build`, { kind: "doc", key: p });
    if (!g.lines.length) ok(g, "Không tài liệu văn phòng nào vào build");
  }

  // Gate có lệnh riêng hoặc người kiểm: check không chạy, chỉ nhắc
  for (const [id, title, how] of [
    ["IP-10", "Thẻ so sánh với game tham chiếu (QC 6)", "Lệnh `compare <phần> --ref <game>` sinh thẻ; người điền, [DUYỆT] xem thẻ Đỏ"],
    ["IP-11", "Ảnh so với ảnh game tham chiếu (QC 4.11)", "Lệnh `compare … --mine <ảnh của mình> --theirs <ảnh tham chiếu>`"],
    ["IP-12", "Tổ hợp chọn và sắp xếp (QC 6.3)", "Trong thẻ so sánh"],
    ["IP-14", "Tên game, icon, nhãn hiệu (QC 7)", "Lệnh `name \"<tên>\"`; icon: người xem"],
    ["IP-16", "Store listing (QC 4.16)", "Người kiểm; preview App Store: `creative` với `storePreview: true`"],
    ["IP-17", "Creative UA (QC 8)", "Lệnh `creative <folder>` cho từng creative"],
    ["IP-20", "Sẵn sàng ứng phó (QC 4.20)", "Checklist"],
  ]) {
    const g = gate(id, title);
    g.lines.push({ mark: "–", text: `Không chạy trong check. ${how}` });
  }

  const rank = { "✗": 3, "!": 2, "✓": 1, "·": 0, "–": 0 };
  for (const g of gates) g.mark = g.lines.reduce((m, l) => (rank[l.mark] > rank[m] ? l.mark : m), "–");
  const totals = { fail: 0, warn: 0 };
  for (const g of gates) for (const l of g.lines) (l.mark === "✗" ? totals.fail++ : l.mark === "!" ? totals.warn++ : 0);
  return { gates, totals };
}

function addDays(date, n) {
  const d = new Date(`${date}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + n);
  return d.toISOString().slice(0, 10);
}

export function checkReport(s, result, failsOnly = false) {
  const L = [`# Kiểm Game IP: ${s.project.productName || ""}`, "", `game-ip ${s.tool.version}, ${s.generatedAt}. ✗ ${result.totals.fail} · ! ${result.totals.warn}. Không phải tư vấn pháp lý; [DUYỆT] quyết mọi ngoại lệ (QC 10.2).`, ""];
  for (const g of result.gates) {
    const lines = failsOnly ? g.lines.filter((l) => l.mark === "✗" || l.mark === "!") : g.lines;
    if (failsOnly && !lines.length) continue;
    L.push(`## ${g.mark} ${g.gate} ${g.title}`);
    for (const l of lines.slice(0, 200)) L.push(`- ${l.mark} ${l.text}`);
    if (lines.length > 200) L.push(`- … và ${lines.length - 200} dòng nữa`);
    L.push("");
  }
  return L.join("\n");
}

/** Nội dung màn Credits / Licenses từ sổ: mọi mục vào build đòi ghi công, cộng nội dung file license (MIT, BSD, Apache, OFL). */
export function buildNotices(s) {
  const ledger = s.ledger || { entries: [] };
  const match = makeMatcher(ledger.entries);
  const used = new Set();
  for (const { path: p } of s.inBuildList) {
    const f = s.index && s.index.byPath.get(p);
    const e = match(p, f ? f.guid : null);
    if (e) used.add(e);
  }
  const need = [...used].filter((e) => e.attribution || (licenseInfo(e.license).attribution && e.kind !== "inhouse")).sort((a, b) => (a.title || a.id).localeCompare(b.title || b.id));
  const out = [`${s.project.productName || "This game"} uses the following third-party software and content.`, ""];
  for (const e of need) {
    const L = licenseInfo(e.license);
    out.push("------------------------------------------------------------");
    out.push(e.title || e.id);
    if (e.author) out.push(`by ${e.author}`);
    if (e.attribution) out.push(e.attribution);
    out.push(`License: ${L.name}${e.source ? ` (${e.source})` : ""}`);
    if (e.licenseFile && /^(MIT|BSD|Apache|ISC|OFL|Zlib)/.test(e.license)) {
      const abs = s.index && s.index.byPath.get(e.licenseFile);
      const txt = readText(abs ? abs.abs : path.join(s.project.root, e.licenseFile));
      if (txt) out.push("", txt.trim());
    }
    out.push("");
  }
  return { text: `${out.join("\n")}\n`, count: need.length };
}
