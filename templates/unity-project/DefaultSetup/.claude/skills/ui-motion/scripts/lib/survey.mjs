// Khảo sát một project Unity (chỉ đọc file, không cần mở Unity): module UI Motion có chưa và ở đâu, asset của project, file
// luật riêng, package, class của project tự có motion trên UI, class bấm được, audio manager, chỗ đóng screen, list pool,
// thói quen đặt tên, bẫy môi trường. Kết quả là ỨNG VIÊN kèm bằng chứng; người / Claude xác nhận rồi mới ghi thành luật.
import fs from "node:fs";
import path from "node:path";
import { exists, readText, rel, walk } from "./fsutil.mjs";
import { analyze, isAudioMethod, isShowHideMethod } from "./csharp.mjs";
import { CLASS, documents, hierarchy, objectList, scalar, stringList } from "./unityyaml.mjs";
import { baseName, matchKeyword, tokenize, matches } from "./tokens.mjs";

// Script của uGUI / TMP (GUID cố định trong package) — dùng khi project chưa có Library/PackageCache.
export const BUILTIN_SCRIPTS = {
  "4e29b1a8efbd4b44bb3f3716e73f07ff": "UnityEngine.UI.Button",
  fe87c0e1cc204ed48ad3b37840f39efc: "UnityEngine.UI.Image",
  "1344c3c82d62a2a41a3576d8abb8e3ea": "UnityEngine.UI.RawImage",
  "5f7201a12d95ffc409449d95f23cf332": "UnityEngine.UI.Text",
  "9085046f02f69544eb97fd06b6048fe2": "UnityEngine.UI.Toggle",
  "2fafe2cfe61f6974895a912c3755e8f1": "UnityEngine.UI.ToggleGroup",
  "67db9e8f0e2ae9c40bc1e2b64352a6b4": "UnityEngine.UI.Slider",
  "2a4db7a114972834c8e4117be1d82ba3": "UnityEngine.UI.Scrollbar",
  "1aa08ab6e0800fa44ae55d278d1423e3": "UnityEngine.UI.ScrollRect",
  "0d0b652f32a2cc243917e4028fa0f046": "UnityEngine.UI.Dropdown",
  d199490a83bb2b844b9695cbf13b01ef: "UnityEngine.UI.InputField",
  "31a19414c41e5ae4aae2af33fee712f6": "UnityEngine.UI.Mask",
  "3312d7739989d2b4e91e6319e9a96d76": "UnityEngine.UI.RectMask2D",
  "59f8146938fff824cb5fd77236b75775": "UnityEngine.UI.VerticalLayoutGroup",
  "30649d3a9faa99c48a7b1166b86bf2a0": "UnityEngine.UI.HorizontalLayoutGroup",
  "8a8695521f0d02e499659fee002a26c2": "UnityEngine.UI.GridLayoutGroup",
  "3245ec927659c4140ac4f8d17403cc18": "UnityEngine.UI.ContentSizeFitter",
  "306cc8c2b49d7114eaa3623786fc2126": "UnityEngine.UI.LayoutElement",
  d0b148fe25e99eb48b9724523833bab1: "UnityEngine.EventSystems.EventTrigger",
  "7a98125502f715b4b83cfb77b434e436": "UnityEngine.UI.Selectable",
  "0cd44c1031e13a943bb63640046fad76": "UnityEngine.UI.CanvasScaler",
  dc42784cf147c0c48a680349fa168899: "UnityEngine.UI.GraphicRaycaster",
  f4688fdb7df04437aeb418b961361dc5: "TMPro.TextMeshProUGUI",
  "7b743370ac3e4ec2a1668f5455a8ef8a": "TMPro.TMP_Dropdown",
  "2da0c512f12947e489f739169773d7ca": "TMPro.TMP_InputField",
};

const SELECTABLE = new Set(["UnityEngine.UI.Button", "UnityEngine.UI.Toggle", "UnityEngine.UI.Slider", "UnityEngine.UI.Scrollbar", "UnityEngine.UI.Dropdown", "UnityEngine.UI.InputField", "UnityEngine.UI.Selectable", "TMPro.TMP_Dropdown", "TMPro.TMP_InputField"]);
const UNITY_UI_BASES = new Set(["Button", "Toggle", "Slider", "Scrollbar", "Dropdown", "InputField", "Selectable", "ScrollRect", "Image", "RawImage", "Text", "Graphic", "MaskableGraphic", "TMP_Text", "TextMeshProUGUI", "TMP_Dropdown", "TMP_InputField", "LayoutGroup", "HorizontalOrVerticalLayoutGroup", "UIBehaviour"]);
const MONO_BASES = new Set(["MonoBehaviour", "UIBehaviour", "NetworkBehaviour", "SerializedMonoBehaviour"]);

// Folder thường là pack bên thứ ba / sample (không phải UI của project).
export const THIRD_PARTY = ["/Plugins/", "/ThirdParty/", "/3rdParty/", "/Third Party/", "/Samples/", "/Sample/", "/Demo/", "/Demos/", "/Examples/", "/Example/", "/AssetStore/", "/Asset Store/", "/Models/"];

// Gợi ý role theo từ trong tên class (thứ tự = ưu tiên).
const ROLE_WORDS = [
  ["Button", ["button", "btn", "click", "clickable", "press", "tap", "touchable"]],
  ["Popup", ["popup", "dialog", "modal", "alert", "confirm"]],
  ["Sheet", ["sheet", "drawer"]],
  ["Toast", ["toast", "snackbar", "notice"]],
  ["Tooltip", ["tooltip", "hint"]],
  ["Badge", ["badge", "reddot", "noti", "notification", "notify"]],
  ["List", ["list", "scroll", "grid", "stagger", "showing", "recycle", "pool", "infinite"]],
  ["Tab", ["tab", "tabs"]],
  ["Toggle", ["toggle", "switch", "checkbox"]],
  ["Card", ["card"]],
  ["Counter", ["counter", "number", "currency", "coin", "gold", "gem", "cash", "money", "score", "amount"]],
  ["Bar", ["bar", "progress", "hp", "health", "exp", "xp", "gauge"]],
  ["Timer", ["timer", "countdown", "clock"]],
  ["Spinner", ["loading", "loader", "spinner", "spin"]],
  ["DamageText", ["damage", "floating", "float", "popuptext"]],
  ["ScreenFx", ["shake", "flash", "screenfx", "camshake"]],
  ["Title", ["title", "header", "heading"]],
  ["Icon", ["icon", "avatar"]],
  ["Decoration", ["deco", "glow", "sparkle", "shine", "rotate", "rotator", "pulse", "bounce", "wobble", "idle", "effect", "fx"]],
  ["Screen", ["screen", "page", "window", "view", "panel", "feature", "menu", "hud", "layer", "ui", "controller", "scene"]],
];

