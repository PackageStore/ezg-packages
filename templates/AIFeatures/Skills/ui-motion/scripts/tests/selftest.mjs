// Tự kiểm script khảo sát trên project giả (make_fixture.mjs): khảo sát phải tìm ra đúng ứng viên đã cài sẵn trong fixture,
// init ra file luật hợp lệ, không ghi đè file đã có. Chạy: node uimotion.mjs selftest [--keep]
import fs from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

export function runSelftest(ctx) {
  const { dir, makeFixture, moduleSource, survey, rulesFromSurvey, projectNotes, surveyReport, validateRules, rolesFromSource } = ctx;
  const results = [];
  const check = (name, cond, detail = "") => results.push({ name, ok: !!cond, detail });

  // 1. Project chưa có module
  const bare = makeFixture(path.join(dir, "bare"), null);
  const s0 = survey({ root: bare });
  check("không module → module = null", s0.module === null);
  check("đếm prefab UI của project (bỏ ThirdParty và pack có Readme)", s0.counts.ownUiPrefabs === 6 && s0.counts.uiPrefabs === 8, `${s0.counts.ownUiPrefabs}/${s0.counts.uiPrefabs}`);
  check("nhận pack mua về qua Readme", (s0.folders.packRoots || []).includes("Assets/VendorKit/UIKit"), JSON.stringify(s0.folders.packRoots));
  check("nhận DOTween từ DLL + Modules", s0.packages.dotween === true && s0.packages.dotweenModules === true && !s0.traps.some((t) => /dotween/.test(t.kind)));
  const own = Object.fromEntries(s0.candidates.ownMotion.map((c) => [c.shortName, c]));
  check("PopupBase: tự có motion, role Popup, độ tin cao", own.PopupBase && own.PopupBase.role === "Popup" && own.PopupBase.confidence === "cao", JSON.stringify(own.PopupBase && { role: own.PopupBase.role, c: own.PopupBase.confidence, ev: own.PopupBase.evidence }));
  check("ShopPopup: lớp cha PopupBase phủ", own.ShopPopup && own.ShopPopup.coveredByParent === "PopupBase", own.ShopPopup && own.ShopPopup.coveredByParent);
  check("ClickScale: tự có motion, role Button", own.ClickScale && own.ClickScale.role === "Button", JSON.stringify(own.ClickScale && { role: own.ClickScale.role, ev: own.ClickScale.evidence }));
  check("ListStagger: tự có motion, role List", own.ListStagger && own.ListStagger.role === "List");
  const click = s0.candidates.clickable.map((c) => c.shortName);
  check("TapArea: bấm được, chưa có motion", click.includes("TapArea"), click.join(","));
  check("NewBadge: badge riêng", s0.candidates.badge.some((c) => c.shortName === "NewBadge"));
  check("audio manager AudioHub", s0.audio.managers[0] && s0.audio.managers[0].name === "AudioHub" && s0.audio.managers[0].calls >= 2, JSON.stringify(s0.audio.managers[0]));
  check("enum SfxId", s0.audio.enums.some((e) => e.name === "SfxId" && e.members.includes("Tap")));
  check("tiếng kép: ClickScale tự phát tiếng", s0.audio.doubleSoundRisk.some((d) => /ClickScale/.test(d.file)));
  check("UI manager có stack", s0.screens.managers.some((m) => /UIManager/.test(m.file)), JSON.stringify(s0.screens.managers));
  check("đếm SetActive(false) (UIManager ×2, PopupBase ×1)", s0.screens.setActiveFalseTotal === 3, String(s0.screens.setActiveFalseTotal));
  check("bẫy FPS", s0.traps.some((t) => t.kind === "fps-cap"));
  check("prefab item *View được spawn → gợi ý từ view", s0.lists.itemPrefabWords.suggestAdd.some((x) => x.word === "view"), JSON.stringify(s0.lists.itemPrefabWords));
  check("prefab khuôn lồng 2 lần → gợi ý template (đã có)", !s0.lists.templatePrefabWords.suggestAdd.some((x) => x.word === "template"));
  check("viết tắt pnl → Panel", s0.naming.abbreviationSuggestions.some((a) => a.word === "pnl" && a.role === "Panel"), JSON.stringify(s0.naming.abbreviationSuggestions));
  check("folder quét đề xuất Assets/Game", JSON.stringify(s0.folders.scanFoldersSuggestion) === JSON.stringify(["Assets/Game"]), JSON.stringify(s0.folders.scanFoldersSuggestion));

  const rules = rulesFromSurvey(s0);
  const types = rules.componentRules.map((r) => r.type);
  check("init: luật chỉ có ứng viên độ tin cao", types.includes("Fixture.UI.PopupBase") && !types.includes("Fixture.UI.ShopPopup"), types.join(","));
  const v = validateRules(JSON.stringify(rules));
  check("init: file luật hợp lệ", v.errors.length === 0, v.errors.join("; "));
  check("init: dataFolder mặc định khi chưa có asset", rules.dataFolder === "Assets/Resources/UIMotion");
  const notes = projectNotes(s0, rules, "2000-01-01");
  check("ghi chú có mục Cần xác nhận", /## Cần xác nhận/.test(notes));
  const report = surveyReport(s0);
  check("báo cáo có mục class tự có motion", /Class tự có motion/.test(report) && /PopupBase/.test(report));

  // 1b. RoleMap cũ: dòng class có trong project được chép vào file luật, dòng class lạ bị bỏ
  const s0b = JSON.parse(JSON.stringify(s0));
  s0b.data.roleMap = { projectRows: [{ type: "ClickScale", role: "Button", ownMotion: true }], staleRows: [{ type: "SomeOtherGameController", role: "Screen", ownMotion: true }] };
  const rulesB = rulesFromSurvey(s0b);
  check("init: dòng RoleMap của project vào file luật, không lặp với khảo sát", rulesB.componentRules.filter((r) => /ClickScale$/.test(r.type)).length === 1 && rulesB.componentRules[0].type === "ClickScale", JSON.stringify(rulesB.componentRules.map((r) => r.type)));
  check("init: dòng RoleMap mang class lạ vào componentRulesRemove", JSON.stringify(rulesB.componentRulesRemove) === JSON.stringify(["SomeOtherGameController"]));

  // 2. Kiểm file luật: lỗi được báo
  const bad = validateRules(JSON.stringify({ componentRules: [{ type: "X", role: "Scren" }], keywords: { add: [{ word: "a b" }] }, settings: { scanFolders: "Assets" }, extra: 1 }));
  check("check-rules bắt role sai, thiếu role, kiểu sai", bad.errors.length === 3, bad.errors.join(" | "));
  check("check-rules cảnh báo khoá lạ", bad.warnings.some((w) => /khoá lạ "extra"/.test(w)));

  // 3. Project có module (nếu biết nguồn module)
  if (moduleSource) {
    const s0m = survey({ root: bare, moduleSource });
    check("chưa có module: bảng keyword lấy từ module nguồn", /module nguồn/.test(s0m.naming.keywordSource || "") && Object.keys(s0m.naming.objectsByKeywordRole).length > 0, s0m.naming.keywordSource);
    fs.rmSync(path.join(bare, "Assets/Plugins/Demigiant/DOTween/Modules"), { recursive: true, force: true });
    check("thiếu DOTween Modules → bẫy dotween-modules", survey({ root: bare }).traps.some((t) => t.kind === "dotween-modules"));
    const withModule = makeFixture(path.join(dir, "module"), moduleSource, "Assets/Game/Modules/UIMotion");
    const s1 = survey({ root: withModule });
    check("nhận module + bản", s1.module && s1.module.path === "Assets/Game/Modules/UIMotion" && !!s1.module.version, JSON.stringify(s1.module));
    check("nhận quy chuẩn", s1.module && !!s1.module.rulesDoc);
    const roles = rolesFromSource(fs.readFileSync(path.join(withModule, "Assets/Game/Modules/UIMotion/Roles/UIMotionRoleTypes.cs"), "utf8"));
    check("đọc enum UIRole từ module", roles && roles.includes("Screen") && roles.includes("InputField"));
    check("bảng keyword mặc định đọc từ module", s1.naming.keywordSource === "bảng mặc định của module" && Object.keys(s1.naming.objectsByKeywordRole).length > 0, JSON.stringify(s1.naming.objectsByKeywordRole));
    check("module không bị tính là code của project", !s1.candidates.ownMotion.some((c) => /^UIMotion/.test(c.shortName)));

    // 4. install-module trên project có module "bản cũ" (asset của project nằm trong folder module)
    const old = path.join(withModule, "Assets/Game/Modules/UIMotion");
    fs.mkdirSync(path.join(old, "Resources/UIMotion"), { recursive: true });
    fs.writeFileSync(path.join(old, "Resources.meta"), "fileFormatVersion: 2\nguid: c0000000000000000000000000000001\nfolderAsset: yes\n");
    fs.writeFileSync(path.join(old, "Resources/UIMotion.meta"), "fileFormatVersion: 2\nguid: c0000000000000000000000000000002\nfolderAsset: yes\n");
    fs.writeFileSync(path.join(old, "Resources/UIMotion/UIMotionRoleMap.asset"), "%YAML 1.1\n--- !u!114 &11400000\nMonoBehaviour:\n  _componentRules:\n  - TypeName: ClickScale\n    Role: 6\n    HasOwnMotion: 1\n");
    fs.writeFileSync(path.join(old, "Resources/UIMotion/UIMotionRoleMap.asset.meta"), "fileFormatVersion: 2\nguid: c0000000000000000000000000000003\n");
    fs.writeFileSync(path.join(old, "OldOnly.cs"), "// file chỉ có ở bản cũ\n");
    const cli = path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "uimotion.mjs");
    const run = spawnSync(process.execPath, [cli, "install-module", "--project", withModule, "--from", moduleSource], { encoding: "utf8" });
    const data = path.join(withModule, "Assets/Resources/UIMotion");
    check("install-module chạy xong", run.status === 0, (run.stderr || run.stdout || "").slice(0, 300));
    check("install-module dời asset cũ ra folder dữ liệu (kèm .meta)", fs.existsSync(path.join(data, "UIMotionRoleMap.asset")) && fs.readFileSync(path.join(data, "UIMotionRoleMap.asset.meta"), "utf8").includes("c0000000000000000000000000000003"));
    check("install-module mang .meta của folder cũ (giữ GUID folder)", fs.existsSync(data + ".meta") && fs.readFileSync(data + ".meta", "utf8").includes("c0000000000000000000000000000002"));
    check("install-module thay nguyên folder module", !fs.existsSync(path.join(old, "OldOnly.cs")) && !fs.existsSync(path.join(old, "Resources")) && fs.existsSync(path.join(old, "UIMotionDefaults.cs")));
  } else {
    results.push({ name: "(bỏ qua phần có module: không biết nguồn module)", ok: true, detail: "" });
  }

  let failed = 0;
  for (const r of results) {
    if (!r.ok) failed++;
    console.log(`${r.ok ? "PASS" : "FAIL"} ${r.name}${!r.ok && r.detail ? " — " + r.detail : ""}`);
  }
  console.log(failed ? `selftest: ${failed} / ${results.length} FAIL` : `selftest: ${results.length} PASS`);
  return failed === 0;
}
