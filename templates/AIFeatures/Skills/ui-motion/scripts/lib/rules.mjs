// Kiểm file luật riêng của project (ProjectSettings/UIMotionProject.json) ngoài Unity — cùng luật với UIMotionProjectRules
// của module: khoá, kiểu, tên role. Để Claude sửa file xong biết ngay có lỗi, không phải chờ mở Unity.

// Tên role dự phòng khi không đọc được enum UIRole từ source module (module mới hơn có thể thêm role ở cuối).
export const FALLBACK_ROLES = ["Unknown", "None", "Screen", "Popup", "Backdrop", "Panel", "Button", "List", "ListItem", "Counter", "Bar", "Label", "Toggle", "Slider", "Tab", "Dropdown", "Scrollbar", "Sheet", "Toast", "Tooltip", "Card", "Title", "Icon", "Badge", "Timer", "Spinner", "DamageText", "Decoration", "ScreenFx", "InputField"];

const ROOT = ["schema", "project", "dataFolder", "componentRules", "componentRulesRemove", "keywords", "prefabWords", "settings", "notes"];
const COMPONENT = ["type", "role", "ownMotion", "note"];
const KEYWORDS = ["add", "remove"];
const KEYWORD = ["word", "role", "before"];
const PREFAB_WORDS = ["item", "template"];
const SETTINGS = ["scanFolders", "excludePathContains", "sfxSinkType"];

/** Trả { errors: [], warnings: [] }. `roles` = tên role hợp lệ. */
export function validateRules(text, roles = FALLBACK_ROLES) {
  const errors = [];
  const warnings = [];
  let root;
  try {
    root = JSON.parse(text);
  } catch (e) {
    errors.push("JSON lỗi: " + e.message);
    return { errors, warnings };
  }
  if (!root || typeof root !== "object" || Array.isArray(root)) {
    errors.push("Gốc file phải là một object { … }.");
    return { errors, warnings };
  }
  const roleSet = new Map(roles.map((r) => [r.toLowerCase(), r]));
  const unknown = (obj, at, known) => {
    for (const k of Object.keys(obj)) if (!known.includes(k)) warnings.push(`${at}: khoá lạ "${k}" (module bỏ qua) — khoá đọc được: ${known.join(", ")}`);
  };
  const str = (v, at, required) => {
    if (v == null) {
      if (required) errors.push(at + ": thiếu");
      return null;
    }
    if (typeof v !== "string") {
      errors.push(at + ": phải là chuỗi");
      return null;
    }
    return v;
  };
  const role = (v, at) => {
    const s = str(v, at, true);
    if (s == null) return;
    if (/^\d/.test(s.trim()) || !roleSet.has(s.trim().toLowerCase())) errors.push(`${at}: "${s}" không phải role — dùng: ${roles.join(", ")}`);
  };
  const strList = (v, at) => {
    if (v == null) return;
    if (!Array.isArray(v)) return errors.push(at + ": phải là danh sách chuỗi");
    v.forEach((x, i) => typeof x !== "string" && errors.push(`${at}[${i}]: phải là chuỗi`));
  };
  const objList = (v, at, visit) => {
    if (v == null) return;
    if (!Array.isArray(v)) return errors.push(at + ": phải là danh sách [ { … } ]");
    v.forEach((x, i) => (x && typeof x === "object" && !Array.isArray(x) ? visit(x, `${at}[${i}]`) : errors.push(`${at}[${i}]: phải là object`)));
  };

  unknown(root, "gốc", ROOT);
  if (root.schema != null && typeof root.schema !== "number") errors.push("schema: phải là số");
  if (root.schema > 1) warnings.push(`schema ${root.schema} mới hơn bản script này đọc (1)`);
  str(root.project, "project");
  if (root.dataFolder != null) {
    const d = str(root.dataFolder, "dataFolder");
    if (d && !/^Assets(\/|$)/.test(d.replace(/\\/g, "/"))) warnings.push(`dataFolder "${d}" không bắt đầu bằng Assets/ — module sẽ thêm Assets/ vào đầu`);
    if (d && /\\/.test(d)) warnings.push("dataFolder: dùng dấu / thay cho \\");
  }
  const types = new Set();
  objList(root.componentRules, "componentRules", (x, at) => {
    unknown(x, at, COMPONENT);
    const t = str(x.type, at + ".type", true);
    if (t) {
      if (types.has(t)) warnings.push(`${at}: class "${t}" lặp lại`);
      types.add(t);
    }
    role(x.role, at + ".role");
    if (x.ownMotion != null && typeof x.ownMotion !== "boolean") errors.push(at + ".ownMotion: phải là true / false");
    str(x.note, at + ".note");
  });
  strList(root.componentRulesRemove, "componentRulesRemove");
  if (root.keywords != null) {
    if (typeof root.keywords !== "object" || Array.isArray(root.keywords)) errors.push('keywords: phải là object { "add": [...], "remove": [...] }');
    else {
      unknown(root.keywords, "keywords", KEYWORDS);
      objList(root.keywords.add, "keywords.add", (x, at) => {
        unknown(x, at, KEYWORD);
        const w = str(x.word, at + ".word", true);
        if (w && /[^a-z0-9]/i.test(w)) warnings.push(`${at}.word "${w}": keyword là một từ (chữ / số), module so nguyên từ sau khi tách tên`);
        role(x.role, at + ".role");
        str(x.before, at + ".before");
      });
      strList(root.keywords.remove, "keywords.remove");
    }
  }
  if (root.prefabWords != null) {
    if (typeof root.prefabWords !== "object" || Array.isArray(root.prefabWords)) errors.push("prefabWords: phải là object");
    else {
      unknown(root.prefabWords, "prefabWords", PREFAB_WORDS);
      strList(root.prefabWords.item, "prefabWords.item");
      strList(root.prefabWords.template, "prefabWords.template");
    }
  }
  if (root.settings != null) {
    if (typeof root.settings !== "object" || Array.isArray(root.settings)) errors.push("settings: phải là object");
    else {
      unknown(root.settings, "settings", SETTINGS);
      strList(root.settings.scanFolders, "settings.scanFolders");
      strList(root.settings.excludePathContains, "settings.excludePathContains");
      str(root.settings.sfxSinkType, "settings.sfxSinkType");
    }
  }
  return { errors, warnings };
}

/** Đọc tên role từ enum UIRole trong source module (giữ đúng bản module của project). */
export function rolesFromSource(src) {
  if (!src) return null;
  const m = /enum\s+UIRole\s*\{([\s\S]*?)\}/.exec(src);
  if (!m) return null;
  const names = [];
  for (const part of m[1].split(",")) {
    const e = /(\w+)\s*=\s*\d+/.exec(part.replace(/\[[^\]]*\]/g, ""));
    if (e) names.push(e[1]);
  }
  return names.length ? names : null;
}