const ABBREVIATIONS = { pnl: "Panel", dlg: "Popup", pop: "Popup", pu: "Popup", lst: "List", scrl: "List", scr: "List", cnt: "Counter", prg: "Bar", prog: "Bar", tmr: "Timer", bdg: "Badge", ttl: "Title", tgl: "Toggle", sld: "Slider", ic: "Icon", img: null, lbl: null, txt: null, bg: null };

function guessRoleFromName(className) {
  const tokens = tokenize(className);
  for (const [role, words] of ROLE_WORDS) {
    for (const w of words) if (matches(tokens, w)) return { role, word: w };
  }
  return null;
}

/** Đọc bảng keyword mặc định từ source module (FillDefaultKeywords) — không chép bảng vào script để khỏi lệch bản. */
function moduleDefaultKeywords(roleMapSource) {
  const out = [];
  if (!roleMapSource) return out;
  const body = /static void FillDefaultKeywords\s*\([^)]*\)\s*\{([\s\S]*?)\n\s*\}\s*\n/.exec(roleMapSource);
  if (!body) return out;
  const re = /Add\(keywords,\s*UIRole\.(\w+),([\s\S]*?)\);/g;
  let m;
  while ((m = re.exec(body[1]))) {
    for (const w of m[2].match(/"([^"]+)"/g) || []) out.push({ word: w.slice(1, -1), role: m[1] });
  }
  return out;
}

function moduleDefaultWords(roleMapSource, field) {
  if (!roleMapSource) return null;
  const m = new RegExp(field + "\\s*=\\s*\\{([^}]*)\\}").exec(roleMapSource);
  return m ? (m[1].match(/"([^"]+)"/g) || []).map((w) => w.slice(1, -1)) : null;
}

function parseRoleEnum(src) {
  const names = [];
  if (!src) return names;
  const m = /enum\s+UIRole\s*\{([\s\S]*?)\}/.exec(src);
  if (!m) return names;
  for (const part of m[1].split(",")) {
    const e = /(\w+)\s*=\s*(\d+)/.exec(part.replace(/\[[^\]]*\]/g, ""));
    if (e) names[+e[2]] = e[1];
  }
  return names;
}

// Folder gốc (Assets/<X>) có tên kiểu SDK / quảng cáo / analytics / plugin editor: gần như luôn là code bên thứ ba.
const VENDOR_TOP = /sdk|ads$|^ads|mediation|admob|applovin|maxsdk|ironsource|firebase|facebook|appsflyer|adjust|gameanalytics|googlemobileads|googleplay|playservices|externaldependencymanager|editor default resources|textmesh pro|^plugins$|^packages$/i;

// Gốc pack mua về (Assets/X hoặc Assets/X/Y có folder Documentation, Readme .txt / .pdf…) — điền lúc bắt đầu khảo sát.
let packRoots = [];

function isThirdParty(relPath) {
  const p = "/" + relPath;
  if (THIRD_PARTY.some((x) => p.includes(x))) return true;
  const segs = relPath.split("/");
  if (segs.slice(1, -1).some((s) => /^(demo|demos|example|examples|sample|samples)\b/i.test(s))) return true;
  if (packRoots.some((r) => relPath === r || relPath.startsWith(r + "/"))) return true;
  const top = segs[1] || "";
  return VENDOR_TOP.test(top.replace(/\s+/g, " "));
}

/** Folder cấp 1–2 dưới Assets mang dấu hiệu pack: folder Documentation, file Readme .txt / .pdf / .rtf, PDF hướng dẫn. */
function findPackRoots(assets, root) {
  const out = [];
  const isMarker = (e) =>
    (e.isDirectory() && /^documentation$/i.test(e.name)) ||
    (e.isFile() && (/^read[ _-]?me[^/]*\.(txt|pdf|rtf|html?)$/i.test(e.name) || /(manual|documentation|guide|userguide).*\.pdf$/i.test(e.name)));
  const visit = (abs, depth) => {
    let entries;
    try {
      entries = fs.readdirSync(abs, { withFileTypes: true });
    } catch {
      return;
    }
    if (depth >= 1 && entries.some(isMarker)) {
      out.push(rel(root, abs));
      return;
    }
    if (depth >= 2) return;
    for (const e of entries) if (e.isDirectory() && !e.name.startsWith(".") && !e.name.endsWith("~")) visit(path.join(abs, e.name), depth + 1);
  };
  visit(assets, 0);
  return out;
}

const isEditorPath = (relPath) => ("/" + relPath).includes("/Editor/");

/**
 * Chạy khảo sát. `opts`: { root, moduleSource } — moduleSource (folder module đi kèm skill) dùng cho bảng keyword / tên role khi
 * project chưa có module. Trả object survey (ghi JSON được).
 */
