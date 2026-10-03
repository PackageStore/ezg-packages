import { readFileSync, writeFileSync } from 'node:fs';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { connect } from './bridge-client.mjs';
import {
  loadTable, loadCodes, loadDebt, resolveRows, validateDebt, applyDebt, verdict, ConfigError,
} from './rules.mjs';

const H = join(dirname(fileURLToPath(import.meta.url)), '..');
const EVAL_TIMEOUT_MS = 120000;
const LINT_LIMIT = 5000;

const USAGE = `usage: node audit.mjs --project <project.json> (--screens A,B | --page <name> | --nodes 1:2,3:4)
                 [--rules S-3,V-5] [--fail-on block|any|none] [--json <out>] [--port N]

exit: 0 policy passed, 1 findings failed --fail-on, 2 usage/config/debt error, 3 bridge unreachable`;

export function parseArgs(argv) {
  const opts = { failOn: 'block' };
  const valued = {
    '--project': 'project', '--screens': 'screens', '--page': 'page', '--nodes': 'nodes',
    '--rules': 'rules', '--fail-on': 'failOn', '--json': 'json', '--port': 'port',
  };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--help' || a === '-h') {
      opts.help = true;
      continue;
    }
    const key = valued[a];
    if (!key) throw new ConfigError(`unknown argument ${a}`);
    const v = argv[++i];
    if (v === undefined || v.startsWith('--')) throw new ConfigError(`${a} needs a value`);
    opts[key] = v;
  }
  if (opts.help) return opts;
  if (!opts.project) throw new ConfigError('--project is required');
  const modes = ['screens', 'page', 'nodes'].filter((k) => opts[k] !== undefined);
  if (modes.length !== 1) throw new ConfigError('give exactly one of --screens, --page, --nodes');
  if (!['block', 'any', 'none'].includes(opts.failOn)) throw new ConfigError('--fail-on must be block, any or none');
  for (const k of ['screens', 'nodes', 'rules']) {
    if (opts[k] !== undefined) opts[k] = opts[k].split(',').map((s) => s.trim()).filter(Boolean);
  }
  if (opts.port !== undefined && !Number.isInteger(Number(opts.port))) throw new ConfigError('--port must be an integer');
  return opts;
}

function readJson(path) {
  let text;
  try {
    text = readFileSync(path, 'utf8');
  } catch (e) {
    throw new ConfigError(`cannot read ${path}: ${e.message}`);
  }
  try {
    return JSON.parse(text);
  } catch (e) {
    throw new ConfigError(`invalid JSON in ${path}: ${e.message}`);
  }
}

function loadProject(path) {
  const project = readJson(path);
  for (const k of ['fileKey', 'screensPage', 'debtDir']) {
    if (typeof project[k] !== 'string' || !project[k]) throw new ConfigError(`${path}: missing ${k}`);
  }
  project.nineSlice = project.nineSliceRegistry ? readJson(resolve(project.nineSliceRegistry)) : {};
  return project;
}

const readHere = (...p) => readFileSync(join(H, ...p), 'utf8');

export function buildEvalBody(rows, targetIds, project) {
  const evalRows = rows.filter((r) => r.engine === 'eval');
  const files = [...new Set(evalRows.map((r) => r.eval.predicate))];
  const driver = `
const PROJECT = ${JSON.stringify(project)};
const ROWS = ${JSON.stringify(evalRows.map((r) => ({ id: r.id, params: r.params ?? {} })))};
const TARGETS = ${JSON.stringify(targetIds)};
const findings = [];
const missing = [];
for (const id of TARGETS) {
  const root = await figma.getNodeByIdAsync(id);
  if (!root) { missing.push(id); continue; }
  for (const row of ROWS) {
    try {
      const out = await PREDICATES[row.id](root, row.params, { project: PROJECT, finding });
      for (const f of out) findings.push(f);
    } catch (e) {
      findings.push(finding(row.id, root, 'rule ' + row.id + ' failed: ' + (e && e.message ? e.message : String(e))));
    }
  }
}
return { findings, missing };
`;
  return [readHere('predicates', 'prelude.js'), ...files.map((f) => readHere('predicates', f)), driver].join('\n');
}

