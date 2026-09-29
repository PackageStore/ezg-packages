// Đọc tối thiểu file YAML của Unity (prefab, scene, asset) — đủ cho khảo sát: tên GameObject, component (class id / script
// GUID), cha con, anchor, prefab lồng, tham chiếu GUID trong field. Không phải parser YAML đầy đủ.

export const CLASS = {
  GameObject: 1,
  Transform: 4,
  Animator: 95,
  Animation: 111,
  MonoBehaviour: 114,
  Canvas: 223,
  CanvasGroup: 225,
  RectTransform: 224,
  PrefabInstance: 1001,
};

const HEADER = /^--- !u!(\d+) &(-?\d+)( stripped)?/;

/** Tách file thành các document: { classId, fileId, stripped, lines }. */
export function documents(text) {
  const docs = [];
  let cur = null;
  for (const line of text.split(/\r?\n/)) {
    const m = HEADER.exec(line);
    if (m) {
      cur = { classId: +m[1], fileId: m[2], stripped: !!m[3], lines: [] };
      docs.push(cur);
    } else if (cur) {
      cur.lines.push(line);
    }
  }
  return docs;
}

/** Giá trị vô hướng của khoá đầu tiên `key:` (thụt lề bất kỳ); null nếu không có. */
export function scalar(doc, key) {
  const re = new RegExp("^\\s*" + key + ":\\s?(.*)$");
  for (const line of doc.lines) {
    const m = re.exec(line);
    if (m) return unquote(m[1]);
  }
  return null;
}

/** {fileID, guid} của khoá tham chiếu `key: {fileID: …, guid: …}`. */
export function ref(doc, key) {
  const v = rawValue(doc, key);
  return v ? parseRef(v) : null;
}

function rawValue(doc, key) {
  const re = new RegExp("^\\s*" + key + ":\\s?(.*)$");
  for (const line of doc.lines) {
    const m = re.exec(line);
    if (m) return m[1];
  }
  return null;
}

export function parseRef(v) {
  const f = /fileID:\s*(-?\d+)/.exec(v);
  const g = /guid:\s*([0-9a-f]{32})/.exec(v);
  return f ? { fileID: f[1], guid: g ? g[1] : null } : null;
}

/** Mọi GUID được tham chiếu trong document (field trỏ tới asset khác). */
export function guidsIn(doc) {
  const out = [];
  for (const line of doc.lines) {
    const re = /guid:\s*([0-9a-f]{32})/g;
    let m;
    while ((m = re.exec(line))) out.push(m[1]);
  }
  return out;
}

