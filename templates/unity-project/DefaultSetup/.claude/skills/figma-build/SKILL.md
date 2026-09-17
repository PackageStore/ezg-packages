---
name: figma-build
description: Build a Figma frame from a build plan JSON with tested Plugin API helpers — containers, image rects, 9-slice frames, component instances and ink-positioned text — through one generated use_figma payload. Source-agnostic: the plan is the whole input; PSD, code or hand-written producers all use it. Use when asked to "build this screen in Figma from a plan", "generate the use_figma payload", "run the helpers self-test", or when another skill needs a frame created from an op list. Does NOT parse PSDs (use psd-to-figma), does NOT create variables (use figma-tokens), does NOT run the hygiene gate (use figma-hygiene).
---

# Figma build

Creates one frame in the target Figma file from a build plan
(`reference/build-plan.md`). Nothing project- or source-specific lives here:
page, frame, font, hashes, component ids and style ids all arrive inside the
plan or as flags.

`<scripts>` is this skill's `scripts/` dir. Any `python3` works; the scripts
use the standard library only.

## Files

| File | Role |
|---|---|
| `scripts/figma_helpers.js` | Plugin API helpers: `nineSliceFrame`, `rectHash`, `instanceAt`, `textByInk`, `reparentKeepWorld`, `deleteByIds`, `readGeometry`. Paste ahead of any build. |
| `scripts/figma_helpers_selftest.js` | Exercises every helper on one page, asserts geometry to 0.01px, deletes its frame. |
| `scripts/figma_build.js` | Template that walks `PLAN.ops` and calls the helpers. Never paste as-is. |
| `scripts/figma_build_gen.py` | Fills the template with a plan and prints the payload. |
| `reference/build-plan.md` | The plan and result contract. |
| `reference/plugin-helpers.md` | What each helper does and the trap it prevents. |

## Commands

```bash
# helpers self-test, once per session, on any page id
python3 <scripts>/figma_build_gen.py --helpers-selftest --page-id <id> --font Family/Style --out <payload.js>

# build self-test: three containers on a page, deleted on success
python3 <scripts>/figma_build_gen.py --selftest --page <page name> --out <payload.js>

# build a frame
python3 <scripts>/figma_build_gen.py --plan <plan.json> [--font Family/Style] [--at X,Y] --out <payload.js>
```

Paste the payload into `use_figma` (load `figma-use` first; `whoami` must show
a full seat). The helpers self-test must return `{pass: true}` with no leftover
node before a real build. Save the build's return object; it maps op ids and
`layerKey`s to node ids.

## Rules

- **Generated payload, never hand-written.** A change goes into the plan or
  into the helpers, then regenerate.
- **One payload, one frame.** A throw rolls back every write in the payload;
  fix the plan and run again rather than patching in Figma.
- **Blocking warnings stay blocking.** `NO_STYLE` / `NO_HASH` in the plan stop
  the build unless the producer lists them in `allowWarnings` on purpose.
- **Text is placed by ink box**, not by node origin; `textDeltas` in the result
  is the residual and must be ≤ 0.01px.
- **Fonts arrive from the caller** (`--font` or `plan.font`). No font name is
  written in this skill.

## Producers

- `psd-to-figma` — `build_plan_gen.py` turns a PSD manifest into a plan and its
  `figma_build_gen.py` wrapper calls this skill with the project font.
- Any other producer writes the same JSON and runs the command above.