export function survey(opts) {
  const root = opts.root;
  const assets = path.join(root, "Assets");
  packRoots = findPackRoots(assets, root);
  const out = {
    tool: "ui-motion survey",
    schema: 1,
    generatedAt: new Date().toISOString(),
    project: {},
    packages: {},
    module: null,
    data: { assets: [], roleMap: null, settings: null, insideModule: [] },
    projectFiles: {},
    counts: {},
    candidates: { ownMotion: [], clickable: [], badge: [] },
    audio: { managers: [], enums: [], doubleSoundRisk: [] },
    screens: { showHideClasses: [], setActiveFalseFiles: [], managers: [] },
    lists: { pools: [], spawnedPrefabTokens: [] },
    naming: {},
    folders: {},
    traps: [],
    notes: [],
  };

  // ---- Project ----
  const version = readText(path.join(root, "ProjectSettings", "ProjectVersion.txt")) || "";
  const ps = readText(path.join(root, "ProjectSettings", "ProjectSettings.asset")) || "";
  out.project = {
    root,
    name: (/productName:\s*(.+)/.exec(ps) || [])[1]?.trim() || path.basename(root),
    unity: (/m_EditorVersion:\s*(\S+)/.exec(version) || [])[1] || null,
    inputHandler: { 0: "Input Manager cũ", 1: "Input System", 2: "cả hai" }[(/activeInputHandler:\s*(\d)/.exec(ps) || [])[1]] || null,
  };

  // ---- Packages ----
  const manifestText = readText(path.join(root, "Packages", "manifest.json"));
  let deps = {};
  try {
    deps = manifestText ? JSON.parse(manifestText).dependencies || {} : {};
  } catch {
    out.notes.push("Packages/manifest.json không đọc được (JSON lỗi).");
  }
  const has = (id) => Object.prototype.hasOwnProperty.call(deps, id);

  // ---- Script GUID → class (Assets, Packages nhúng, PackageCache) ----
  const guidToScript = new Map(Object.entries(BUILTIN_SCRIPTS).map(([g, n]) => [g, { name: n, path: null, builtin: true }]));
  const scriptFiles = [];
  const metaGuid = (abs) => {
    const t = readText(abs + ".meta");
    const m = t && /guid:\s*([0-9a-f]{32})/.exec(t);
    return m ? m[1] : null;
  };
  walk(assets, [".cs"], (abs) => scriptFiles.push(abs));
  const pkgScripts = [];
  walk(path.join(root, "Packages"), [".cs"], (abs) => pkgScripts.push(abs));
  const cache = path.join(root, "Library", "PackageCache");
  if (exists(cache)) walk(cache, [".cs"], (abs) => /[\\/](Runtime|Scripts)[\\/]/.test(abs) && pkgScripts.push(abs));
  for (const abs of pkgScripts) {
    const g = metaGuid(abs);
    if (g && !guidToScript.has(g)) guidToScript.set(g, { name: path.basename(abs, ".cs"), path: abs, package: true });
  }

  // ---- Code của project ----
  const files = [];
  const classIndex = new Map(); // tên class → { file, base, ... }
  let moduleDir = null;
  let moduleVersion = null;
  const otherStubs = [];
  for (const abs of scriptFiles) {
    const r = rel(root, abs);
    const src = readText(abs);
    if (src == null) continue;
    if (path.basename(abs) === "UIMotionDefaults.cs" && /const string Version = "([^"]+)"/.test(src)) {
      if (moduleDir) out.notes.push(`Có hơn một UIMotionDefaults.cs: ${moduleDir} và ${rel(root, path.dirname(abs))} — project chép module hai lần.`);
      else {
        moduleDir = path.dirname(abs);
        moduleVersion = /const string Version = "([^"]+)"/.exec(src)[1];
      }
    }
    const info = analyze(src);
    const guid = metaGuid(abs);
    const file = { abs, rel: r, guid, info, editor: /\/Editor\//.test("/" + r + "/"), thirdParty: isThirdParty(r) };
    files.push(file);
    if (guid) guidToScript.set(guid, { name: path.basename(abs, ".cs"), path: abs, file });
    for (const c of info.classes) {
      if (!classIndex.has(c.name)) classIndex.set(c.name, { ...c, file, namespace: info.namespace });
    }
    if (info.features.odinStubs) otherStubs.push(r);
  }
  const moduleRel = moduleDir ? rel(root, moduleDir) : null;
  const inModule = (r) => moduleRel && (r === moduleRel || r.startsWith(moduleRel + "/"));

  // ---- Packages / plugin nhận ra (file .cs và .dll trong Assets) ----
  const dlls = [];
  walk(assets, [".dll"], (abs) => dlls.push(rel(root, abs)));
  const anyFile = (re) => files.some((f) => re.test(f.rel)) || dlls.some((d) => re.test(d));
  out.packages = {
    dotween: anyFile(/(^|\/)DOTween\.dll$|Demigiant\/DOTween\//),
    dotweenModules: anyFile(/DOTweenModuleUI\.cs$/),
    dotweenPro: anyFile(/DOTweenPro\//),
    odin: anyFile(/Sirenix\.OdinInspector\.Attributes\.dll$/) || has("com.sirenix.odininspector"),
    tmp: has("com.unity.textmeshpro") || (has("com.unity.ugui") && /^2\./.test(deps["com.unity.ugui"] || "")) || anyFile(/TextMesh Pro\//),
    unitask: has("com.cysharp.unitask") || anyFile(/UniTask\//),
    uiEffect: has("com.coffee.ui-effect") || anyFile(/UIEffect\//),
    uiExtensions: Object.keys(deps).some((k) => /uiextensions/i.test(k)) || anyFile(/Unity-UI-Extensions|UnityUIExtensions/),
    inputSystem: has("com.unity.inputsystem"),
    uiToolkit: false,
    listPools: [],
  };
  if (anyFile(/Com\.TheFallenGames\.OSA|\/OSA\//) || files.some((f) => f.info.namespace && /TheFallenGames\.OSA/.test(f.info.namespace))) out.packages.listPools.push("OSA (Optimized ScrollView Adapter)");
  if (anyFile(/EnhancedScroller/)) out.packages.listPools.push("EnhancedScroller");
  if (anyFile(/LoopScrollRect|LoopScroll/)) out.packages.listPools.push("LoopScrollRect");
  if (anyFile(/FancyScrollView/)) out.packages.listPools.push("FancyScrollView");
  if (anyFile(/SuperScrollView/)) out.packages.listPools.push("SuperScrollView");
  if (anyFile(/RecyclableScroll|RecycleScroll/)) out.packages.listPools.push("Recyclable Scroll Rect");

  // ---- Module ----
  if (moduleDir) {
    const docs = path.join(moduleDir, "Docs", "UIMotion_QuyChuan.md");
    const qc = readText(docs) || "";
    out.module = {
      path: moduleRel,
      version: moduleVersion,
      rulesDoc: exists(docs) ? rel(root, docs) : null,
      rulesDocVersion: (/Phiên bản ([\d.]+)/.exec(qc) || [])[1] || null,
      hasOdinStubs: exists(path.join(moduleDir, "OdinStubs")),
      skill: exists(path.join(moduleDir, "Skill~", "ui-motion", "SKILL.md")) ? rel(root, path.join(moduleDir, "Skill~", "ui-motion")) : null,
    };
    if (out.module.hasOdinStubs) {
      const dup = otherStubs.filter((r) => !inModule(r));
      if (dup.length && !out.packages.odin) out.traps.push({ kind: "odin-stubs", detail: `Project có bộ stub Odin khác ngoài module (${dup.join(", ")}): trùng class khi không cài Odin — xoá một bộ.` });
    }
  }
  // Bảng mặc định / tên role: module của project; chưa có thì module nguồn đi kèm skill.
  const srcDir = moduleDir || (opts.moduleSource && exists(path.join(opts.moduleSource, "UIMotionDefaults.cs")) ? opts.moduleSource : null);
  const roleMapSrc = srcDir ? readText(path.join(srcDir, "Roles", "UIMotionRoleMap.cs")) : null;
  const roleNames = parseRoleEnum(srcDir ? readText(path.join(srcDir, "Roles", "UIMotionRoleTypes.cs")) : null);
  const roleName = (n) => roleNames[+n] || `role#${n}`;

  // ---- Asset của project (RoleMap, Settings, Profile) ----
  const assetFiles = [];
  walk(assets, [".asset"], (abs, name) => /^UIMotion(RoleMap|Settings|Profile_\w+)\.asset$/.test(name) && assetFiles.push(abs));
  for (const abs of assetFiles) {
    const r = rel(root, abs);
    out.data.assets.push(r);
    if (inModule(r)) out.data.insideModule.push(r);
    const text = readText(abs) || "";
    const doc = documents(text)[0];
    if (!doc) continue;
    if (/UIMotionRoleMap\.asset$/.test(r)) {
      out.data.roleMap = {
        path: r,
        defaultsVersion: scalar(doc, "_defaultsVersion") || null,
        projectRulesHash: scalar(doc, "_projectRulesHash") || null,
        componentRules: objectList(doc, "_componentRules").map((x) => ({ type: x.TypeName, role: roleName(x.Role), ownMotion: x.HasOwnMotion === "1", note: x.Note || "" })),
        keywords: objectList(doc, "_keywords").map((x) => ({ word: (x.Keyword || "").toLowerCase(), role: roleName(x.Role) })),
        itemPrefabWords: text.includes("_itemPrefabWords:") ? stringList(doc, "_itemPrefabWords") : null,
        templatePrefabWords: text.includes("_templatePrefabWords:") ? stringList(doc, "_templatePrefabWords") : null,
      };
    } else if (/UIMotionSettings\.asset$/.test(r)) {
      out.data.settings = { path: r, scanFolders: stringList(doc, "_scanFolders"), excludePathContains: stringList(doc, "_excludePathContains"), sfxSinkType: scalar(doc, "_sfxSinkType") || "" };
    }
  }
  out.data.folder = out.data.assets.length ? path.posix.dirname(out.data.assets.find((a) => !inModule(a)) || out.data.assets[0]) : null;

  // ---- File riêng của project ----
  const rulesPath = path.join(root, "ProjectSettings", "UIMotionProject.json");
  const notesPath = path.join(root, "ProjectSettings", "UIMotionProject.md");
  const debtPath = path.join(root, "ProjectSettings", "UIMotionDebt.json");
  const rulesText = readText(rulesPath);
  out.projectFiles = { rules: rulesText != null ? "ProjectSettings/UIMotionProject.json" : null, notes: exists(notesPath) ? "ProjectSettings/UIMotionProject.md" : null, debt: null };
  if (rulesText != null) {
    try {
      out.projectFiles.rulesData = JSON.parse(rulesText);
    } catch (e) {
      out.projectFiles.rulesError = String(e.message || e);
    }
  }
  const debtText = readText(debtPath);
  if (debtText) {
    const n = (debtText.match(/"rule"\s*:/gi) || []).length;
    out.projectFiles.debt = { path: "ProjectSettings/UIMotionDebt.json", entries: n };
  }

  // Bảng keyword đang có hiệu lực: asset của project, không có thì mặc định của module.
  const keywordTable = out.data.roleMap?.keywords?.length ? out.data.roleMap.keywords : moduleDefaultKeywords(roleMapSrc);
  const itemWords = out.data.roleMap?.itemPrefabWords || moduleDefaultWords(roleMapSrc, "DefaultItemPrefabWords") || ["item", "cell", "slot", "row", "entry", "element", "card"];
  const templateWords = out.data.roleMap?.templatePrefabWords || moduleDefaultWords(roleMapSrc, "DefaultTemplatePrefabWords") || ["template", "layout", "tpl"];
  const ruleTypes = new Set((out.data.roleMap?.componentRules || []).map((r) => r.type));
  const fileRuleTypes = new Set((out.projectFiles.rulesData?.componentRules || []).map((r) => r.type));

  // ---- Prefab ----
  const prefabFiles = [];
  walk(assets, [".prefab"], (abs) => prefabFiles.push(abs));
  const sceneFiles = [];
  walk(assets, [".unity"], (abs) => sceneFiles.push(abs));
  const prefabByGuid = new Map();
  for (const abs of prefabFiles) {
    const g = metaGuid(abs);
    if (g) prefabByGuid.set(g, rel(root, abs));
  }
  const usage = new Map(); // class name → { prefabs:Set, root, button, stretch, names:[] }
  const nameTokens = new Map(); // token → count (tên object trong prefab UI của project)
  const unmatchedTokens = new Map();
  const roleByKeyword = new Map();
  const nestedCount = new Map(); // guid prefab → số lần được lồng
  const spawnRefs = new Map(); // guid prefab → số lần field trỏ tới
  const folders = new Map(); // Assets/<top>/<sub> → số prefab UI
  const segmentCounts = new Map(); // tên folder bất kỳ cấp → số prefab UI (tìm folder pack)
  let uiPrefabs = 0;
  let ownUiPrefabs = 0;
  const unknownDll = new Map();
  for (const abs of prefabFiles) {
    const r = rel(root, abs);
    const text = readText(abs);
    if (!text || !text.includes("--- !u!224 ")) {
      if (text) for (const m of text.matchAll(/m_SourcePrefab: \{fileID: \d+, guid: ([0-9a-f]{32})/g)) nestedCount.set(m[1], (nestedCount.get(m[1]) || 0) + 1);
      continue;
    }
    if (isEditorPath(r)) continue;
    uiPrefabs++;
    const third = isThirdParty(r) || inModule(r);
    if (!third) ownUiPrefabs++;
    const top = r.split("/").slice(0, 3).join("/");
    if (!third) folders.set(top, (folders.get(top) || 0) + 1);
    if (!third) for (const seg of new Set(r.split("/").slice(1, -1))) segmentCounts.set(seg, (segmentCounts.get(seg) || 0) + 1);
    const h = hierarchy(text);
    for (const pi of h.prefabInstances) nestedCount.set(pi.sourceGuid, (nestedCount.get(pi.sourceGuid) || 0) + 1);
    for (const o of h.objects.values()) {
      const classes = [];
      for (const c of o.components) {
        if (c.classId !== CLASS.MonoBehaviour || !c.script) continue;
        const s = c.script.guid ? guidToScript.get(c.script.guid) : null;
        if (s) classes.push(s.name);
        else if (c.script.guid) unknownDll.set(c.script.guid, (unknownDll.get(c.script.guid) || 0) + 1);
        for (const g of c.guids) if (prefabByGuid.has(g)) spawnRefs.set(g, (spawnRefs.get(g) || 0) + 1);
      }
      if (third) continue;
      const isRoot = !o.parent;
      const stretch = o.rect && o.rect.anchorMin && o.rect.anchorMax && o.rect.anchorMin.x <= 0.001 && o.rect.anchorMin.y <= 0.001 && o.rect.anchorMax.x >= 0.999 && o.rect.anchorMax.y >= 0.999;
      const selectable = classes.some((n) => SELECTABLE.has(n));
      for (const n of classes) {
        const u = usage.get(n) || { prefabs: new Set(), uses: 0, root: 0, selectable: 0, stretch: 0, names: [] };
        u.prefabs.add(r);
        u.uses++;
        if (isRoot) u.root++;
        if (selectable && !SELECTABLE.has(n)) u.selectable++;
        if (stretch) u.stretch++;
        if (u.names.length < 6 && !u.names.includes(o.name)) u.names.push(o.name);
        usage.set(n, u);
      }
      // Thói quen đặt tên
      const tokens = tokenize(o.name);
      for (const t of new Set(tokens)) if (t.length >= 2 && !/^\d+$/.test(t)) nameTokens.set(t, (nameTokens.get(t) || 0) + 1);
      const hit = matchKeyword(o.name, keywordTable);
      if (hit) roleByKeyword.set(hit.role, (roleByKeyword.get(hit.role) || 0) + 1);
      else for (const t of new Set(tokens)) if (t.length >= 2 && !/^\d+$/.test(t)) unmatchedTokens.set(t, (unmatchedTokens.get(t) || 0) + 1);
    }
  }
  // Scene: object UI (có RectTransform) — con trực tiếp của Canvas gốc tính như gốc screen.
  let uiScenes = 0;
  for (const abs of sceneFiles) {
    const r = rel(root, abs);
    if (isThirdParty(r) || inModule(r) || isEditorPath(r)) continue;
    const text = readText(abs);
    if (!text || !text.includes("--- !u!224 ") || !text.includes("--- !u!223 ")) continue;
    uiScenes++;
    const sceneTop = r.split("/").slice(0, 3).join("/");
    folders.set(sceneTop, (folders.get(sceneTop) || 0) + 1);
    const h = hierarchy(text);
    for (const o of h.objects.values()) {
      if (!o.rect) continue;
      const parent = o.parent ? h.objects.get(o.parent) : null;
      const screenRoot = !!parent && !parent.parent && parent.components.some((c) => c.classId === CLASS.Canvas);
      const classes = [];
      for (const c of o.components) {
        if (c.classId !== CLASS.MonoBehaviour || !c.script?.guid) continue;
        const sc = guidToScript.get(c.script.guid);
        if (sc) classes.push(sc.name);
      }
      const selectable = classes.some((n) => SELECTABLE.has(n));
      for (const n of classes) {
        const u = usage.get(n) || { prefabs: new Set(), uses: 0, root: 0, selectable: 0, stretch: 0, names: [] };
        u.prefabs.add(r);
        u.uses++;
        if (screenRoot) u.root++;
        if (selectable && !SELECTABLE.has(n)) u.selectable++;
        if (u.names.length < 6 && !u.names.includes(o.name)) u.names.push(o.name);
        usage.set(n, u);
      }
      const tokens = tokenize(o.name);
      for (const t of new Set(tokens)) if (t.length >= 2 && !/^\d+$/.test(t)) nameTokens.set(t, (nameTokens.get(t) || 0) + 1);
    }
  }
  out.counts = { scripts: files.length, prefabs: prefabFiles.length, uiPrefabs, ownUiPrefabs, uiScenes, unknownDllScripts: unknownDll.size };

  // ---- Folder UI ----
  const folderList = [...folders.entries()].sort((a, b) => b[1] - a[1]);
  const total = folderList.reduce((s, [, n]) => s + n, 0);
  let acc = 0;
  const scanSuggestion = [];
  for (const [f, n] of folderList) {
    if (acc >= total * 0.95) break;
    scanSuggestion.push(f);
    acc += n;
  }
  const topLevel = new Set(scanSuggestion.map((f) => f.split("/").slice(0, 2).join("/")));
  const packSegments = [...segmentCounts.entries()]
    .filter(([seg, n]) => n >= 3 && /pack|download|vendor|external|imported|assetstore|asset ?source/i.test(seg))
    .map(([seg, n]) => ({ contains: "/" + seg + "/", uiPrefabs: n }));
  out.folders = {
    uiPrefabsByFolder: folderList.slice(0, 15).map(([f, n]) => ({ folder: f, uiPrefabs: n })),
    scanFoldersSuggestion: topLevel.size && topLevel.size <= 3 ? [...topLevel] : ["Assets"],
    thirdPartyPrefabs: uiPrefabs - ownUiPrefabs,
    packRoots,
    excludeSuggestion: packSegments,
  };

  // ---- Class ứng viên ----
  const chainMemo = new Map();
  const baseChain = (name) => {
    if (chainMemo.has(name)) return chainMemo.get(name);
    const chain = [];
    let cur = classIndex.get(name);
    const seen = new Set();
    while (cur && cur.base && !seen.has(cur.base) && chain.length < 12) {
      seen.add(cur.base);
      chain.push(cur.base.split(".").pop());
      cur = classIndex.get(cur.base.split(".").pop());
    }
    chainMemo.set(name, chain);
    return chain;
  };
  const inheritsUnityUI = (name) => baseChain(name).some((b) => UNITY_UI_BASES.has(b));
  const isMono = (name) => {
    const chain = baseChain(name);
    return chain.some((b) => MONO_BASES.has(b) || UNITY_UI_BASES.has(b));
  };
  const motionOf = (name) => {
    let f = classIndex.get(name)?.file;
    const reasons = [];
    const visit = (file) => {
      if (!file) return;
      const x = file.info.features;
      if (x.uiTweens) reasons.push(`${x.uiTweens} lệnh DOTween lên UI`);
      if (x.otherTween) reasons.push("tween khác (LeanTween / PrimeTween / iTween)");
      if (x.animator) reasons.push("Animator (SetTrigger / Play)");
      if (x.manualFade) reasons.push("fade CanvasGroup bằng tay");
    };
    visit(f);
    for (const b of baseChain(name)) visit(classIndex.get(b)?.file);
    return [...new Set(reasons)];
  };
  // Dùng trong prefab: cộng cả lớp con vào lớp cha của project (tính một lượt).
  const aggregated = new Map();
  for (const [n, u] of usage) {
    for (const target of [n, ...baseChain(n)]) {
      const agg = aggregated.get(target) || { prefabs: new Set(), uses: 0, root: 0, selectable: 0, stretch: 0, names: [], subclasses: [] };
      if (target !== n) agg.subclasses.push(n);
      for (const p of u.prefabs) agg.prefabs.add(p);
      agg.uses += u.uses;
      agg.root += u.root;
      agg.selectable += u.selectable;
      agg.stretch += u.stretch;
      for (const x of u.names) if (agg.names.length < 6 && !agg.names.includes(x)) agg.names.push(x);
      aggregated.set(target, agg);
    }
  }
  const EMPTY_USAGE = { prefabs: new Set(), uses: 0, root: 0, selectable: 0, stretch: 0, names: [], subclasses: [] };
  const usageOf = (name) => aggregated.get(name) || EMPTY_USAGE;

  const seenRule = new Set();
  for (const [name, c] of classIndex) {
    const f = c.file;
    if (f.editor || inModule(f.rel) || f.thirdParty) continue;
    if (!isMono(name)) continue;
    const u = usageOf(name);
    if (u.uses === 0) continue;
    // Lớp cha của project đã được dùng thì luật đặt ở lớp cha (khớp cả lớp con) — bỏ lớp con có cùng dấu hiệu.
    const parentInProject = baseChain(name).find((b) => classIndex.has(b) && !classIndex.get(b).file.thirdParty && !inModule(classIndex.get(b).file.rel) && usageOf(b).uses > 0 && isMono(b) && !MONO_BASES.has(b));
    const motion = motionOf(name);
    const byName = guessRoleFromName(name);
    let role = byName ? byName.role : null;
    const evidence = [];
    // Vai trò theo chỗ gắn: nằm cùng object bấm được → họ nút; ở gốc prefab / con trực tiếp của Canvas và có chỗ kéo giãn kín
    // → họ screen. Chỉ "cao" khi tên và chỗ gắn cùng chỉ một họ.
    const BUTTON_FAMILY = new Set(["Button", "Toggle", "Tab", "Card", "Dropdown"]);
    const SCREEN_FAMILY = new Set(["Screen", "Popup", "Sheet", "Toast", "Tooltip", "Panel"]);
    let context = null;
    if (u.selectable / u.uses >= 0.6) {
      context = "Button";
      if (!role || role === "Screen" || role === "Decoration") role = "Button";
      evidence.push(`${u.selectable}/${u.uses} chỗ nằm cùng object bấm được`);
    } else if (u.root / u.uses >= 0.6) {
      if (u.stretch > 0) context = "Screen";
      if (!role || role === "Decoration") role = "Screen";
      evidence.push(`${u.root}/${u.uses} chỗ gắn ở gốc prefab` + (u.stretch ? `, ${u.stretch} kéo giãn kín` : ""));
    }
    if (byName) evidence.push(`tên có "${byName.word}"`);
    const agree = !!byName && !!context &&
      ((context === "Button" && BUTTON_FAMILY.has(byName.role)) || (context === "Screen" && SCREEN_FAMILY.has(byName.role)));
    const item = {
      type: c.namespace ? `${c.namespace}.${name}` : name,
      shortName: name,
      file: f.rel,
      role: role || "Unknown",
      prefabs: u.prefabs.size,
      uses: u.uses,
      examples: u.names,
      subclasses: u.subclasses.slice(0, 6),
      motion,
      evidence,
      tweenLines: f.info.tweenLines,
      inRoleMap: ruleTypes.has(name) || ruleTypes.has(c.namespace ? `${c.namespace}.${name}` : name),
      inRulesFile: fileRuleTypes.has(name) || fileRuleTypes.has(c.namespace ? `${c.namespace}.${name}` : name),
      coveredByParent: parentInProject || null,
    };
    if (motion.length) {
      item.confidence = !role || role === "Unknown" ? "thấp" : agree ? "cao" : "vừa";
      out.candidates.ownMotion.push(item);
    } else if (f.info.features.pointerHandlers && !inheritsUnityUI(name) && (role === "Button" || !role || role === "Screen")) {
      if (u.selectable / u.uses < 0.6) {
        item.role = "Button";
        item.confidence = "vừa";
        out.candidates.clickable.push(item);
      }
    } else if (byName && byName.role === "Badge") {
      item.confidence = "vừa";
      out.candidates.badge.push(item);
    }
    seenRule.add(name);
  }
  const byUses = (a, b) => b.uses - a.uses;
  out.candidates.ownMotion.sort(byUses);
  out.candidates.clickable.sort(byUses);
  out.candidates.badge.sort(byUses);

  // ---- Âm thanh ----
  const projectEnums = new Map();
  for (const f of files) for (const e of f.info.enums) if (!projectEnums.has(e.name)) projectEnums.set(e.name, { name: e.name, file: f.rel, members: e.members, kind: "enum" });
  // Class chứa hằng chuỗi tên tiếng (class Sfx { public const string Touch = "touch"; }) — dùng như enum khi gọi audio manager.
  for (const f of files) {
    const holder = f.info.classes[0];
    if (holder && f.info.constStrings.length >= 3 && !projectEnums.has(holder.name)) projectEnums.set(holder.name, { name: holder.name, file: f.rel, members: f.info.constStrings, kind: "const string" });
  }
  const audioClasses = [];
  for (const [name, c] of classIndex) {
    const f = c.file;
    if (f.editor || inModule(f.rel)) continue;
    const audioMethods = f.info.methods.filter(isAudioMethod);
    if (!audioMethods.length) continue;
    if (!(f.info.features.audioSource || f.info.features.playOneShot || /audio|sound|sfx|music/i.test(name))) continue;
    audioClasses.push({ name, type: c.namespace ? `${c.namespace}.${name}` : name, file: f.rel, methods: [...new Set(audioMethods)].slice(0, 8), singleton: f.info.features.singleton, thirdParty: f.thirdParty });
  }
  // Đếm chỗ gọi trong code project + ví dụ dòng gọi
  for (const a of audioClasses) {
    const re = new RegExp("\\b" + a.name + "\\b(?:\\s*\\.\\s*(?:Instance|I|Ins|instance)\\b)?\\s*\\.\\s*(\\w+)\\s*\\(([^;]*)", "g");
    let calls = 0;
    const examples = [];
    const argTypes = new Map();
    for (const f of files) {
      if (f.rel === a.file || f.editor) continue;
      const src = readText(f.abs) || "";
      let m;
      while ((m = re.exec(src))) {
        if (!a.methods.includes(m[1]) && !isAudioMethod(m[1])) continue;
        calls++;
        if (examples.length < 5) examples.push(`${f.rel}: ${a.name}…${m[1]}(${m[2].slice(0, 60).trim()}`);
        for (const pair of m[2].matchAll(/\b([A-Za-z_]\w*)\.([A-Za-z_]\w*)/g)) {
          const t = projectEnums.has(pair[1]) ? pair[1] : projectEnums.has(pair[2]) ? pair[2] : null;
          if (t) argTypes.set(t, (argTypes.get(t) || 0) + 1);
        }
      }
    }
    a.calls = calls;
    a.examples = examples;
    a.argTypes = [...argTypes.entries()].sort((x, y) => y[1] - x[1]).map(([t]) => t);
  }
  audioClasses.sort((x, y) => y.calls - x.calls);
  out.audio.managers = audioClasses.filter((a) => a.calls > 0 || a.singleton).slice(0, 5);
  const enumNames = new Set(out.audio.managers.flatMap((a) => a.argTypes));
  for (const e of projectEnums.values()) if (enumNames.has(e.name) || /^(Sfx|SFX|Sounds?|SoundType|SfxType|AudioType|SoundId|SfxId)$/.test(e.name)) out.audio.enums.push({ name: e.name, kind: e.kind, file: e.file, members: e.members.slice(0, 80) });
  // Tiếng kép: class có sự kiện bấm (không phải module) mà tự gọi audio manager
  const managerNames = out.audio.managers.map((a) => a.name);
  if (managerNames.length) {
    const re = new RegExp("\\b(" + managerNames.join("|") + ")\\b");
    for (const f of files) {
      if (f.editor || inModule(f.rel) || !f.info.features.uiEvents) continue;
      const src = readText(f.abs) || "";
      if (re.test(src)) out.audio.doubleSoundRisk.push({ file: f.rel, classes: f.info.classes.map((c) => c.name).slice(0, 3) });
    }
  }

  // ---- Mở / đóng screen ----
  for (const f of files) {
    if (f.editor || inModule(f.rel) || f.thirdParty || isThirdParty(f.rel)) continue;
    const sh = f.info.methods.filter(isShowHideMethod);
    if (f.info.features.setActiveFalse) out.screens.setActiveFalseFiles.push({ file: f.rel, count: f.info.features.setActiveFalse });
    if (sh.length && (f.info.features.setActiveFalse || f.info.features.destroy)) {
      const cls = f.info.classes.map((c) => c.name);
      const stackLike = /\bStack<|\bList<\w*(Screen|Popup|Panel|View|Feature|Controller)\w*>|\bDictionary<[^>]*(Screen|Popup|Panel|View|Feature)/.test(readText(f.abs) || "");
      const entry = { file: f.rel, classes: cls.slice(0, 3), methods: [...new Set(sh)].slice(0, 8), setActiveFalse: f.info.features.setActiveFalse, destroy: f.info.features.destroy, instantiate: f.info.features.instantiate };
      if (stackLike || (f.info.features.instantiate && sh.length >= 2)) out.screens.managers.push(entry);
      else out.screens.showHideClasses.push(entry);
    }
  }
  out.screens.setActiveFalseFiles.sort((a, b) => b.count - a.count);
  out.screens.setActiveFalseTotal = out.screens.setActiveFalseFiles.reduce((s, x) => s + x.count, 0);
  out.screens.setActiveFalseFiles = out.screens.setActiveFalseFiles.slice(0, 12);
  out.screens.managers = out.screens.managers.slice(0, 8);
  out.screens.showHideClasses = out.screens.showHideClasses.slice(0, 12);
  out.screens.usesUIMotion = files.filter((f) => !inModule(f.rel) && f.info.features.usesUIMotion).map((f) => f.rel).slice(0, 12);

  // ---- List / prefab item ----
  out.lists.pools = out.packages.listPools;
  const prefabTokens = new Map();
  const templateTokens = new Map();
  for (const [g, r] of prefabByGuid) {
    if (isThirdParty(r) || inModule(r)) continue;
    const name = path.posix.basename(r, ".prefab");
    const tokens = [...new Set(tokenize(name))].filter((t) => t.length >= 2 && !/^\d+$/.test(t));
    if (spawnRefs.get(g)) for (const t of tokens) prefabTokens.set(t, (prefabTokens.get(t) || 0) + 1);
    if (!nestedCount.get(g)) for (const t of tokens) templateTokens.set(t, (templateTokens.get(t) || 0) + 1);
  }
  const ITEMISH = ["item", "cell", "slot", "row", "entry", "element", "card", "view", "line", "unit", "record", "widget", "node", "tile", "box"];
  const TEMPLATEISH = ["template", "layout", "tpl", "base", "frame", "prototype", "master", "skeleton", "shell"];
  out.lists.itemPrefabWords = { current: itemWords, suggestAdd: [...prefabTokens.entries()].filter(([t, n]) => n >= 3 && ITEMISH.includes(t) && !itemWords.includes(t)).map(([t, n]) => ({ word: t, prefabs: n })) };
  out.lists.templatePrefabWords = { current: templateWords, suggestAdd: [...templateTokens.entries()].filter(([t, n]) => n >= 2 && TEMPLATEISH.includes(t) && !templateWords.includes(t)).map(([t, n]) => ({ word: t, prefabs: n })) };
  out.lists.spawnedPrefabTokens = [...prefabTokens.entries()].sort((a, b) => b[1] - a[1]).slice(0, 15).map(([t, n]) => ({ token: t, prefabs: n }));

  // ---- Đặt tên ----
  const tops = (m, n) => [...m.entries()].sort((a, b) => b[1] - a[1]).slice(0, n).map(([t, c]) => ({ token: t, count: c }));
  const kwWords = new Set(keywordTable.map((k) => k.word));
  out.naming = {
    keywordSource: out.data.roleMap?.keywords?.length ? out.data.roleMap.path : moduleDir ? "bảng mặc định của module" : srcDir ? "bảng mặc định của module nguồn (project chưa có module)" : null,
    objectsByKeywordRole: Object.fromEntries([...roleByKeyword.entries()].sort((a, b) => b[1] - a[1])),
    topTokens: tops(nameTokens, 25),
    topUnmatchedTokens: tops(unmatchedTokens, 25),
    abbreviationSuggestions: [...nameTokens.entries()].filter(([t, n]) => n >= 3 && ABBREVIATIONS[t] && !kwWords.has(t)).map(([t, n]) => ({ word: t, role: ABBREVIATIONS[t], objects: n })),
  };

  // ---- Bẫy môi trường ----
  for (const f of files) {
    if (inModule(f.rel)) continue;
    if (f.info.features.targetFrameRate && !f.thirdParty) out.traps.push({ kind: "fps-cap", detail: `${f.rel} đặt Application.targetFrameRate — test Play mode chờ Time.frameCount đổi, không tin một lần yield null.` });
    if (f.editor && f.info.features.initializeOnLoad && f.info.features.playModeHook) out.traps.push({ kind: "editor-hook", detail: `${f.rel}: hook Editor mở / nạp scene khi vào Play — có thể chặn test Play mode chạy batch (scene tạm của test không có đường dẫn).` });
  }
  if (out.packages.dotween === false) out.traps.push({ kind: "no-dotween", detail: "Không thấy DOTween: module UI Motion cần DOTween (Demigiant) để compile." });
  else if (!out.packages.dotweenModules) out.traps.push({ kind: "dotween-modules", detail: "Có DOTween nhưng không thấy DOTween Modules (DOTweenModuleUI.cs): module cần DOFade / DOAnchorPos… của Modules — chạy DOTween Utility Panel > Setup (tick UI) trước khi chép module, không thì lỗi compile trông như module hỏng." });
  if (out.data.insideModule.length) out.traps.push({ kind: "data-in-module", detail: `${out.data.insideModule.length} asset của project nằm trong folder module — dời ra ngoài (Tools/UI Motion/Dời asset của project ra khỏi folder module) trước khi chép bản module mới.` });

  // ---- Dòng Component của RoleMap: của module / Unity / package, của project (class có trong code), hay thừa ----
  if (out.data.roleMap) {
    const builtin = (t) => /^(UnityEngine|TMPro|DG\.Tweening)\./.test(t) || /^UIMotion|^UIDamageText/.test(t.split(".").pop());
    const exists = (t) => classIndex.has(t.split(".").pop());
    out.data.roleMap.projectRows = out.data.roleMap.componentRules.filter((r) => r.type && !builtin(r.type) && exists(r.type));
    out.data.roleMap.staleRows = out.data.roleMap.componentRules.filter((r) => r.type && !builtin(r.type) && !exists(r.type));
  }

  // ---- So với file luật riêng ----
  if (out.projectFiles.rulesData) {
    const inFile = new Set((out.projectFiles.rulesData.componentRules || []).map((r) => r.type));
    out.projectFiles.newCandidates = out.candidates.ownMotion.filter((c) => c.confidence !== "thấp" && !inFile.has(c.type) && !inFile.has(c.shortName) && !c.coveredByParent).map((c) => c.type);
    out.projectFiles.missingClasses = [...inFile].filter((t) => !classIndex.has(t.split(".").pop()) && !/^(UnityEngine|TMPro|DG)\./.test(t));
  }
  if (out.data.roleMap && out.projectFiles.rulesData) {
    out.projectFiles.applied = !!out.data.roleMap.projectRulesHash;
  }
  return out;
}