/** Danh sách fileID con của khoá dạng list `key:\n  - {fileID: 1}` hoặc `- component: {fileID: 1}`. */
export function refList(doc, key) {
  const out = [];
  let inside = false;
  let indent = -1;
  for (const line of doc.lines) {
    if (!inside) {
      const m = new RegExp("^(\\s*)" + key + ":\\s*(\\[\\])?\\s*$").exec(line);
      if (m) {
        if (m[2]) return out;
        inside = true;
        indent = m[1].length;
      }
      continue;
    }
    const m = /^(\s*)- (?:\w+: )?\{fileID: (-?\d+)/.exec(line);
    if (m && m[1].length >= indent) {
      out.push(m[2]);
      continue;
    }
    if (/^\s*-/.test(line) && line.search(/\S/) >= indent) continue;
    break;
  }
  return out;
}

/** Vector2 dạng `{x: 0, y: 1}`. */
export function vec2(doc, key) {
  const v = rawValue(doc, key);
  if (!v) return null;
  const x = /x:\s*(-?[\d.e+-]+)/.exec(v);
  const y = /y:\s*(-?[\d.e+-]+)/.exec(v);
  return x && y ? { x: +x[1], y: +y[1] } : null;
}

/** Danh sách chuỗi của khoá `key:\n  - a\n  - b` (asset ScriptableObject). */
export function stringList(doc, key) {
  const out = [];
  let inside = false;
  let indent = -1;
  for (const line of doc.lines) {
    if (!inside) {
      const m = new RegExp("^(\\s*)" + key + ":\\s*(\\[\\])?\\s*$").exec(line);
      if (m) {
        if (m[2]) return out;
        inside = true;
        indent = m[1].length;
      }
      continue;
    }
    const m = /^(\s*)- (.*)$/.exec(line);
    if (m && m[1].length >= indent) {
      out.push(unquote(m[2]));
      continue;
    }
    break;
  }
  return out;
}

/** Chuỗi nháy kép đã đóng chưa (nháy cuối không bị escape). */
function closedQuote(text) {
  const t = text.trim();
  if (t.length < 2 || t[0] !== '"') return true;
  let slashes = 0;
  for (let i = t.length - 2; i >= 0 && t[i] === "\\"; i--) slashes++;
  return t[t.length - 1] === '"' && slashes % 2 === 0;
}

/**
 * Danh sách object của khoá `key:\n  - A: 1\n    B: 2` → [{A:"1", B:"2"}]. Giá trị vô hướng một dòng; chuỗi nháy kép Unity
 * bẻ sang nhiều dòng thì nối lại.
 */
export function objectList(doc, key) {
  const out = [];
  let inside = false;
  let indent = -1;
  let cur = null;
  let open = null; // chuỗi nháy kép chưa đóng: { obj, key, text }
  for (const line of doc.lines) {
    if (open) {
      open.text += " " + line.trim();
      if (closedQuote(open.text)) {
        open.obj[open.key] = unquote(open.text);
        open = null;
      }
      continue;
    }
    if (!inside) {
      const m = new RegExp("^(\\s*)" + key + ":\\s*(\\[\\])?\\s*$").exec(line);
      if (m) {
        if (m[2]) return out;
        inside = true;
        indent = m[1].length;
      }
      continue;
    }
    const start = /^(\s*)- (\w+):\s?(.*)$/.exec(line);
    const field = /^(\s+)(\w+):\s?(.*)$/.exec(line);
    let hit = null;
    if (start && start[1].length >= indent) {
      cur = {};
      out.push(cur);
      hit = start;
    } else if (field && cur && field[1].length > indent) {
      hit = field;
    }
    if (hit) {
      if (closedQuote(hit[3])) cur[hit[2]] = unquote(hit[3]);
      else open = { obj: cur, key: hit[2], text: hit[3].trim() };
      continue;
    }
    if (/^\s+\S/.test(line) && cur && line.search(/\S/) > indent) continue; // dòng tiếp của chuỗi dài / list con
    if (/^\s*$/.test(line)) continue;
    break;
  }
  return out;
}

export function unquote(v) {
  if (v == null) return v;
  let s = v.trim();
  if (s.length >= 2 && s[0] === "'" && s[s.length - 1] === "'") return s.slice(1, -1).replace(/''/g, "'");
  if (s.length >= 2 && s[0] === '"' && s[s.length - 1] === '"') {
    s = s.slice(1, -1);
    return s
      .replace(/\\u([0-9a-fA-F]{4})/g, (_, h) => String.fromCharCode(parseInt(h, 16)))
      .replace(/\\x([0-9a-fA-F]{2})/g, (_, h) => String.fromCharCode(parseInt(h, 16)))
      .replace(/\\"/g, '"')
      .replace(/\\\\/g, "\\");
  }
  return s;
}

/**
 * Cây GameObject của một prefab / scene: { objects: Map fileId → {name, components:[{classId, fileId, script}], rect,
 * parent, children}, roots, prefabInstances: [{sourceGuid}], scriptRefs: Map componentFileId → guids tham chiếu }.
 */
export function hierarchy(text) {
  const docs = documents(text);
  const byId = new Map(docs.map((d) => [d.fileId, d]));
  const objects = new Map();
  const transforms = new Map(); // transform fileId → gameObject fileId
  const prefabInstances = [];

  for (const d of docs) {
    if (d.classId === CLASS.GameObject && !d.stripped) {
      objects.set(d.fileId, { fileId: d.fileId, name: scalar(d, "m_Name") || "", components: [], parent: null, children: [], rect: null, active: scalar(d, "m_IsActive") !== "0" });
    } else if (d.classId === CLASS.PrefabInstance) {
      const src = ref(d, "m_SourcePrefab");
      if (src && src.guid) prefabInstances.push({ fileId: d.fileId, sourceGuid: src.guid });
    }
  }

  for (const d of docs) {
    if (d.stripped || d.classId === CLASS.GameObject || d.classId === CLASS.PrefabInstance) continue;
    const go = ref(d, "m_GameObject");
    if (!go) continue;
    const owner = objects.get(go.fileID);
    if (!owner) continue;
    const comp = { classId: d.classId, fileId: d.fileId, script: null, guids: [] };
    if (d.classId === CLASS.MonoBehaviour) {
      const s = ref(d, "m_Script");
      comp.script = s ? { guid: s.guid, fileID: s.fileID } : null;
      comp.guids = guidsIn(d).filter((g) => !s || g !== s.guid);
    }
    owner.components.push(comp);
    if (d.classId === CLASS.Transform || d.classId === CLASS.RectTransform) {
      transforms.set(d.fileId, go.fileID);
      if (d.classId === CLASS.RectTransform) {
        owner.rect = { anchorMin: vec2(d, "m_AnchorMin"), anchorMax: vec2(d, "m_AnchorMax") };
      }
      owner._father = ref(d, "m_Father");
    }
  }

  for (const o of objects.values()) {
    const f = o._father;
    const parentGo = f && f.fileID !== "0" ? transforms.get(f.fileID) : null;
    if (parentGo && objects.has(parentGo)) {
      o.parent = parentGo;
      objects.get(parentGo).children.push(o.fileId);
    }
    delete o._father;
  }

  const roots = [...objects.values()].filter((o) => !o.parent).map((o) => o.fileId);
  return { objects, roots, prefabInstances, byId };
}
