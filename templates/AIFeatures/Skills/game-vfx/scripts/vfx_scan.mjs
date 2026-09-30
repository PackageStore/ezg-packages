// vfx_scan.mjs — quét VFX của một project Unity (asset dạng text), chỉ đọc, không ghi gì vào project.
// Dùng: node vfx_scan.mjs <project> <thư mục kết quả> [--packs "<đoạn đường dẫn folder pack>,..."]
// --packs: các đoạn đường dẫn (chữ thường) coi là folder pack. Ghi <kết quả>/effects.csv, systems.csv, materials.csv,
// textures.csv, summary.json. "Vào build" = đi được theo GUID từ scene build đang bật và folder Resources.
// Cách đọc số theo quy chuẩn: SKILL.md của skill game-vfx, mục 4. Tên shader built-in suy từ fileID (bảng BUILTIN).
import fs from 'fs';
import path from 'path';

if (!process.argv[2] || !process.argv[3]) {
  console.error('node vfx_scan.mjs <project> <thư mục kết quả> [--packs "<đoạn đường dẫn folder pack>,..."]');
  process.exit(1);
}
const root = path.resolve(process.argv[2]);
const outDir = path.resolve(process.argv[3]);
fs.mkdirSync(outDir, { recursive: true });
const rel = (p) => path.relative(root, p).split(path.sep).join('/');

// ---------- 1. file walk + guid index ----------
const SKIP_DIRS = new Set(['Library', 'Temp', 'Logs', 'obj', 'Build', 'Builds', 'UserSettings', '.git', '.vs', '.idea', 'ProfilerCaptures']);
const files = [];
function walk(dir) {
  let ents;
  try { ents = fs.readdirSync(dir, { withFileTypes: true }); } catch { return; }
  for (const e of ents) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) { if (!SKIP_DIRS.has(e.name) && !e.name.endsWith('~')) walk(p); }
    else files.push(p);
  }
}
walk(path.join(root, 'Assets'));
walk(path.join(root, 'Packages'));
// package cache: only shaders (for names of package shaders such as URP Particles/Unlit)
const pkgCache = path.join(root, 'Library', 'PackageCache');
const pkgShaderFiles = [];
(function walkPkg(dir, depth) {
  let ents; try { ents = fs.readdirSync(dir, { withFileTypes: true }); } catch { return; }
  for (const e of ents) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) { if (depth < 8) walkPkg(p, depth + 1); }
    else if (/\.(shader|shadergraph|meta)$/.test(e.name)) pkgShaderFiles.push(p);
  }
})(pkgCache, 0);

const guidToPath = new Map();
function indexMeta(metaPath) {
  try {
    const head = fs.readFileSync(metaPath, 'utf8').slice(0, 300);
    const m = head.match(/guid: ([0-9a-f]{32})/);
    if (m) guidToPath.set(m[1], metaPath.slice(0, -5));
  } catch { }
}
for (const f of files) if (f.endsWith('.meta')) indexMeta(f);
for (const f of pkgShaderFiles) if (f.endsWith('.meta')) indexMeta(f);

// ---------- 2. roots: enabled build scenes + everything under Resources/ ----------
const YAML_EXT = /\.(unity|prefab|asset|mat|controller|overrideController|anim|playable|spriteatlas|spriteatlasv2|mask|preset|lighting|renderTexture|flare|guiskin|physicMaterial|shadervariants|terrainlayer|brush|signal)$/;
const buildScenes = [];
try {
  const ebs = fs.readFileSync(path.join(root, 'ProjectSettings', 'EditorBuildSettings.asset'), 'utf8');
  const re = /- enabled: (\d)\s+path: ([^\r\n]+)\s+guid: ([0-9a-f]{32})/g; let m;
  while ((m = re.exec(ebs))) buildScenes.push({ enabled: m[1] === '1', path: m[2].trim(), guid: m[3] });
} catch { }
const roots = [];
for (const s of buildScenes) if (s.enabled) roots.push(path.join(root, s.path));
for (const f of files) if (!f.endsWith('.meta') && /[\\/]Resources[\\/]/.test(f) && !/[\\/]Editor[\\/]/.test(f)) roots.push(f);

// reference graph over YAML assets (lazy)
const refCache = new Map();
function refsOf(p) {
  if (refCache.has(p)) return refCache.get(p);
  let out = [];
  if (YAML_EXT.test(p)) {
    try {
      const txt = fs.readFileSync(p, 'utf8');
      const set = new Set(); const re = /guid: ([0-9a-f]{32})/g; let m;
      while ((m = re.exec(txt))) set.add(m[1]);
      out = [...set];
    } catch { }
  }
  refCache.set(p, out);
  return out;
}
const shipped = new Set();
const queue = [...new Set(roots)];
for (const q of queue) shipped.add(q);
while (queue.length) {
  const p = queue.pop();
  for (const g of refsOf(p)) {
    const t = guidToPath.get(g);
    if (t && !shipped.has(t)) { shipped.add(t); queue.push(t); }
  }
}