export function mapTidy(result, page) {
  if (result?.error) {
    return [{ rule: 'S-10', nodeId: page.id, name: page.name, path: page.name, detail: `rule S-10 failed: ${result.error}` }];
  }
  const out = [];
  for (const [a, b] of result?.overlaps ?? []) {
    out.push({ rule: 'S-10', nodeId: b.id, name: b.name, path: b.name, detail: `overlaps ${a.name}` });
  }
  for (const g of result?.wideGaps ?? []) {
    if (g.above) {
      out.push({
        rule: 'S-10', nodeId: page.id, name: page.name, path: page.name,
        detail: `row ${g.row} sits ${g.gap} px below the previous row`,
      });
    } else {
      out.push({
        rule: 'S-10', nodeId: g.before.id, name: g.before.name, path: g.before.name,
        detail: `${g.gap} px from its left neighbour in row ${g.row}`,
      });
    }
  }
  return out;
}

function targetsCode(opts, project) {
  const q = JSON.stringify;
  const pageOf = `async function pageOf(n) { let p = n; while (p && p.type !== 'PAGE') p = p.parent; return p; }
const label = async (n) => { const p = await pageOf(n); return { id: n.id, name: n.name, pageId: p ? p.id : null, pageName: p ? p.name : null }; };`;
  if (opts.nodes) {
    return `${pageOf}
const out = [], missing = [];
for (const id of ${q(opts.nodes)}) {
  const n = await figma.getNodeByIdAsync(id);
  if (n) out.push(await label(n)); else missing.push(id);
}
return { targets: out, missing };`;
  }
  const pageName = opts.page ?? project.screensPage;
  const wanted = opts.screens ? q(opts.screens) : 'null';
  return `${pageOf}
const page = figma.root.children.find((p) => p.name === ${q(pageName)});
if (!page) return { targets: [], missing: [], error: 'no page named ' + ${q(pageName)} };
await page.loadAsync();
const wanted = ${wanted};
const frames = page.children.filter((n) => n.type === 'FRAME');
const hit = wanted ? frames.filter((n) => wanted.includes(n.name)) : frames;
const missing = wanted ? wanted.filter((w) => !frames.some((n) => n.name === w)) : [];
const out = [];
for (const n of hit) out.push(await label(n));
return { targets: out, missing };`;
}

async function evalValue(client, code, description) {
  const reply = await client.call('eval', { code, description, timeoutMs: EVAL_TIMEOUT_MS }, EVAL_TIMEOUT_MS);
  return reply?.value;
}

async function runLint(client, rows, targets, warnings) {
  const lintRows = rows.filter((r) => r.engine === 'lint');
  if (!lintRows.length) return [];
  const byBridge = new Map();
  const options = {};
  for (const r of lintRows) {
    if (!byBridge.has(r.lint.rule)) byBridge.set(r.lint.rule, r.id);
    Object.assign(options, r.lint.options);
  }
  const payload = {
    nodeIds: targets.map((t) => t.id),
    rules: [...byBridge.keys()],
    options,
    limit: LINT_LIMIT,
  };
  if (lintRows.some((r) => r.lint.includeInstances)) payload.includeInstances = true;
  const report = await client.call('lint', payload, EVAL_TIMEOUT_MS);
  if (report?.truncated) warnings.push('lint report truncated; findings may be missing');
  return (report?.findings ?? [])
    .filter((f) => byBridge.has(f.rule))
    .map((f) => ({ rule: byBridge.get(f.rule), nodeId: f.nodeId, name: f.name, path: f.path, detail: f.detail }));
}

async function runEval(client, rows, targets, project) {
  if (!rows.some((r) => r.engine === 'eval')) return [];
  const body = buildEvalBody(rows, targets.map((t) => t.id), project);
  const value = await evalValue(client, body, 'figma-hygiene audit predicates');
  return value?.findings ?? [];
}

