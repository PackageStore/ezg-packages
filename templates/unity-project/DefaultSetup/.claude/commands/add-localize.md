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

## 2. DRY RUN FIRST

```bash
python3 .claude/skills/add-localize/scripts/add_localize.py \
    --key '<key>' --en '<en>' --vi '<vi>' [--tab '<tab>'] --dry-run
```

Show the plan. If the key already exists in that tab the script says so — stop
and ask instead of forcing.

## 3. RUN IT

```bash
python3 .claude/skills/add-localize/scripts/add_localize.py \
    --key '<key>' --en '<en>' --vi '<vi>' [--tab '<tab>']
```

It stages the row in `template` with GOOGLETRANSLATE formulas, waits for them to
resolve, writes the static text into the target tab, then merges just those keys
into `LocalizationData/<lang>/<tab>.csv` and
`Resources/LocalizationData/<lang>/<tab>.asset` for all 16 languages.

For several keys at once, write a JSON list and pass `--file`.

## 4. REPORT

- [ ] Sheet row written (tab + row number from the script output)
- [ ] 16 language columns resolved, no `#ERROR!`
- [ ] CSV + `.asset` touched for the key only — `git diff --stat` shows nothing else
- [ ] Tell the user to open the Editor once so Unity reimports them

Do **not** attach the localize component to any prefab here, and do not go
hunting for other hardcoded text — that is `fill-localize`.