// ---------- 3. mini Unity YAML parser ----------
function parseScalar(v) {
  v = v.trim();
  if (/^-?\d{16,}$/.test(v)) return v; // 64-bit fileIDs lose precision as Number
  if (/^-?\d+(\.\d+)?(e[-+]?\d+)?$/i.test(v)) return Number(v);
  if (v.startsWith("'") && v.endsWith("'")) return v.slice(1, -1);
  return v;
}
function parseFlow(v) {
  v = v.trim();
  if (!v.startsWith('{')) return parseScalar(v);
  const o = {};
  const body = v.replace(/^\{/, '').replace(/\}$/, '');
  for (const part of body.split(/,\s*/)) { const i = part.indexOf(':'); if (i > 0) o[part.slice(0, i).trim()] = parseScalar(part.slice(i + 1)); }
  return o;
}
function parseYaml(text) {
  const rootObj = {};
  const stack = [{ indent: 0, kind: 'map', obj: rootObj }];
  let pending = null;
  for (let raw of text.split('\n')) {
    if (raw.endsWith('\r')) raw = raw.slice(0, -1);
    const t = raw.trimStart();
    if (!t) continue;
    const c = raw.length - t.length;
    const isItem = t === '-' || t.startsWith('- ');
    if (pending) {
      if (isItem && c >= pending.indent) { const a = []; pending.obj[pending.key] = a; stack.push({ indent: c, kind: 'seq', obj: a }); }
      else if (!isItem && c > pending.indent) { const o = {}; pending.obj[pending.key] = o; stack.push({ indent: c, kind: 'map', obj: o }); }
      else pending.obj[pending.key] = '';
      pending = null;
    }
    while (stack.length > 1) {
      const top = stack[stack.length - 1];
      if (top.kind === 'map' && c < top.indent) { stack.pop(); continue; }
      if (top.kind === 'seq' && (c < top.indent || (c === top.indent && !isItem))) { stack.pop(); continue; }
      break;
    }
    let top = stack[stack.length - 1];
    let content = t, col = c;
    if (isItem) {
      if (top.kind !== 'seq') continue;
      const rest = t === '-' ? '' : t.slice(2);
      if (rest.startsWith('{')) { top.obj.push(parseFlow(rest)); continue; }
      if (!/^[^:{}]+?:(\s|$)/.test(rest)) { top.obj.push(parseScalar(rest)); continue; }
      const o = {}; top.obj.push(o); stack.push({ indent: c + 2, kind: 'map', obj: o });
      top = stack[stack.length - 1]; content = rest; col = c + 2;
    }
    const m = content.match(/^([^:]+?):(?:\s(.*))?$/);
    if (!m) continue;
    const key = m[1], val = m[2];
    if (val === undefined || val === '') pending = { obj: top.obj, key, indent: col };
    else top.obj[key] = val.startsWith('{') ? parseFlow(val) : parseScalar(val);
  }
  if (pending) pending.obj[pending.key] = '';
  return rootObj;
}
function splitDocs(text) {
  const docs = [];
  const re = /^--- !u!(\d+) &(-?\d+)( stripped)?\r?$/gm; let m, last = null;
  while ((m = re.exec(text))) {
    if (last) docs.push({ ...last, body: text.slice(last.start, m.index) });
    last = { cls: Number(m[1]), id: m[2], stripped: !!m[3], start: re.lastIndex };
  }
  if (last) docs.push({ ...last, body: text.slice(last.start) });
  return docs;
}

// ---------- 4. helpers for curves / colors ----------
const num = (x, d = 0) => (typeof x === 'number' && isFinite(x) ? x : d);
function curveMax(mm) { // MinMaxCurve -> max value (approx)
  if (!mm || typeof mm !== 'object') return 0;
  const st = num(mm.minMaxState), s = num(mm.scalar);
  if (st === 0) return s;
  if (st === 3) return Math.max(s, num(mm.minScalar));
  const keys = (mm.maxCurve && mm.maxCurve.m_Curve) || [];
  let k = 0; for (const kf of keys) k = Math.max(k, Math.abs(num(kf.value)));
  if (st === 2) { const k2 = (mm.minCurve && mm.minCurve.m_Curve) || []; for (const kf of k2) k = Math.max(k, Math.abs(num(kf.value))); }
  return s * (keys.length ? k : 1);
}
function curveMin(mm) {
  if (!mm || typeof mm !== 'object') return 0;
  const st = num(mm.minMaxState), s = num(mm.scalar);
  if (st === 0) return s;
  if (st === 3) return Math.min(s, num(mm.minScalar));
  return curveMax(mm);
}
function hex(c) {
  if (!c || typeof c !== 'object') return '';
  const h = (v) => Math.round(Math.max(0, Math.min(1, num(v))) * 255).toString(16).padStart(2, '0');
  return '#' + h(c.r) + h(c.g) + h(c.b);
}
function hsv(c) {
  const r = num(c.r), g = num(c.g), b = num(c.b);
  const mx = Math.max(r, g, b), mn = Math.min(r, g, b);
  return { s: mx > 0 ? (mx - mn) / mx : 0, v: mx };
}
function gradientKeys(gr) {
  if (!gr || typeof gr !== 'object') return [];
  const n = num(gr.m_NumColorKeys, 0), out = [];
  for (let i = 0; i < Math.min(8, n); i++) if (gr['key' + i]) out.push(gr['key' + i]);
  return out;
}
function gradientAlphaMax(gr) {
  if (!gr || typeof gr !== 'object') return 1;
  const n = num(gr.m_NumAlphaKeys, 0); let a = 0;
  for (let i = 0; i < Math.min(8, n); i++) if (gr['key' + i]) a = Math.max(a, num(gr['key' + i].a));
  return n ? a : 1;
}
function mmgColors(mmg) { // MinMaxGradient -> list of colors
  if (!mmg || typeof mmg !== 'object') return [];
  const st = num(mmg.minMaxState);
  if (st === 0) return [mmg.maxColor];
  if (st === 2) return [mmg.minColor, mmg.maxColor];
  if (st === 1 || st === 4) return gradientKeys(mmg.maxGradient);
  if (st === 3) return [...gradientKeys(mmg.minGradient), ...gradientKeys(mmg.maxGradient)];
  return [];
}

