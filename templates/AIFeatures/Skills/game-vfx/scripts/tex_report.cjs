// tex_report.cjs — thống kê texture của hiệu ứng đã vào build. Dùng: node tex_report.cjs <thư mục kết quả> [...]
const fs = require('fs');
const path = require('path');
if (process.argv.length < 3) { console.error('node tex_report.cjs <thư mục kết quả> [...]'); process.exit(1); }
const parse = (file) => {
  const lines = fs.readFileSync(file, 'utf8').split('\n').filter(Boolean);
  const head = lines[0].split(',');
  return lines.slice(1).map((l) => {
    const cells = []; let cur = '', q = false;
    for (const ch of l) { if (ch === '"') q = !q; else if (ch === ',' && !q) { cells.push(cur); cur = ''; } else cur += ch; }
    cells.push(cur);
    return Object.fromEntries(head.map((h, i) => [h, cells[i]]));
  });
};
const countBy = (a) => { const m = {}; for (const x of a) m[x] = (m[x] || 0) + 1; return Object.fromEntries(Object.entries(m).sort((x, y) => y[1] - x[1])); };
const FMT = { 47: 'ETC2_RGBA8', 48: 'ASTC_4x4', 49: 'ASTC_5x5', 50: 'ASTC_6x6', 51: 'ASTC_8x8', 52: 'ASTC_10x10', 53: 'ASTC_12x12', 4: 'RGBA32', 3: 'RGB24', 1: 'Alpha8', 34: 'ETC_RGB4', 45: 'ETC2_RGB', 46: 'ETC2_RGBA1', 63: 'ETC2_RGBA8Crunched', 64: 'ETC_RGB4Crunched', '-1': 'auto', 13: 'RGBA4444', 7: 'RGB565' };
for (const dir of process.argv.slice(2)) {
  const rows = parse(path.join(dir, 'textures.csv')).filter((r) => Number(r.usedByShipped) > 0);
  const dim = (r) => Math.max(Number(r.w) || 0, Number(r.h) || 0);
  const eff = rows.map((r) => Number(r.effAndroid) || dim(r));
  const bucket = (v) => (v <= 64 ? '<=64' : v <= 128 ? '<=128' : v <= 256 ? '<=256' : v <= 512 ? '<=512' : v <= 1024 ? '<=1024' : '>1024');
  const pot = (x) => x > 0 && (x & (x - 1)) === 0;
  const npot = rows.filter((r) => { const w = Number(r.w), h = Number(r.h); return w && h && !(pot(w) && pot(h)); }).length;
  console.log(`=== ${path.basename(path.resolve(dir))}: ${rows.length} textures used by shipped VFX`);
  console.log('  source size   ', JSON.stringify(countBy(rows.map((r) => bucket(dim(r))))));
  console.log('  android size  ', JSON.stringify(countBy(eff.map(bucket))));
  console.log('  android fmt   ', JSON.stringify(countBy(rows.map((r) => (r.android ? FMT[r.android.split('/fmt')[1]] || 'fmt' + r.android.split('/fmt')[1] : 'no override')))));
  console.log('  mipmaps       ', JSON.stringify(countBy(rows.map((r) => (r.mip === '1' ? 'on' : r.mip === '0' ? 'off' : '?')))));
  console.log('  type          ', JSON.stringify(countBy(rows.map((r) => ({ 0: 'Default', 8: 'Sprite', 1: 'NormalMap', 2: 'GUI', 10: 'SingleChannel' }[r.type] || r.type)))));
  console.log('  NPOT          ', npot, '| >=1024 src:', rows.filter((r) => dim(r) >= 1024).length);
  const big = rows.filter((r) => dim(r) >= 1024).sort((a, b) => dim(b) - dim(a)).slice(0, 5).map((r) => r.path.replace(/^Assets\/_Project\//, '') + ' ' + r.w + 'x' + r.h + ' android ' + (r.android || 'default ' + r.maxSize));
  console.log('  biggest       ', big.join(' ; '));
}
