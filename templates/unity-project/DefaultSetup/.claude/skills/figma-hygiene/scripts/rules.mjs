import { readFileSync, existsSync } from 'node:fs';
import { join } from 'node:path';

export class ConfigError extends Error {
  constructor(message) {
    super(message);
    this.name = 'ConfigError';
  }
}

const ENGINES = ['lint', 'eval', 'script'];
const FAIL_ON = ['block', 'any', 'none'];

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

export function loadTable(path) {
  const table = readJson(path);
  if (!table || !Array.isArray(table.rules)) throw new ConfigError(`${path}: missing rules array`);
  const seen = new Set();
  for (const row of table.rules) {
    const id = row?.id;
    if (typeof id !== 'string' || !id) throw new ConfigError(`${path}: row without id`);
    if (seen.has(id)) throw new ConfigError(`${path}: duplicate id ${id}`);
    seen.add(id);
    if (!ENGINES.includes(row.engine)) throw new ConfigError(`${id}: engine must be one of ${ENGINES.join(', ')}`);
    if (typeof row.blocks !== 'boolean') throw new ConfigError(`${id}: blocks must be a boolean`);
    if (row.engine === 'eval' && !row.eval?.predicate) throw new ConfigError(`${id}: eval row needs eval.predicate`);
    if (row.engine === 'lint' && !row.lint?.rule) throw new ConfigError(`${id}: lint row needs lint.rule`);
  }
  return table;
}

export function loadCodes(path) {
  const data = readJson(path);
  if (!data || !Array.isArray(data.codes)) throw new ConfigError(`${path}: missing codes array`);
  return new Set(data.codes.map((c) => c.code));
}

export function loadDebt(dir, fileKey) {
  const path = join(dir, `${fileKey}.json`);
  if (!existsSync(path)) return { entries: [] };
  const data = readJson(path);
  if (!Array.isArray(data.entries)) throw new ConfigError(`${path}: missing entries array`);
  return data;
}

export function resolveRows(table, project, ruleIds) {
  const known = new Map(table.rules.map((r) => [r.id, r]));
  let rows = table.rules;
  if (ruleIds && ruleIds.length) {
    rows = ruleIds.map((id) => {
      const row = known.get(id);
      if (!row) throw new ConfigError(`unknown rule id ${id}`);
      return row;
    });
  }
  return rows.map((row) => {
    const { blocks: _ignored, ...override } = project?.ruleParams?.[row.id] ?? {};
    return { ...row, params: { ...row.params, ...override } };
  });
}

export function validateDebt(entries, codes, ruleIds) {
  const errors = [];
  const rules = ruleIds ? new Set(ruleIds) : null;
  entries.forEach((e, i) => {
    const at = `debt[${i}]`;
    if (!codes.has(e.code)) errors.push(`${at}: unknown code ${JSON.stringify(e.code)}`);
    if (rules && !rules.has(e.rule)) errors.push(`${at}: unknown rule id ${JSON.stringify(e.rule)}`);
    if (typeof e.nodeId !== 'string' || !e.nodeId) errors.push(`${at}: missing nodeId`);
    if (e.code === 'other' && !(typeof e.issue === 'string' && e.issue.trim())) {
      errors.push(`${at}: code "other" needs a non-empty issue`);
    }
  });
  return errors;
}

export function applyDebt(findings, entries) {
  const keyOf = (rule, nodeId) => `${rule}\u0000${nodeId}`;
  const keys = new Set(entries.map((e) => keyOf(e.rule, e.nodeId)));
  const kept = [];
  const waived = [];
  const waivedKeys = new Set();
  const hit = new Set();
  for (const f of findings) {
    const k = keyOf(f.rule, f.nodeId);
    if (!keys.has(k)) {
      kept.push(f);
      continue;
    }
    hit.add(k);
    if (!waivedKeys.has(k)) {
      waivedKeys.add(k);
      waived.push(f);
    }
  }
  const stale = entries.filter((e) => !hit.has(keyOf(e.rule, e.nodeId)));
  return { kept, waived, stale };
}

export function verdict(findings, rows, failOn) {
  if (!FAIL_ON.includes(failOn)) throw new ConfigError(`failOn must be one of ${FAIL_ON.join(', ')}`);
  const blocks = new Map(rows.map((r) => [r.id, r.blocks]));
  const blocking = [];
  const warnings = [];
  for (const f of findings) {
    if (!blocks.has(f.rule)) throw new ConfigError(`finding for unknown rule ${f.rule}`);
    (blocks.get(f.rule) ? blocking : warnings).push(f);
  }
  const pass = failOn === 'none' ? true : failOn === 'any' ? findings.length === 0 : blocking.length === 0;
  return { pass, blocking, warnings };
}