// ---------- 5. materials, shaders, textures ----------
const BUILTIN = { 10720: ['Mobile/Particles/Additive', 'fixed:additive'], 10721: ['Mobile/Particles/Alpha Blended', 'fixed:alpha'], 10722: ['Mobile/Particles/VertexLit Blended', 'fixed:alpha'], 10723: ['Mobile/Particles/Multiply', 'fixed:multiply'], 200: ['Legacy Shaders/Particles/Additive', 'fixed:additive'], 202: ['Legacy Shaders/Particles/Additive (Soft)?', 'fixed:additive'], 203: ['Legacy Shaders/Particles/Alpha Blended', 'fixed:alpha'], 208: ['builtin:208', ''], 210: ['Particles/Standard Surface', 'floats'], 211: ['Particles/Standard Unlit', 'floats'], 10770: ['UI/Default', 'fixed:alpha'], 10753: ['Sprites/Default', 'fixed:alpha'], 46: ['Standard', 'floats'], 10755: ['Sprites/Mask', 'fixed:alpha'] };
const shaderInfoCache = new Map();
function shaderInfo(ref) {
  if (!ref || typeof ref !== 'object') return { name: '(none)', blendHint: '', costly: [] };
  if (ref.guid === '0000000000000000f000000000000000' || ref.guid === '0000000000000000e000000000000000') { const b = BUILTIN[ref.fileID]; return b ? { name: b[0], blendHint: b[1], costly: [], builtin: true } : { name: 'builtin:' + ref.fileID, blendHint: '', costly: [], builtin: true }; }
  const key = ref.guid;
  if (shaderInfoCache.has(key)) return shaderInfoCache.get(key);
  const p = guidToPath.get(key);
  let info = { name: p ? '(file ' + path.basename(p) + ')' : '(missing ' + key.slice(0, 8) + ')', blendHint: '', costly: [], file: p ? rel(p) : '' };
  if (p) {
    try {
      const txt = fs.readFileSync(p, 'utf8');
      if (p.endsWith('.shader')) {
        const m = txt.match(/Shader\s+"([^"]+)"/); if (m) info.name = m[1];
        const blends = [...txt.matchAll(/^\s*Blend\s+([^\r\n]+)/gm)].map((x) => x[1].trim());
        info.blendHint = [...new Set(blends)].slice(0, 3).join(' / ');
        if (/GrabPass/.test(txt)) info.costly.push('GrabPass');
        if (/_CameraOpaqueTexture|SampleSceneColor/.test(txt)) info.costly.push('OpaqueTexture');
        if (/multi_compile_particles|SOFTPARTICLES_ON/.test(txt)) info.softVariant = true; if (/_CameraDepthTexture|SampleSceneDepth/.test(txt) && !/SOFTPARTICLES_ON|_SOFTPARTICLES_ON|_FADING_ON/.test(txt)) info.costly.push('Depth');
        
        info.texSamples = (txt.match(/tex2D\s*\(|SAMPLE_TEXTURE2D\s*\(/g) || []).length;
      } else if (p.endsWith('.shadergraph')) {
        info.name = 'SG:' + path.basename(p, '.shadergraph');
        if (/SceneColor/.test(txt)) info.costly.push('OpaqueTexture');
        if (/SceneDepth/.test(txt)) info.costly.push('Depth/Soft');
        const surf = txt.match(/"m_SurfaceType":\s*(\d)/); const am = txt.match(/"m_AlphaMode":\s*(\d)/);
        if (surf) info.blendHint = 'SG surface=' + surf[1] + (am ? ' alphaMode=' + am[1] : '');
        info.texSamples = (txt.match(/SampleTexture2DNode/g) || []).length;
      }
    } catch { }
  }
  shaderInfoCache.set(key, info);
  return info;
}
const BLEND = { 0: 'Zero', 1: 'One', 2: 'DstColor', 3: 'SrcColor', 4: 'OneMinusDstColor', 5: 'SrcAlpha', 6: 'OneMinusSrcColor', 7: 'DstAlpha', 8: 'OneMinusDstAlpha', 9: 'SrcAlphaSaturate', 10: 'OneMinusSrcAlpha' };
function classifyBlend(src, dst, hint, shaderName, floats) {
  const s = (x) => String(x).toLowerCase();
  if (String(hint).startsWith('fixed:')) return hint.slice(6);
  const propDriven = hint === 'floats' || /\[/.test(String(hint)) || /universal render pipeline|shader graphs|^SG:/i.test(shaderName);
  if (!propDriven && hint && !/^SG surface/.test(hint)) { const h = s(hint); if (/^srcalpha one(\s|$)|^one one(\s|$)/.test(h)) return 'additive'; if (/^srcalpha oneminussrcalpha/.test(h)) return 'alpha'; if (/^one oneminussrcalpha/.test(h)) return 'premultiplied'; if (/^dstcolor zero|^zero srccolor/.test(h)) return 'multiply'; }
  if (propDriven && src !== undefined && dst !== undefined) {
    if (dst === 1) return 'additive';
    if (src === 5 && dst === 10) return 'alpha';
    if (src === 1 && dst === 10) return 'premultiplied';
    if ((src === 2 && dst === 0) || (src === 0 && dst === 3)) return 'multiply';
    if (src === 1 && dst === 0) return 'opaque';
    return 'blend(' + (BLEND[src] || src) + ',' + (BLEND[dst] || dst) + ')';
  }
  if (floats && floats._Surface === 0 && /universal|urp/i.test(shaderName)) return 'opaque';
  if (floats && floats._Blend !== undefined && /universal render pipeline\/particles|urp/i.test(shaderName)) return ['alpha', 'premultiplied', 'additive', 'multiply'][floats._Blend] || 'blend?';
  const h = s(hint) + ' ' + s(shaderName);
  if (/srcalpha one(\s|$)|one one(\s|$)|additive|(^|[^a-z])add([^a-z]|$)/.test(h)) return 'additive';
  if (/srcalpha oneminussrcalpha|alpha blended|alphablend|(^|[^a-z])(alpha|blend|ab)([^a-z]|$)/.test(h)) return 'alpha';
  if (/one oneminussrcalpha|premultipl/.test(h)) return 'premultiplied';
  if (/dstcolor zero|multiply/.test(h)) return 'multiply';
  return 'unknown';
}
const matCache = new Map();
function material(guid) {
  if (matCache.has(guid)) return matCache.get(guid);
  const p = guidToPath.get(guid);
  let info = { guid, path: p ? rel(p) : '(missing)', shader: '(missing)', blend: 'unknown', textures: [], costly: [], floats: {} };
  if (p && p.endsWith('.mat')) {
    try {
      const docs = splitDocs(fs.readFileSync(p, 'utf8')); const md = docs.find((d) => d.cls === 21) || docs[0]; const doc = parseYaml(md.body);
      const mat = doc.Material || {};
      const sh = shaderInfo(mat.m_Shader);
      info.shader = sh.name; info.costly = [...sh.costly]; info.shaderFile = sh.file || '';
      const props = mat.m_SavedProperties || {};
      const floats = {};
      for (const f of props.m_Floats || []) for (const [k, v] of Object.entries(f)) floats[k] = v;
      info.floats = floats;
      info.blend = classifyBlend(floats._SrcBlend, floats._DstBlend, sh.blendHint, sh.name, floats);
      for (const t of props.m_TexEnvs || []) for (const [k, v] of Object.entries(t)) {
        const g = v && v.m_Texture && v.m_Texture.guid; if (g) info.textures.push({ slot: k, guid: g });
      }
      const kw = mat.m_ValidKeywords || mat.m_ShaderKeywords || '';
      info.keywords = Array.isArray(kw) ? kw.join(' ') : String(kw);
      if (/SOFTPARTICLES_ON|_FADING_ON/.test(info.keywords) && (sh.name === 'Particles/Standard Unlit' || /universal render pipeline\/particles/i.test(sh.name) || sh.softVariant)) info.costly.push('SoftParticles(material)');
      else if (sh.softVariant) info.costly.push('SoftParticles(quality)');
      if (/_DISTORTION_ON|distort/i.test(info.keywords + ' ' + sh.name)) info.costly.push('Distortion');
    } catch (e) { info.shader = '(parse error)'; }
  }
  matCache.set(guid, info);
  return info;
}
function imageSize(p) {
  try {
    const fd = fs.openSync(p, 'r'); const b = Buffer.alloc(64 * 1024); const n = fs.readSync(fd, b, 0, b.length, 0); fs.closeSync(fd);
    const ext = path.extname(p).toLowerCase();
    if (n > 24 && b[0] === 0x89 && b[1] === 0x50 && b[2] === 0x4e && b[3] === 0x47) return [b.readUInt32BE(16), b.readUInt32BE(20)];
    if (n > 26 && b.toString('ascii', 0, 4) === '8BPS') return [b.readUInt32BE(18), b.readUInt32BE(14)];
    if (ext === '.psd' || ext === '.png') return null;
    if (ext === '.tga' && n > 18) return [b.readUInt16LE(12), b.readUInt16LE(14)];
    if (ext === '.jpg' || ext === '.jpeg') {
      let i = 2; while (i < n - 9) { if (b[i] !== 0xff) { i++; continue; } const mk = b[i + 1]; const len = b.readUInt16BE(i + 2); if (mk >= 0xc0 && mk <= 0xc3) return [b.readUInt16BE(i + 7), b.readUInt16BE(i + 5)]; i += 2 + len; }
    }
  } catch { }
  return null;
}
const texCache = new Map();
function texture(guid) {
  if (texCache.has(guid)) return texCache.get(guid);
  const p = guidToPath.get(guid);
  const info = { guid, path: p ? rel(p) : '(missing)', w: 0, h: 0, maxSize: 0, mip: '', type: '', android: '', ios: '', effAndroid: 0 };
  if (p) {
    const sz = imageSize(p); if (sz) { info.w = sz[0]; info.h = sz[1]; }
    try {
      const meta = fs.readFileSync(p + '.meta', 'utf8');
      const g = (re) => { const m = meta.match(re); return m ? m[1] : ''; };
      info.maxSize = Number(g(/^\s{2}maxTextureSize: (\d+)/m)) || 0;
      info.mip = g(/enableMipMap: (\d)/);
      info.type = g(/textureType: (-?\d+)/);
      info.spriteMode = g(/spriteMode: (\d)/);
      const plat = [...meta.matchAll(/- serializedVersion: \d+\s+buildTarget: (\w+)\s+maxTextureSize: (\d+)[\s\S]*?textureFormat: (-?\d+)[\s\S]*?overridden: (\d)/g)];
      for (const m of plat) {
        if (m[1] === 'Android') info.android = m[4] === '1' ? m[2] + '/fmt' + m[3] : '';
        if (m[1] === 'iPhone') info.ios = m[4] === '1' ? m[2] + '/fmt' + m[3] : '';
      }
      const dim = Math.max(info.w, info.h);
      let cap = info.maxSize || 2048;
      if (info.android) cap = Number(info.android.split('/')[0]);
      info.effAndroid = dim ? Math.min(dim, cap) : cap;
    } catch { }
  }
  texCache.set(guid, info);
  return info;
}

// ---------- 6. scan prefabs ----------
const prefabFiles = files.filter((f) => f.endsWith('.prefab'));
const scriptName = (g) => { const p = guidToPath.get(g); return p ? path.basename(p).replace(/\.cs$/, '') : '(missing)'; };
const prefabCache = new Map(); // path -> parsed info (own components + nested instances)
function scanPrefab(p, depth = 0) {
  if (prefabCache.has(p)) return prefabCache.get(p);
  const info = { systems: [], trails: [], lines: [], spriteRenderers: 0, animators: 0, lights: 0, scripts: new Set(), nested: [], names: new Map(), rootName: '' };
  prefabCache.set(p, info);
  let txt; try { txt = fs.readFileSync(p, 'utf8'); } catch { return info; }
  if (!/ParticleSystem:|TrailRenderer:|LineRenderer:|PrefabInstance:|SpriteRenderer:/.test(txt)) return info;
  const docs = splitDocs(txt);
  const renderersByGo = new Map();
  for (const d of docs) {
    if (d.stripped) continue;
    if (d.cls === 1) { const m = d.body.match(/m_Name: ([^\r\n]*)/); if (m) info.names.set(d.id, m[1].trim()); }
    else if (d.cls === 199) { const o = parseYaml(d.body).ParticleSystemRenderer || {}; renderersByGo.set((o.m_GameObject || {}).fileID + '', o); }
    else if (d.cls === 96) { const o = parseYaml(d.body).TrailRenderer || {}; info.trails.push({ time: num(o.m_Time), mats: (o.m_Materials || []).map((x) => x && x.guid).filter(Boolean) }); }
    else if (d.cls === 120) { const o = parseYaml(d.body).LineRenderer || {}; info.lines.push({ mats: (o.m_Materials || []).map((x) => x && x.guid).filter(Boolean) }); }
    else if (d.cls === 212) { info.spriteRenderers++; const m = d.body.match(/m_Sprite: \{fileID: \d+, guid: ([0-9a-f]{32})/); if (m) (info.srTex = info.srTex || new Set()).add(m[1]); }
    else if (d.cls === 95) info.animators++;
    else if (d.cls === 108) info.lights++;
    else if (d.cls === 114) { const m = d.body.match(/m_Script: \{fileID: 11500000, guid: ([0-9a-f]{32})/); if (m) info.scripts.add(scriptName(m[1])); }
    else if (d.cls === 1001) { const m = d.body.match(/m_SourcePrefab: \{fileID: \d+, guid: ([0-9a-f]{32})/); if (m) { const np = guidToPath.get(m[1]); if (np && np.endsWith('.prefab')) info.nested.push(np); } }
  }
  for (const d of docs) {
    if (d.cls !== 198 || d.stripped) continue;
    const ps = parseYaml(d.body).ParticleSystem || {};
    const go = (ps.m_GameObject || {}).fileID + '';
    const r = renderersByGo.get(go) || {};
    const im = ps.InitialModule || {}, em = ps.EmissionModule || {};
    const en = (mod) => !!(ps[mod] && num(ps[mod].enabled) === 1);
    const bursts = Array.isArray(em.m_Bursts) ? em.m_Bursts : [];
    let burstTotal = 0;
    for (const b of bursts) { const cnt = curveMax(b.countCurve) || num(b.minCount) || num(b.maxCount); const cyc = Math.max(1, num(b.cycleCount, 1)); burstTotal += cnt * cyc * Math.min(1, num(b.probability, 1) || 1); }
    const rate = en('EmissionModule') ? curveMax(em.rateOverTime) : 0;
    const rateDist = en('EmissionModule') ? curveMax(em.rateOverDistance) : 0;
    const life = curveMax(im.startLifetime), lifeMin = curveMin(im.startLifetime);
    const dur = num(ps.lengthInSec);
    const looping = num(ps.looping) === 1;
    const maxP = num(im.maxNumParticles, 1000);
    let est = rate * Math.min(looping ? life : dur, life) + (en('EmissionModule') ? burstTotal : 0);
    est = Math.min(maxP, Math.round(est));
    const startCols = mmgColors(im.startColor);
    const colMod = en('ColorModule') ? mmgColors((ps.ColorModule || {}).gradient) : [];
    const uv = ps.UVModule || {};
    const sub = ps.SubModule || {};
    const mats = (r.m_Materials || []).map((x) => x && x.guid).filter(Boolean);
    const sheetSprites = en('UVModule') && num(uv.mode) === 1 && Array.isArray(uv.sprites) ? uv.sprites.map((x) => x && x.sprite && x.sprite.guid).filter(Boolean) : [];
    info.systems.push({ sheetFrames: sheetSprites.length, sheetTex: [...new Set(sheetSprites)],
      name: info.names.get(go) || '?',
      duration: dur, looping, prewarm: num(ps.prewarm) === 1, playOnAwake: num(ps.playOnAwake) === 1,
      stopAction: num(ps.stopAction), cullingMode: num(ps.cullingMode), scalingMode: num(ps.scalingMode),
      simSpace: num(ps.moveWithTransform), unscaled: num(ps.useUnscaledTime) === 1, startDelay: curveMax(ps.startDelay || im.startDelay),
      lifeMax: life, lifeMin, maxParticles: maxP, rate, rateDist, bursts: bursts.length, burstTotal: Math.round(burstTotal), est,
      sizeMax: curveMax(im.startSize), speedMax: curveMax(im.startSpeed), gravity: curveMax(im.gravityModifier),
      noise: en('NoiseModule'), noiseQuality: num((ps.NoiseModule || {}).quality), collision: en('CollisionModule'), collisionType: num((ps.CollisionModule || {}).type),
      trigger: en('TriggerModule'), subEmitters: en('SubModule') ? (Array.isArray(sub.subEmitters) ? sub.subEmitters.length : 1) : 0,
      lightsMod: en('LightsModule'), trailsMod: en('TrailModule'), externalForces: en('ExternalForcesModule'), clampVel: en('ClampVelocityModule'),
      sheet: en('UVModule') ? (num(uv.mode) === 1 ? 'sprites' : num(uv.tilesX, 1) + 'x' + num(uv.tilesY, 1)) : '',
      shape: en('ShapeModule') ? num((ps.ShapeModule || {}).type) : -1,
      colorOverLife: en('ColorModule'), sizeOverLife: en('SizeModule'), rotOverLife: en('RotationModule'), velOverLife: en('VelocityModule'),
      startColors: startCols.map(hex), startAlphaMax: Math.max(0, ...startCols.map((c) => num(c && c.a, 1))),
      startSV: startCols.map((c) => hsv(c || {})), colMod: colMod.map(hex),
      renderMode: r.m_RenderMode === undefined ? -1 : num(r.m_RenderMode), rendererEnabled: num(r.m_Enabled, 1) === 1,
      sortingLayerID: r.m_SortingLayerID === undefined ? '' : String(r.m_SortingLayerID), sortingOrder: num(r.m_SortingOrder), sortMode: num(r.m_SortMode),
      maxParticleSize: num(r.m_MaxParticleSize, 0.5), gpuInstancing: num(r.m_EnableGPUInstancing) === 1, mats,
      customVertexStreams: num(r.m_UseCustomVertexStreams) === 1, meshRef: r.m_Mesh && r.m_Mesh.guid ? r.m_Mesh.guid : '',
    });
  }
  return info;
}
function collect(p, seen = new Set()) { // flatten own + nested prefabs
  if (seen.has(p)) return { systems: [], trails: [], lines: [], spriteRenderers: 0, animators: 0, lights: 0, scripts: new Set(), nestedCount: 0 };
  seen.add(p);
  const own = scanPrefab(p);
  const acc = { systems: [...own.systems], trails: [...own.trails], lines: [...own.lines], spriteRenderers: own.spriteRenderers, animators: own.animators, lights: own.lights, scripts: new Set(own.scripts), nestedCount: own.nested.length };
  for (const np of own.nested) {
    const c = collect(np, seen);
    acc.systems.push(...c.systems); acc.trails.push(...c.trails); acc.lines.push(...c.lines);
    acc.spriteRenderers += c.spriteRenderers; acc.animators += c.animators; acc.lights += c.lights;
    for (const s of c.scripts) acc.scripts.add(s);
  }
  return acc;
}

// pack detection
const packArg = process.argv.indexOf('--packs');
const packHints = (packArg > 0 ? process.argv[packArg + 1].split(',') : []).map((s) => s.trim().toLowerCase()).filter(Boolean);
function isPack(rp) {
  const l = rp.toLowerCase();
  if (packHints.some((h) => l.includes(h))) return true;
  return /(^|\/)(plugins|thirdpart(y|ies)|samples|asset ?store|packages?\/(?!_project)|demo|examples?)\//.test(l) && !/\/_project\//.test(l);
}
const CATS = [
  ['telegraph', /telegraph|warning|indicator|alert|danger|aoe_?range|range_?circle|target_?circle|marker/],
  ['projectile', /projectile|bullet|missile|arrow|shot|\bproj|fireball|orb_?fly|bolt|trail|lazer|laser|beam/],
  ['hit', /hit|impact|damage|slash|strike|blood|crit|explo|boom|blast/],
  ['cast', /cast|charge|muzzle|spawn_?skill|skill|ability|spell|ulti|atk|attack/],
  ['buff', /buff|debuff|heal|shield|stun|freeze|poison|burn|slow|aura|regen|power_?up|rage|status|effect\//],
  ['death', /death|die|dead|destroy|dissolve|despawn/],
  ['spawn', /spawn|appear|summon|portal|teleport|revive|level_?up|upgrade|evolve/],
  ['pickup', /coin|gem|gold|pickup|collect|loot|drop|chest|reward|exp|xp/],
  ['ui', /(^|\/)ui|canvas|popup|button|panel|screen|reward|gacha|shop|card/],
  ['env', /env|ambient|weather|rain|snow|fog|firefly|torch|water|grass|leaf|smoke_?loop|campfire/],
];
function category(rp) { const parts = rp.toLowerCase().replace(/\.prefab$/, '').split('/').filter((x) => !/^(assets|_project|resources|gameplay|visual|artasset|prefabs?|features|core)$/.test(x)); const l = parts.slice(-3).join('/'); for (const [c, re] of CATS) if (re.test(l)) return c; return 'other'; }

const sortingLayers = new Map();
try {
  const tm = fs.readFileSync(path.join(root, 'ProjectSettings', 'TagManager.asset'), 'utf8');
  const re = /- name: ([^\r\n]*)\s+uniqueID: (\d+)/g; let m;
  while ((m = re.exec(tm))) sortingLayers.set(m[2], m[1].trim() || '(empty)');
  sortingLayers.set('0', 'Default');
} catch { }

const effects = [];
for (const p of prefabFiles) {
  const own = scanPrefab(p);
  if (!own.systems.length && !own.trails.length && !own.lines.length) continue; // VFX prefab = owns a PS / trail / line
  const c = collect(p);
  const rp = rel(p);
  const sys = c.systems;
  const matGuids = new Set(); for (const s of sys) s.mats.forEach((g) => matGuids.add(g)); for (const t of c.trails) t.mats.forEach((g) => matGuids.add(g)); for (const l of c.lines) l.mats.forEach((g) => matGuids.add(g));
  const mats = [...matGuids].map(material);
  const texGuids = new Set(); for (const m of mats) for (const t of m.textures) texGuids.add(t.guid);
  for (const sy of sys) for (const g of sy.sheetTex || []) texGuids.add(g);
  for (const g of (scanPrefab(p).srTex || [])) texGuids.add(g);
  const texs = [...texGuids].map(texture);
  const maxTex = Math.max(0, ...texs.map((t) => t.effAndroid || Math.max(t.w, t.h)));
  const texPixels = texs.reduce((a, t) => a + (t.w && t.h ? Math.min(t.w, t.effAndroid || t.w) * Math.min(t.h, t.effAndroid || t.h) : 0), 0);
  const blends = mats.length ? [...new Set(mats.map((m) => m.blend))] : ['(no material)'];
  const shaders = mats.length ? [...new Set(mats.map((m) => m.shader))] : ['(no material)'];
  const costly = [...new Set(mats.flatMap((m) => m.costly))];
  const layers = [...new Set(sys.map((s) => sortingLayers.get(s.sortingLayerID) || s.sortingLayerID))];
  const lifeMax = Math.max(0, ...sys.map((s) => s.lifeMax));
  const durMax = Math.max(0, ...sys.map((s) => s.duration + s.startDelay));
  const totalTime = Math.max(0, ...sys.map((s) => (s.looping ? Infinity : s.startDelay + s.duration + s.lifeMax)));
  effects.push({
    path: rp, name: path.basename(p, '.prefab'), shipped: shipped.has(p), pack: isPack(rp), category: category(rp),
    systems: sys.length, nested: c.nestedCount, sumMaxParticles: sys.reduce((a, s) => a + s.maxParticles, 0), estPeak: sys.reduce((a, s) => a + s.est, 0),
    lifeMax: +lifeMax.toFixed(3), durMax: +durMax.toFixed(3), totalTime: totalTime === Infinity ? 'loop' : +totalTime.toFixed(3),
    looping: sys.filter((s) => s.looping).length, prewarm: sys.filter((s) => s.prewarm).length,
    stopActions: [...new Set(sys.map((s) => s.stopAction))].join('|'), culling: [...new Set(sys.map((s) => s.cullingMode))].join('|'),
    scaling: [...new Set(sys.map((s) => s.scalingMode))].join('|'), simSpace: [...new Set(sys.map((s) => s.simSpace))].join('|'), unscaled: sys.filter((s) => s.unscaled).length,
    noise: sys.filter((s) => s.noise).length, collision: sys.filter((s) => s.collision).length, subEmitters: sys.reduce((a, s) => a + s.subEmitters, 0),
    lightsMod: sys.filter((s) => s.lightsMod).length, trailsMod: sys.filter((s) => s.trailsMod).length, sheets: sys.filter((s) => s.sheet).length,
    renderModes: [...new Set(sys.map((s) => s.renderMode))].join('|'), meshParticles: sys.filter((s) => s.renderMode === 4).length,
    trailRenderers: c.trails.length, lineRenderers: c.lines.length, spriteRenderers: c.spriteRenderers, animators: c.animators, lightComponents: c.lights,
    materials: mats.length, shaders: shaders.join('|'), blends: blends.join('|'), costly: costly.join('|'), textures: texs.length, maxTex, texMPix: +(texPixels / 1e6).toFixed(3),
    layers: layers.join('|'), orders: [...new Set(sys.map((s) => s.sortingOrder))].slice(0, 6).join('|'),
    scripts: [...c.scripts].join('|'),
    _sys: sys, _mats: mats.map((m) => m.guid), _texs: texs.map((t) => t.guid),
  });
}

// ---------- 7. outputs ----------
const csvEsc = (v) => { const s = String(v ?? ''); return /[",\n]/.test(s) ? '"' + s.replace(/"/g, '""') + '"' : s; };
function writeCsv(name, rows, cols) {
  fs.writeFileSync(path.join(outDir, name), [cols.join(','), ...rows.map((r) => cols.map((c) => csvEsc(r[c])).join(','))].join('\n'));
}
const effCols = Object.keys(effects[0] || {}).filter((k) => !k.startsWith('_'));
writeCsv('effects.csv', effects, effCols);
const sysRows = [];
for (const e of effects) for (const s of e._sys) sysRows.push({ effect: e.path, shipped: e.shipped, pack: e.pack, ...s, startColors: s.startColors.join(' '), colMod: s.colMod.join(' '), startSV: s.startSV.map((x) => x.s.toFixed(2) + '/' + x.v.toFixed(2)).join(' '), mats: s.mats.length, layer: sortingLayers.get(s.sortingLayerID) || s.sortingLayerID });
writeCsv('systems.csv', sysRows, Object.keys(sysRows[0] || { effect: 1 }));
const matUse = new Map(), texUse = new Map();
for (const e of effects) { for (const g of e._mats) matUse.set(g, (matUse.get(g) || { all: 0, shipped: 0 })), matUse.get(g).all++, e.shipped && matUse.get(g).shipped++; for (const g of e._texs) texUse.set(g, (texUse.get(g) || { all: 0, shipped: 0 })), texUse.get(g).all++, e.shipped && texUse.get(g).shipped++; }
writeCsv('materials.csv', [...matUse.entries()].map(([g, u]) => { const m = material(g); return { path: m.path, shader: m.shader, blend: m.blend, costly: m.costly.join('|'), textures: m.textures.length, usedBy: u.all, usedByShipped: u.shipped, keywords: m.keywords || '' }; }), ['path', 'shader', 'blend', 'costly', 'textures', 'usedBy', 'usedByShipped', 'keywords']);
writeCsv('textures.csv', [...texUse.entries()].map(([g, u]) => { const t = texture(g); return { path: t.path, w: t.w, h: t.h, maxSize: t.maxSize, android: t.android, ios: t.ios, effAndroid: t.effAndroid, mip: t.mip, type: t.type, usedBy: u.all, usedByShipped: u.shipped }; }), ['path', 'w', 'h', 'maxSize', 'android', 'ios', 'effAndroid', 'mip', 'type', 'usedBy', 'usedByShipped']);

// summary stats
function dist(arr) {
  const a = arr.filter((x) => typeof x === 'number' && isFinite(x)).sort((x, y) => x - y);
  if (!a.length) return null;
  const q = (p) => a[Math.min(a.length - 1, Math.floor(p * (a.length - 1)))];
  return { n: a.length, min: a[0], p50: q(0.5), p90: q(0.9), max: a[a.length - 1] };
}
function countBy(arr) { const m = {}; for (const x of arr) m[x] = (m[x] || 0) + 1; return Object.fromEntries(Object.entries(m).sort((a, b) => b[1] - a[1])); }
function summarize(list, label) {
  const sys = list.flatMap((e) => e._sys);
  return {
    label, effects: list.length, systems: sys.length,
    systemsPerEffect: dist(list.map((e) => e.systems)), estPeakPerEffect: dist(list.map((e) => e.estPeak)), sumMaxParticlesPerEffect: dist(list.map((e) => e.sumMaxParticles)),
    lifeMax: dist(list.map((e) => e.lifeMax)), totalTimeNonLoop: dist(list.filter((e) => e.totalTime !== 'loop').map((e) => e.totalTime)),
    loopingEffects: list.filter((e) => e.looping > 0).length, materialsPerEffect: dist(list.map((e) => e.materials)), texturesPerEffect: dist(list.map((e) => e.textures)), maxTexPerEffect: dist(list.map((e) => e.maxTex)),
    sys_maxParticles: dist(sys.map((s) => s.maxParticles)), sys_maxParticlesDefault1000: sys.filter((s) => s.maxParticles === 1000).length,
    sys_est: dist(sys.map((s) => s.est)), sys_lifeMax: dist(sys.map((s) => s.lifeMax)), sys_duration: dist(sys.map((s) => s.duration)),
    stopAction: countBy(sys.map((s) => ['None', 'Disable', 'Destroy', 'Callback'][s.stopAction] || s.stopAction)),
    cullingMode: countBy(sys.map((s) => ['Automatic', 'PauseAndCatchup', 'Pause', 'AlwaysSimulate'][s.cullingMode] || s.cullingMode)),
    scalingMode: countBy(sys.map((s) => ['Hierarchy', 'Local', 'Shape'][s.scalingMode] || s.scalingMode)),
    simSpace: countBy(sys.map((s) => ['Local', 'World', 'Custom'][s.simSpace] || s.simSpace)),
    renderMode: countBy(sys.map((s) => ['Billboard', 'Stretched', 'Horizontal', 'Vertical', 'Mesh', 'None'][s.renderMode] ?? 'noRenderer')),
    modules: { noise: sys.filter((s) => s.noise).length, collision: sys.filter((s) => s.collision).length, trigger: sys.filter((s) => s.trigger).length, subEmitters: sys.filter((s) => s.subEmitters).length, lights: sys.filter((s) => s.lightsMod).length, trails: sys.filter((s) => s.trailsMod).length, sheet: sys.filter((s) => s.sheet).length, externalForces: sys.filter((s) => s.externalForces).length, clampVel: sys.filter((s) => s.clampVel).length, prewarm: sys.filter((s) => s.prewarm).length, unscaled: sys.filter((s) => s.unscaled).length, gpuInstancing: sys.filter((s) => s.gpuInstancing).length, rateOverDistance: sys.filter((s) => s.rateDist > 0).length },
    sheetSizes: countBy(sys.filter((s) => s.sheet).map((s) => s.sheet)),
    blends: countBy(list.flatMap((e) => e.blends.split('|'))), shaders: countBy(list.flatMap((e) => e.shaders.split('|'))),
    costly: countBy(list.flatMap((e) => e.costly ? e.costly.split('|') : [])),
    layers: countBy(sys.map((s) => sortingLayers.get(s.sortingLayerID) || s.sortingLayerID)),
    categories: countBy(list.map((e) => e.category)),
    trailRenderers: list.reduce((a, e) => a + e.trailRenderers, 0), lineRenderers: list.reduce((a, e) => a + e.lineRenderers, 0), lightComponents: list.reduce((a, e) => a + e.lightComponents, 0),
    scripts: Object.fromEntries(Object.entries(countBy(list.flatMap((e) => e.scripts ? e.scripts.split('|') : []))).slice(0, 25)),
    startSat: dist(sys.flatMap((s) => s.startSV.map((x) => +x.s.toFixed(3)))), startVal: dist(sys.flatMap((s) => s.startSV.map((x) => +x.v.toFixed(3)))),
    pureWhiteStart: sys.filter((s) => s.startColors.includes('#ffffff')).length,
  };
}
const shippedEff = effects.filter((e) => e.shipped), shippedOwn = shippedEff.filter((e) => !e.pack);
const summary = {
  project: root, scannedAt: new Date().toISOString(), files: files.length, guids: guidToPath.size, prefabs: prefabFiles.length,
  buildScenes: buildScenes.filter((s) => s.enabled).map((s) => s.path), roots: roots.length, shippedAssets: shipped.size,
  vfxPrefabs: effects.length, vfxShipped: shippedEff.length, vfxPack: effects.filter((e) => e.pack).length, vfxShippedFromPackFolders: shippedEff.filter((e) => e.pack).length,
  sortingLayers: Object.fromEntries(sortingLayers),
  shippedOwn: summarize(shippedOwn, 'shipped, outside pack folders'),
  shippedAll: summarize(shippedEff, 'shipped, all'),
  notShipped: summarize(effects.filter((e) => !e.shipped), 'not shipped'),
  topFolders: countBy(shippedEff.map((e) => e.path.split('/').slice(0, 4).join('/'))),
  outliers: Object.fromEntries(['systems', 'estPeak', 'sumMaxParticles', 'lifeMax', 'maxTex', 'texMPix', 'materials'].map((k) => [k, [...shippedOwn].sort((a, b) => (b[k] || 0) - (a[k] || 0)).slice(0, 12).map((e) => e.path.replace(/^Assets\/_Project\//, '') + ' = ' + e[k])])),
  missingShaderMaterials: countBy(shippedEff.flatMap((e) => e._mats.map(material).filter((m) => /missing|none|parse error|^$/.test(m.shader)).map((m) => m.path + ' [' + m.shader + ']'))),
  costlyByShader: countBy(shippedEff.flatMap((e) => e._mats.map(material).filter((m) => m.costly.length).map((m) => m.shader + ' {' + m.costly.join(',') + '}'))),
  loopingNeverStops: shippedOwn.filter((e) => e.looping > 0 && !/Callback|Destroy|Disable/.test(e.stopActions)).length,
  alwaysSimulateLooping: shippedOwn.filter((e) => e._sys.some((s) => s.looping && s.cullingMode === 3)).length,
  prewarmNonLooping: shippedOwn.flatMap((e) => e._sys).filter((s) => s.prewarm && !s.looping).length,
  prewarmLooping: shippedOwn.flatMap((e) => e._sys).filter((s) => s.prewarm && s.looping).length,
  worldSpace: shippedOwn.flatMap((e) => e._sys).filter((s) => s.simSpace === 1).length,
  proceduralBlockers: (() => { const sys = shippedOwn.flatMap((e) => e._sys); const why = (s) => [s.simSpace === 1 && 'world', s.gravity !== 0 && 'gravity', s.rateDist > 0 && 'rateOverDistance', s.externalForces && 'externalForces', s.clampVel && 'limitVelocity', s.collision && 'collision', s.trigger && 'trigger', s.subEmitters && 'subEmitters', s.noise && 'noise', s.trailsMod && 'trails'].filter(Boolean); const blocked = sys.filter((s) => why(s).length); return { systems: sys.length, blocked: blocked.length, reasons: countBy(blocked.flatMap(why)) }; })(),
};
fs.writeFileSync(path.join(outDir, 'summary.json'), JSON.stringify(summary, null, 1));
console.log(JSON.stringify({ files: summary.files, prefabs: summary.prefabs, vfxPrefabs: summary.vfxPrefabs, vfxShipped: summary.vfxShipped, vfxShippedFromPackFolders: summary.vfxShippedFromPackFolders, buildScenes: summary.buildScenes.length, roots: summary.roots }));
