# Provenance

Forked from https://github.com/natdexterra/work-with-design-systems

- Upstream commit: `7d5edab3c4b27b940daf770bfc169c82ff940bdb`
- Upstream skill version:2.0.3
- Forked: 2026-08-28

## Files taken

| Local | Upstream | Change |
|---|---|---|
| `scripts/inventory.js` | `scripts/inspect/inventory.js` | verbatim |
| `scripts/audit-detached.js` | `scripts/inspect/audit-detached.js` | verbatim |
| `scripts/audit-tokens.js` | `scripts/inspect/audit-tokens.js` | `COMMON_SCALE` -> `SPACING_SCALE` discovery mode + `spacingHistogram` |
| `scripts/audit-states.js` | `scripts/inspect/audit-states.js` | web state ladders -> game archetypes |
| `scripts/audit-naming.js` | `scripts/inspect/audit-naming.js` | optional trailing number, `spacedNames` |
| `scripts/fixHardcodedToTokens.js` | `scripts/build/fixHardcodedToTokens.js` | dropped `codeSyntax.WEB` pre-condition |
| `reference/token-taxonomy.md` | `references/build/token-taxonomy.md` | rewritten for a single-theme game UI |
| `reference/naming-conventions.md` | `references/build/naming-conventions.md` | rewritten for a single-theme game UI |
| `reference/component-description-template.md` | `references/build/component-description-template.md` | Unity notes replace React/ARIA |

Not taken: `audit-accessibility.js`, `validate-design-system.js`, `exportTokensToCSS.js`, `code-export.md`, `framework-mappings.md`, `slots-guide.md`, `patterns-guide.md`, `auto-fix-guide.md`. See the "What was dropped" table in `SKILL.md`.
