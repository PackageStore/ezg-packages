# Provenance

Two upstreams.

## A — natdexterra/work-with-design-systems

https://github.com/natdexterra/work-with-design-systems

- Upstream commit: `7d5edab3c4b27b940daf770bfc169c82ff940bdb` (2026-08-24)
- Upstream skill version: 2.0.3
- Forked: 2026-08-28

Same pin as `figma-tokens`. These are the `references/build/` files that
`figma-tokens/PROVENANCE.md` lists under "Not taken" — they cover component
authoring, which the token skill does not.

| Local | Upstream | Change |
|---|---|---|
| `reference/component-spec.md` | `references/build/component-spec.md` | Core-10 web specs replaced with this project's archetypes; web state ladder cut to Normal/Pressed/Disabled/Active; focus ring, WCAG gates and the Size axis dropped; `.`/`__` private prefix replaced with `Base_` |
| `reference/slots-guide.md` | `references/build/slots-guide.md` | Decision tree kept; slot implementation replaced with the exposed-instance fallback after measuring that `figma.createSlot` does not exist here; Leading/Trailing/Header/Footer names replaced with the file's own |
| `reference/patterns-guide.md` | `references/build/patterns-guide.md` | Fixed-width wrapper replaced with the project's fixed-frame multi-column grid; `P{section}.{number}` numbering dropped; dedicated Patterns page dropped; upstream's form/page/empty-state patterns replaced with this product's |

Not taken from A: `auto-fix-guide.md`, `code-export.md`,
`framework-mappings.md`, `token-taxonomy.md`, `naming-conventions.md`,
`component-description-template.md`. The first three are CSS-export work that
does not exist here; the last three are already vendored by `figma-tokens` and
must not be duplicated.

## B — Figma plugin skill `figma-generate-library`

`~/.claude/plugins/cache/claude-plugins-official/figma/2.2.95/skills/figma-generate-library`

- Plugin version: 2.2.95
- Forked: 2026-08-28

| Local | Upstream | Change |
|---|---|---|
| `reference/component-creation.md` | `references/component-creation.md` | Inter replaced with the project body font; upstream token names replaced with this file's real Semantic names; grid gap 16→20 (`space/default`) and padding 40→30 (`space/margin`); one-page-per-component dropped; `Building Blocks/` slash namespace changed to `Building_Blocks_` underscore; `codeSyntax` and `documentationLinks` removed; the set-level property limit and the exposed-instance workaround added from `psd-to-figma/reference/contracts.md` |
| `reference/discovery-phase.md` | `references/discovery-phase.md` | Codebase token extraction (CSS custom properties, Tailwind, DTCG, CSS-in-JS, iOS, Android) removed entirely — there is none; replaced with the project's own registries. `search_design_system` demoted: this file publishes no library and subscribes to none |
| `reference/error-recovery.md` | `references/error-recovery.md` | Error table rewritten around the traps that actually occur here; state ledger path moved to the session scratchpad; per-phase recovery rewritten for audit / master / states / promotion |
| `scripts/createComponentWithVariants.js` | `scripts/createComponentWithVariants.js` | Takes a pre-built bound base instead of raw `baseProps`; adds the 30-combination guard, the check-before-create guard, axis-aware grid layout (last axis on columns) and the clear-position rule |
| `scripts/cleanupOrphans.js` | `scripts/cleanupOrphans.js` | Refuses any node on the `Screens` page and any master with live instances; `getInstancesAsync` resolved per variant, since it is a `ComponentNode` method and not a `ComponentSetNode` one |
| `scripts/validateComponent.js` | `scripts/validateCreation.js` | Rewritten: axis-product check, duplicate-name check, (0,0) stacking check, hygiene S-2 generic names, V-3 text-style binding, unbound visual properties |

Not taken from B: `token-creation.md`, `naming-conventions.md`,
`code-connect-setup.md`, `documentation-creation.md`,
`scripts/createVariableCollection.js`, `scripts/createSemanticTokens.js`,
`scripts/bindVariablesToComponent.js`, `scripts/createDocumentationPage.js`,
`scripts/inspectFileStructure.js`.

Why each:

| Not taken | Reason |
|---|---|
| `token-creation.md`, `createVariableCollection.js`, `createSemanticTokens.js`, `bindVariablesToComponent.js` | `figma-tokens` owns the token layer and already has its own taxonomy and `fixHardcodedToTokens.js`. Two skills minting variables would fork the collection. |
| `naming-conventions.md` | `figma-tokens/reference/naming-conventions.md` already governs variable, component, variant and layer names, including the `State` ladder. |
| `code-connect-setup.md` | No Code Connect. Unity consumes the file through the UnityFigmaBridge, and there is no CSS or React target. |
| `documentation-creation.md`, `createDocumentationPage.js` | Builds Cover / Getting Started / Foundations pages with colour swatches, type specimens and spacing bars. Conflicts with the three-page structure that `figma-to-unity` and `psd-to-figma` address by name. |
| `inspectFileStructure.js` | Superseded by `scripts/auditComponentCoverage.js`, which measures instance coverage per screen — the number that actually diagnoses a thin component page. |

## New, not from either upstream

| File | Why |
|---|---|
| `scripts/auditComponentCoverage.js` | Neither upstream measures how much of a screen is instances versus loose nodes. That ratio is the diagnosis. |
| `scripts/addStateVariants.js` | Adding a `State` axis to an existing set is the single largest gap in this file, and Figma has no "add axis" API. |
| `scripts/promoteToComponent.js` | Promotion plus instance adoption, with a dry run, a size-mismatch refusal and a hand-off to the `psd-to-figma` verify pass. |

## Measured while forking (2026-08-28)

These are qualitative findings from the fork session. Every count is a snapshot,
not a fact — the file is edited by hand between reads, so re-measure with
`scripts/auditComponentCoverage.js` before trusting any number.

- `figma.createSlot` does not exist in this Plugin API build. Slots are
  unavailable; the exposed-instance fallback is the only path.
- The `Components` and `Icons` pages gained sets inside the session that produced
  this skill — every page baseline is a snapshot, not a fact.
- Most interactive sets carry no `State` axis; only one card set had one.
- Free non-instance nodes per screen vary widely; one screen was a clear outlier
  and one was the standard to aim at.
- Only a minority of screens have the grid style applied. The rest violate
  `figma-hygiene` V-4.
- Existing masters whose widths sit off the column grid are pinned debt and must
  not be resized.
- Registry drift: `psd-to-figma/reference/component-registry.md` listed
  component ids that were not on the page. Refresh it against the live file.