async function runScripts(client, rows, targets) {
  const out = [];
  const pages = new Map();
  for (const t of targets) if (t.pageId) pages.set(t.pageId, { id: t.pageId, name: t.pageName ?? t.pageId });
  for (const row of rows.filter((r) => r.engine === 'script')) {
    const source = readHere('scripts', row.script.file);
    for (const page of pages.values()) {
      const head = Object.entries(row.script.inject ?? {})
        .map(([k, v]) => `const ${k} = ${JSON.stringify(v === '$page' ? page.id : v)};`)
        .join(' ');
      const value = await evalValue(client, `${head}\n${source}`, `figma-hygiene ${row.id}`);
      const map = row.script.map;
      if (map !== 'tidy') throw new ConfigError(`${row.id}: unknown script map ${map}`);
      out.push(...mapTidy(value, page));
    }
  }
  return out;
}

function summarize(rows, kept, doc) {
  const lines = [];
  for (const r of rows) {
    const n = kept.filter((f) => f.rule === r.id).length;
    lines.push(`${r.id}  ${r.blocks ? 'blocks' : 'warns '}  ${r.engine.padEnd(6)}  ${n}`);
  }
  for (const f of kept) lines.push(`${f.rule} ${f.blocks ? '[block]' : '[warn]'} ${f.path} - ${f.detail} (${f.nodeId})`);
  for (const w of doc.warnings) lines.push(`warning: ${w}`);
  lines.push(`debt: ${doc.debt.waived.length} waived, ${doc.debt.stale.length} stale`);
  for (const s of doc.debt.stale) lines.push(`stale debt: ${s.rule} ${s.nodeId} (${s.code})`);
  lines.push(doc.pass ? 'PASS' : 'FAIL');
  return lines.join('\n');
}

export async function run(argv, io = { out: (s) => process.stdout.write(s + '\n'), err: (s) => process.stderr.write(s + '\n') }) {
  let client;
  try {
    const opts = parseArgs(argv);
    if (opts.help) {
      io.out(USAGE);
      return 0;
    }
    const project = loadProject(resolve(opts.project));
    const table = loadTable(join(H, 'rules.json'));
    const codes = loadCodes(join(H, 'debt-codes.json'));
    const allIds = table.rules.map((r) => r.id);
    const rows = resolveRows(table, project, opts.rules);
    const debt = loadDebt(resolve(project.debtDir), project.fileKey);
    const errors = validateDebt(debt.entries, codes, allIds);
    if (errors.length) throw new ConfigError(errors.join('\n'));
    const selected = new Set(rows.map((r) => r.id));
    const entries = debt.entries.filter((e) => selected.has(e.rule));

    client = await connect({ port: opts.port, fileKey: project.fileKey });
    const resolved = await evalValue(client, targetsCode(opts, project), 'figma-hygiene resolve targets');
    if (resolved?.error) throw new ConfigError(resolved.error);
    if (resolved?.missing?.length) throw new ConfigError(`not found: ${resolved.missing.join(', ')}`);
    const targets = resolved?.targets ?? [];

    const warnings = [];
    const found = [
      ...(await runLint(client, rows, targets, warnings)),
      ...(await runEval(client, rows, targets, project)),
      ...(await runScripts(client, rows, targets)),
    ];
    const { kept, waived, stale } = applyDebt(found, entries);
    const v = verdict(kept, rows, opts.failOn);
    const blocksOf = new Map(rows.map((r) => [r.id, r.blocks]));
    const findings = kept.map((f) => ({ ...f, blocks: blocksOf.get(f.rule) }));
    const exit = v.pass ? 0 : 1;
    const doc = {
      fileKey: project.fileKey,
      targets: targets.map((t) => ({ id: t.id, name: t.name })),
      rows: rows.map((r) => ({
        id: r.id, blocks: r.blocks, engine: r.engine, count: kept.filter((f) => f.rule === r.id).length,
      })),
      findings,
      debt: { waived, stale, errors: [] },
      warnings,
      pass: v.pass,
      exit,
    };
    io.out(summarize(rows, findings, doc));
    if (opts.json) writeFileSync(resolve(opts.json), JSON.stringify(doc, null, 2) + '\n');
    return exit;
  } catch (e) {
    if (e instanceof ConfigError) {
      io.err(`config error: ${e.message}`);
      return 2;
    }
    io.err(`bridge error: ${e?.message ?? e}`);
    return 3;
  } finally {
    client?.close();
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  process.exitCode = await run(process.argv.slice(2));
}
