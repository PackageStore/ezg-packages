// Viết kết quả khảo sát ra: báo cáo Markdown (Logs/UIMotionSurvey.md), file luật riêng mới (ProjectSettings/UIMotionProject.json)
// và ghi chú của project (ProjectSettings/UIMotionProject.md). Chỉ ứng viên độ tin "cao" vào file luật; còn lại vào mục
// "Cần xác nhận" của ghi chú để người quyết.

const esc = (s) => String(s ?? "").replace(/\|/g, "\\|").replace(/\r?\n/g, " ");
const code = (s) => "`" + String(s ?? "").replace(/`/g, "'") + "`";
const yesNo = (b) => (b ? "có" : "không");

function table(headers, rows) {
  if (!rows.length) return "_(không có)_\n";
  let s = "| " + headers.join(" | ") + " |\n|" + headers.map(() => "---").join("|") + "|\n";
  for (const r of rows) s += "| " + r.map(esc).join(" | ") + " |\n";
  return s;
}

function candidateRows(list, n = 30) {
  return list.slice(0, n).map((c) => [
    code(c.type),
    c.role,
    c.confidence || "",
    `${c.uses} chỗ / ${c.prefabs} prefab`,
    [...(c.motion || []), ...(c.evidence || [])].join("; "),
    c.coveredByParent ? `lớp cha ${c.coveredByParent} đã phủ` : c.inRulesFile ? "đã có trong file luật" : c.inRoleMap ? "đã có trong RoleMap" : "",
  ]);
}

/** Báo cáo khảo sát đầy đủ (tái tạo mỗi lần chạy, không phải file để sửa tay). */
export function surveyReport(s) {
  const L = [];
  L.push(`# Khảo sát UI Motion — ${s.project.name}`, "");
  L.push(`${s.generatedAt} · Unity ${s.project.unity || "?"} · gốc ${code(s.project.root)}`, "");
  L.push("Báo cáo sinh bằng script (`uimotion.mjs survey`), chạy lại là ghi đè. Điều đã xác nhận ghi vào `ProjectSettings/UIMotionProject.md` / `.json`.", "");

  L.push("## Module và dữ liệu", "");
  if (s.module) {
    L.push(table(["Mục", "Giá trị"], [
      ["Module", `${code(s.module.path)} · bản ${s.module.version}`],
      ["Quy chuẩn", s.module.rulesDoc ? `${code(s.module.rulesDoc)} · phiên bản ${s.module.rulesDocVersion || "?"}` : "không thấy"],
      ["Stub Odin trong module", yesNo(s.module.hasOdinStubs)],
      ["Asset của project", s.data.assets.length ? s.data.assets.map(code).join(", ") : "chưa có (Tools/UI Motion/Tạo asset mẫu)"],
      ["Asset nằm trong module", s.data.insideModule.length ? `${s.data.insideModule.length} — cần dời ra` : "không"],
      ["RoleMap theo bảng mặc định bản", s.data.roleMap?.defaultsVersion || "—"],
      ["Luật riêng đã áp vào RoleMap", s.data.roleMap ? yesNo(!!s.data.roleMap.projectRulesHash) : "—"],
    ]));
  } else {
    L.push("Project **chưa có** module UI Motion (không thấy `UIMotionDefaults.cs`). Xem `reference/retrofit.md` của skill.", "");
  }
  L.push(table(["File riêng của project", "Trạng thái"], [
    ["ProjectSettings/UIMotionProject.json (luật)", s.projectFiles.rules ? (s.projectFiles.rulesError ? "lỗi JSON: " + s.projectFiles.rulesError : "có") : "chưa có"],
    ["ProjectSettings/UIMotionProject.md (ghi chú)", s.projectFiles.notes ? "có" : "chưa có"],
    ["ProjectSettings/UIMotionDebt.json (nợ gate)", s.projectFiles.debt ? `${s.projectFiles.debt.entries} mục` : "chưa có"],
  ]));
  if (s.projectFiles.newCandidates?.length) L.push("", `Ứng viên mới chưa có trong file luật: ${s.projectFiles.newCandidates.map(code).join(", ")}.`);
  if (s.projectFiles.missingClasses?.length) L.push("", `File luật nhắc class không còn trong code: ${s.projectFiles.missingClasses.map(code).join(", ")}.`);
  L.push("");

  L.push("## Package", "");
  const p = s.packages;
  L.push(table(["Package", "Có"], [
    ["DOTween (bắt buộc)", yesNo(p.dotween)], ["DOTween Modules (bắt buộc)", yesNo(p.dotweenModules)], ["DOTween Pro", yesNo(p.dotweenPro)], ["Odin", yesNo(p.odin)], ["TextMeshPro", yesNo(p.tmp)],
    ["UniTask", yesNo(p.unitask)], ["UIEffect", yesNo(p.uiEffect)], ["UI Extensions", yesNo(p.uiExtensions)], ["Input System", yesNo(p.inputSystem)],
    ["List pool", p.listPools.length ? p.listPools.join(", ") : "không thấy"],
  ]));
  L.push("", `Script: ${s.counts.scripts} · prefab: ${s.counts.prefabs} · prefab UI: ${s.counts.uiPrefabs} (của project: ${s.counts.ownUiPrefabs}) · input: ${s.project.inputHandler || "?"}`, "");

  L.push("## Folder UI", "");
  L.push(table(["Folder", "Prefab / scene UI"], s.folders.uiPrefabsByFolder.map((f) => [code(f.folder), f.uiPrefabs])));
  if (s.folders.packRoots?.length) L.push("", `Pack mua về (có Documentation / Readme, bỏ qua khi khảo sát): ${s.folders.packRoots.map(code).join(", ")}.`);
  L.push("", `Folder quét đề xuất: ${s.folders.scanFoldersSuggestion.map(code).join(", ")}.` + (s.folders.excludeSuggestion.length ? ` Folder trông như pack (đề xuất loại trừ): ${s.folders.excludeSuggestion.map((x) => code(x.contains) + ` (${x.uiPrefabs})`).join(", ")}.` : ""), "");

  L.push("## Class tự có motion (ứng viên luật Component \"Đã có motion\")", "");
  L.push("Tool không gắn motion của module chồng lên object có class này. Sai thì screen / nút mất motion của module, nên chỉ ghi luật khi đã chắc.", "");
  L.push(table(["Class", "Role", "Độ tin", "Dùng", "Bằng chứng", "Ghi chú"], candidateRows(s.candidates.ownMotion)));
  const tweens = s.candidates.ownMotion.filter((c) => c.tweenLines?.length && !c.coveredByParent).slice(0, 8);
  if (tweens.length) {
    L.push("", "Số đang dùng (để chỉnh Profile cho screen mới giống cảm giác cũ):", "");
    for (const c of tweens) {
      L.push(`- ${code(c.shortName)} (${c.file})`);
      for (const t of c.tweenLines.slice(0, 5)) L.push(`  - dòng ${t.line}: ${code(t.text)}`);
    }
  }
  L.push("");
  L.push("## Class bấm được chưa có motion (ứng viên luật role Button)", "");
  L.push("Không khai thì gate báo M-10 (bấm được mà chưa rõ role).", "");
  L.push(table(["Class", "Role", "Độ tin", "Dùng", "Bằng chứng", "Ghi chú"], candidateRows(s.candidates.clickable)));
  L.push("", "## Chấm đỏ / badge riêng", "");
  L.push(table(["Class", "Role", "Độ tin", "Dùng", "Bằng chứng", "Ghi chú"], candidateRows(s.candidates.badge)));

  L.push("", "## Âm thanh", "");
  L.push(table(["Class", "Hàm phát", "Singleton", "Chỗ gọi", "Kiểu tham số"], s.audio.managers.map((a) => [code(a.type), a.methods.join(", "), yesNo(a.singleton), a.calls, a.argTypes.join(", ")])));
  for (const a of s.audio.managers.slice(0, 2)) for (const e of a.examples) L.push(`- ${code(e)}`);
  for (const e of s.audio.enums.slice(0, 3)) L.push("", `${e.kind === "const string" ? "Hằng chuỗi" : "Enum"} ${code(e.name)} (${e.file}): ${e.members.join(", ")}`);
  if (s.audio.doubleSoundRisk.length) {
    L.push("", "Code bấm được đang tự phát tiếng (coi chừng tiếng kép khi gán clip cho role trong RoleMap):", "");
    for (const d of s.audio.doubleSoundRisk.slice(0, 10)) L.push(`- ${code(d.file)} (${d.classes.join(", ")})`);
  }

  L.push("", "## Mở / đóng screen", "");
  L.push(`\`SetActive(false)\` trong code project: ${s.screens.setActiveFalseTotal} chỗ. Screen có UIMotionPanel mà bị tắt thẳng thì mất motion ẩn (M-11): đổi chỗ gọi sang \`UIMotion.SetActiveAnimated\`.`, "");
  L.push(table(["UI manager (ứng viên)", "Hàm", "SetActive(false)", "Destroy", "Instantiate"], s.screens.managers.map((m) => [code(m.file), m.methods.join(", "), m.setActiveFalse, m.destroy, m.instantiate])));
  L.push("", table(["Class có Show / Hide / Close", "Hàm", "SetActive(false)", "Destroy"], s.screens.showHideClasses.map((m) => [code(m.file), m.methods.join(", "), m.setActiveFalse, m.destroy])));
  if (s.screens.usesUIMotion.length) L.push("", `Code đã gọi UI Motion: ${s.screens.usesUIMotion.map(code).join(", ")}.`);

  L.push("", "## List và prefab item", "");
  L.push(`Từ nhận prefab item đang dùng: ${s.lists.itemPrefabWords.current.join(", ")}.` + (s.lists.itemPrefabWords.suggestAdd.length ? ` Đề xuất thêm: ${s.lists.itemPrefabWords.suggestAdd.map((x) => `${x.word} (${x.prefabs} prefab được spawn)`).join(", ")}.` : ""));
  L.push(`Từ nhận prefab khuôn đang dùng: ${s.lists.templatePrefabWords.current.join(", ")}.` + (s.lists.templatePrefabWords.suggestAdd.length ? ` Đề xuất thêm: ${s.lists.templatePrefabWords.suggestAdd.map((x) => `${x.word} (${x.prefabs})`).join(", ")}.` : ""));
  if (s.lists.spawnedPrefabTokens.length) L.push(`Từ trong tên prefab được code / prefab khác tham chiếu (hay là item, popup sinh lúc chạy): ${s.lists.spawnedPrefabTokens.map((x) => `${x.token} ×${x.prefabs}`).join(", ")}.`);

  L.push("", "## Đặt tên", "");
  L.push(`Bảng keyword đang so: ${s.naming.keywordSource || "không có (chưa có module)"}.`, "");
  L.push(`Object ra role nhờ keyword: ${Object.entries(s.naming.objectsByKeywordRole).map(([r, n]) => `${r} ×${n}`).join(", ") || "—"}.`, "");
  L.push(`Từ hay gặp chưa khớp keyword nào: ${s.naming.topUnmatchedTokens.map((t) => `${t.token} ×${t.count}`).join(", ")}.`, "");
  if (s.naming.abbreviationSuggestions.length) L.push(`Viết tắt có vẻ là role: ${s.naming.abbreviationSuggestions.map((a) => `${a.word} → ${a.role} (${a.objects} object)`).join(", ")}.`, "");

  L.push("## Bẫy môi trường", "");
  if (!s.traps.length) L.push("_(không thấy)_");
  for (const t of s.traps) L.push(`- **${t.kind}**: ${t.detail}`);
  for (const n of s.notes) L.push(`- ${n}`);
  L.push("");
  return L.join("\n");
}

/** File luật riêng mới từ khảo sát: chỉ ứng viên độ tin cao, lớp cha phủ lớp con. */
export function rulesFromSurvey(s) {
  const pick = (list) => list.filter((c) => c.confidence === "cao" && !c.coveredByParent && !c.inRoleMap);
  // Dòng class của project đã có trong RoleMap (tuning đang chạy) chép vào file để khôi phục được; thắng đoán của khảo sát.
  const fromRoleMap = (s.data.roleMap?.projectRows || []).map((r) => ({ type: r.type, role: r.role, ownMotion: r.ownMotion, note: r.note || "có sẵn trong RoleMap của project" }));
  const known = new Set(fromRoleMap.map((r) => r.type.split(".").pop()));
  const componentRules = [
    ...fromRoleMap,
    ...pick(s.candidates.ownMotion).filter((c) => !known.has(c.shortName)).map((c) => ({ type: c.type, role: c.role, ownMotion: true, note: `khảo sát: ${[...c.motion, ...c.evidence].join("; ")}` })),
    ...pick(s.candidates.clickable).filter((c) => !known.has(c.shortName)).map((c) => ({ type: c.type, role: "Button", ownMotion: false, note: "khảo sát: bấm được, chưa có motion" })),
  ];
  const rules = { schema: 1, project: s.project.name };
  const stale = (s.data.roleMap?.staleRows || []).map((r) => r.type);
  const outside = s.data.assets.filter((a) => !s.data.insideModule.includes(a));
  if (!outside.length) rules.dataFolder = "Assets/Resources/UIMotion";
  rules.componentRules = componentRules;
  if (stale.length) rules.componentRulesRemove = stale;
  rules.keywords = { add: [], remove: [] };
  const settings = {};
  if (s.folders.scanFoldersSuggestion.length && !(s.folders.scanFoldersSuggestion.length === 1 && s.folders.scanFoldersSuggestion[0] === "Assets")) settings.scanFolders = s.folders.scanFoldersSuggestion;
  if (s.folders.excludeSuggestion.length) {
    settings.excludePathContains = ["/3rdParty/", "/ThirdParty/", "/Plugins/", "/Samples/", "/Sample/", "/Demo/", "/Demos/", "/Examples/", "/Models/", ...s.folders.excludeSuggestion.map((x) => x.contains)];
  }
  if (Object.keys(settings).length) rules.settings = settings;
  return rules;
}

/** Ghi chú của project: điều thật của project này, điều cần xác nhận, quyết định. Tạo một lần; sau đó sửa tay / Claude sửa. */
export function projectNotes(s, rules, today) {
  const L = [];
  const pendingOwn = s.candidates.ownMotion.filter((c) => c.confidence !== "cao" && !c.coveredByParent && !c.inRoleMap);
  const pendingClick = s.candidates.clickable.filter((c) => c.confidence !== "cao" && !c.coveredByParent && !c.inRoleMap);
  L.push(`# UI Motion — ghi chú riêng của project ${s.project.name}`, "");
  L.push(`Tạo ${today} bằng \`uimotion.mjs init\` (skill ui-motion) từ khảo sát. File của RIÊNG project này: skill và module không chứa những điều dưới đây. Sửa tay thoải mái; script không ghi đè file này.`, "");
  L.push("Luật máy đọc được nằm ở `ProjectSettings/UIMotionProject.json` (áp vào RoleMap bằng Tools/UI Motion/Áp luật riêng của project). Nợ gate: `ProjectSettings/UIMotionDebt.json`.", "");
  L.push("## Project", "");
  L.push(table(["Mục", "Giá trị"], [
    ["Unity", s.project.unity || "?"],
    ["Module UI Motion", s.module ? `${code(s.module.path)} · ${s.module.version}` : "chưa cài"],
    ["Folder dữ liệu (RoleMap, Settings, Profile)", s.data.folder ? code(s.data.folder) + (s.data.insideModule.length ? " — đang trong module, cần dời" : "") : code(rules.dataFolder || "Assets/Resources/UIMotion") + " (sẽ tạo)"],
    ["Folder quét", (rules.settings?.scanFolders || s.data.settings?.scanFolders || ["Assets"]).map(code).join(", ")],
    ["Package", ["DOTween", s.packages.dotweenPro && "DOTween Pro", s.packages.odin && "Odin", s.packages.tmp && "TMP", s.packages.unitask && "UniTask", s.packages.uiEffect && "UIEffect", s.packages.uiExtensions && "UI Extensions"].filter(Boolean).join(", ")],
    ["List pool", s.packages.listPools.join(", ") || "không thấy"],
    ["Input", s.project.inputHandler || "?"],
  ]));
  L.push("", "## Class của project tự có motion", "");
  L.push("Đã ghi vào file luật (dòng có sẵn trong RoleMap của project, và ứng viên độ tin cao):", "");
  L.push(table(["Class", "Role", "Đã có motion", "Vì sao"], (rules.componentRules || []).map((r) => [code(r.type), r.role, yesNo(r.ownMotion), r.note.replace(/^khảo sát: /, "")])));
  if (s.data.roleMap?.staleRows?.length) {
    L.push("", "RoleMap có dòng mang class không có trong project này (bảng mặc định của bản module cũ để lại): " + s.data.roleMap.staleRows.map((r) => code(r.type)).join(", ") + " — file luật bỏ chúng (khoá componentRulesRemove).");
  }
  L.push("", "## Cần xác nhận", "");
  L.push("Hỏi người giữ project từng dòng; đúng thì thêm vào `componentRules` của file luật rồi áp lại, sai thì ghi lý do ở mục Quyết định.", "");
  L.push(table(["Class", "Role đoán", "Độ tin", "Dùng", "Bằng chứng"], [...pendingOwn, ...pendingClick].slice(0, 25).map((c) => [code(c.type), c.role, c.confidence, `${c.uses} chỗ / ${c.prefabs} prefab`, [...c.motion, ...c.evidence].join("; ")])));
  if (s.candidates.badge.length) L.push("", `Badge riêng (role Badge, module gắn UIMotionBadge): ${s.candidates.badge.slice(0, 6).map((c) => code(c.type)).join(", ")}.`);
  if (s.lists.itemPrefabWords.suggestAdd.length) L.push("", `Từ nhận prefab item có thể thêm: ${s.lists.itemPrefabWords.suggestAdd.map((x) => code(x.word)).join(", ")} (\`prefabWords.item\`).`);
  if (s.naming.abbreviationSuggestions.length) L.push("", `Keyword viết tắt có thể thêm: ${s.naming.abbreviationSuggestions.map((a) => `${code(a.word)} → ${a.role}`).join(", ")} (\`keywords.add\`).`);
  L.push("", "## Âm thanh", "");
  if (s.audio.managers.length) {
    const a = s.audio.managers[0];
    L.push(`Audio manager (ứng viên): ${code(a.type)} — ${a.methods.join(", ")}; ${a.calls} chỗ gọi${a.argTypes.length ? `; tham số kiểu ${a.argTypes.join(", ")}` : ""}.`);
    if (a.examples.length) L.push("", "Ví dụ gọi:", "", ...a.examples.slice(0, 3).map((e) => `- ${code(e)}`));
    L.push("", "Sfx UI đi qua audio manager này: viết sink `IUIMotionSfxSink` trong code của project (mẫu `Docs/Samples/UIMotionSfxSink.cs.txt` của module), chọn trong Settings. Bảng map sự kiện → tiếng:", "");
    L.push(table(["Sự kiện UI Motion", "Tiếng của project"], [["ui.button.click", "?"], ["ui.popup.show / ui.screen.show", "?"], ["ui.popup.hide / ui.screen.hide", "?"], ["ui.counter.arrive", "?"]]));
  } else {
    L.push("Không thấy audio manager: dùng player kèm sẵn của module (kéo AudioClip vào RoleMap).");
  }
  if (s.audio.doubleSoundRisk.length) L.push("", `Code bấm được đã tự phát tiếng: ${s.audio.doubleSoundRisk.slice(0, 6).map((d) => code(d.file)).join(", ")} — đừng gán clip click trùng trong RoleMap cho các nút này.`);
  L.push("", "## Mở / đóng screen", "");
  if (s.screens.managers.length) L.push(`UI manager (ứng viên): ${s.screens.managers.slice(0, 3).map((m) => `${code(m.file)} (${m.methods.join(", ")})`).join("; ")} — chỗ đổi sang \`UIMotion.SetActiveAnimated\`.`);
  L.push(`\`SetActive(false)\` trong code project: ${s.screens.setActiveFalseTotal} chỗ (nhiều nhất: ${s.screens.setActiveFalseFiles.slice(0, 4).map((f) => `${code(f.file)} ×${f.count}`).join(", ") || "—"}).`);
  L.push("", "## Bẫy môi trường", "");
  if (!s.traps.length) L.push("_(chưa thấy)_");
  for (const t of s.traps) L.push(`- ${t.detail}`);
  L.push("", "## Quyết định", "");
  L.push("| Ngày | Quyết định | Lý do |", "|---|---|---|", `| ${today} | Tạo file từ khảo sát | — |`, "");
  return L.join("\n");
}

/** In tóm tắt ngắn ra console. */
export function summary(s) {
  const L = [];
  L.push(`Project ${s.project.name} · Unity ${s.project.unity || "?"} · ${s.counts.uiPrefabs} prefab UI (${s.counts.ownUiPrefabs} của project)`);
  L.push(s.module ? `Module UI Motion ${s.module.version} ở ${s.module.path}; quy chuẩn ${s.module.rulesDocVersion || "?"} (${s.module.rulesDoc || "không thấy"})` : "Chưa có module UI Motion.");
  L.push(`Asset: ${s.data.assets.length ? s.data.folder : "chưa có"}${s.data.insideModule.length ? ` — ${s.data.insideModule.length} asset nằm trong module (cần dời)` : ""}`);
  L.push(`File riêng: luật ${s.projectFiles.rules ? "có" : "chưa"}, ghi chú ${s.projectFiles.notes ? "có" : "chưa"}, nợ ${s.projectFiles.debt ? s.projectFiles.debt.entries : 0}`);
  const high = s.candidates.ownMotion.filter((c) => c.confidence === "cao" && !c.coveredByParent).length;
  L.push(`Ứng viên: ${s.candidates.ownMotion.length} class tự có motion (${high} độ tin cao), ${s.candidates.clickable.length} class bấm được, ${s.candidates.badge.length} badge; audio manager: ${s.audio.managers[0]?.type || "không thấy"}`);
  if (s.projectFiles.newCandidates?.length) L.push(`Mới so với file luật: ${s.projectFiles.newCandidates.join(", ")}`);
  if (s.traps.length) L.push(`Bẫy: ${s.traps.map((t) => t.kind).join(", ")}`);
  return L.join("\n");
}
