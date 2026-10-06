// report.cjs — tóm tắt summary.json của vfx_scan.mjs. Dùng: node report.cjs <thư mục kết quả> [<thư mục kết quả> ...]
const fs = require('fs');
const path = require('path');
if (process.argv.length < 3) { console.error('node report.cjs <thư mục kết quả> [<thư mục kết quả> ...]'); process.exit(1); }
const d = (x) => (x ? `${x.min} / ${x.p50} / ${x.p90} / ${x.max} (n${x.n})` : '-');
for (const dir of process.argv.slice(2)) {
  const s = JSON.parse(fs.readFileSync(path.join(dir, 'summary.json'), 'utf8'));
  const o = s.shippedOwn;
  console.log(`=== ${path.basename(path.resolve(dir))}: vfx prefabs ${s.vfxPrefabs}, shipped ${s.vfxShipped} (from pack folders ${s.vfxShippedFromPackFolders}), shipped own ${o.effects} effects / ${o.systems} systems`);
  for (const k of ['systemsPerEffect', 'estPeakPerEffect', 'lifeMax', 'totalTimeNonLoop', 'materialsPerEffect', 'texturesPerEffect', 'maxTexPerEffect'])
    console.log('  ' + k.padEnd(20) + d(o[k]));
  console.log('  looping effects', o.loopingEffects, '| sys maxParticles=1000:', o.sys_maxParticlesDefault1000, '| white start color sys:', o.pureWhiteStart);
  for (const k of ['stopAction', 'cullingMode', 'scalingMode', 'simSpace', 'renderMode', 'blends', 'categories', 'layers'])
    console.log('  ' + k.padEnd(12) + JSON.stringify(o[k]));
  console.log('  modules     ' + JSON.stringify(o.modules));
  console.log('  shaders     ' + JSON.stringify(o.shaders).slice(0, 900));
  console.log('  costly      ' + JSON.stringify(s.costlyByShader).slice(0, 500));
  console.log('  missingShaderMats ' + Object.keys(s.missingShaderMaterials).length + ' ' + JSON.stringify(s.missingShaderMaterials).slice(0, 500));
  console.log(`  loopNoStop ${s.loopingNeverStops} | alwaysSimLoop ${s.alwaysSimulateLooping} | prewarm nonLoop ${s.prewarmNonLooping} loop ${s.prewarmLooping} | world ${s.worldSpace}`);
  console.log('  procedural  ' + JSON.stringify(s.proceduralBlockers));
  for (const k of Object.keys(s.outliers)) console.log('  top ' + k + ': ' + s.outliers[k].slice(0, 5).join(' ; '));
}
