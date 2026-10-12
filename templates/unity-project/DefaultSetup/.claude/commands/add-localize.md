---
description: Add a new localization key to the team sheet and into the project's CSV/.asset
---

# Add Localize

`/add-localize key: <key>, en: <English>, vi: <Vietnamese>[, tab: <table>]`

Thin entry point — the executable detail lives in the **`add-localize` skill**
(`.claude/skills/add-localize/SKILL.md`). Invoke that skill and follow it.

## 1. PARSE `{{args}}`

Pull out the key, the English copy and the Vietnamese copy.

- **key** — lowercase `snake_case`, **no `#` prefix** (that is the BlazeSurvivor
  sheet's convention, not this project's). The script normalises and rejects
  anything outside `[a-z0-9_]`.
- **en** — required. This is the source column; the other 14 languages are
  machine-translated from it.
- **vi** — authored by hand, like `en`. Ask for it if the request omits it
  rather than letting GOOGLETRANSLATE guess Vietnamese.
- **tab** — the runtime table the key belongs to. Defaults to `common`. Anything
  shop/IAP-facing is `shop`, settings screens are `settings`, quests `quest`,
  station/unit names `unit`, staff `staff`, tutorial `tut`, products `product`.
  A key in the wrong table renders as the raw key at runtime.
- Copy must read like mobile-game UI: short, concrete ("Cửa hàng", not "Nơi để
  mua đồ"). Keep every `{0}` placeholder, in the same order, in both languages.

Examples:
```
/add-localize key: ui_play, vi: Chơi, en: Play
/add-localize key: gold_pack_9, vi: Gói Vàng 9, en: Coin Pack 9, tab: shop
```

## 2. PLAN

```bash
python3 .claude/skills/add-localize/scripts/add_localize.py \
    --key '<key>' --en '<en>' --vi '<vi>' [--tab '<tab>']
```

Writes nothing: validates against the project and prints the call(s) for the
EZG MCP tool `localize_add_keys` (one per 25 keys). The sheet is written by that
tool with EZG's shared service account — never ask for or create a
service-account key file. Tool missing → the EZG MCP server is not connected;
see step 0 of the skill.

## 3. SHEET, THEN PROJECT

For each printed call, in order — without stopping to ask in between:

1. `localize_add_keys` with the call: stages the rows in `template` with
   GOOGLETRANSLATE and writes every key that finished translating into its
   target tab, answering within ~40 s.
2. The result has `apply_command` → run it verbatim. It merges exactly those
   keys into `LocalizationData/<lang>/<tab>.csv` and
   `Resources/LocalizationData/<lang>/<tab>.asset` for all 16 languages.
3. `status: in_progress` → call `localize_add_keys` again at once with the
   result's `next_call`, unchanged; back to 2.
4. `status: needs_attention` → ask the user about `failed` (type that language
   by hand / retry later / skip), then `retry_call` as the skill says.

For several keys at once, write a JSON list and pass `--file` in step 2.

## 4. REPORT

- [ ] Every key written to its tab (`written` across rounds), or listed as skipped / failed
- [ ] `apply_command` run for every round that had one
- [ ] CSV + `.asset` touched for the key only — `git diff --stat` shows nothing else
- [ ] Tell the user to open the Editor once so Unity reimports them

Do **not** attach the localize component to any prefab here, and do not go
hunting for other hardcoded text — that is `fill-localize`.
