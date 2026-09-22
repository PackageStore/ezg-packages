---
name: add-localize
description: >-
  Add one or a handful of NEW localization keys that you already know the wording
  for: write the row into the team sheet (English + Vietnamese by hand, the other
  14 languages machine-translated through the sheet's `template` staging tab),
  then merge just those keys into the project's LocalizationData CSVs and runtime
  .assets. Use when asked to "thêm key localize", "thêm key <x> vào sheet",
  "tạo key localize mới", "add a localize key", "add a new localization key",
  "cần key dịch cho nút này". NOT an audit tool - it never scans screens for
  hardcoded text, never decides key names for strings it was not given, and never
  touches prefabs; a sweep over screens is `fill-localize`.
---

# add-localize — one key, sheet to runtime asset

You already know the key name and the English (and ideally Vietnamese) copy.
This writes it everywhere it has to exist, in one command.

```
   template tab            target tab             project
  (staging, formulas)     (static text)      (CSV + .asset, 16 langs)
   key | =C<r> | GT...  ->  key | text...  ->  LocalizationData/<lang>/<tab>.*
```

## Run it

```bash
python3 .claude/skills/add-localize/scripts/add_localize.py \
    --key ui_play --en "Play" --vi "Chơi"
```

Target tab defaults to `common`. A key that belongs to another runtime table
needs `--tab`:

```bash
python3 .claude/skills/add-localize/scripts/add_localize.py \
    --key gold_pack_9 --en "Coin Pack 9" --vi "Gói Vàng 9" --tab shop
```

Batch — one JSON file, entries may target different tabs:

```bash
python3 .claude/skills/add-localize/scripts/add_localize.py --file keys.json
```

```json
[
  {"key": "ui_play",  "en": "Play",  "vi": "Chơi"},
  {"key": "ui_pause", "en": "Pause", "vi": "Tạm dừng", "tab": "settings"},
  {"key": "ui_quit",  "en": "Quit",  "vi": "Thoát", "ja": "終了"}
]
```

Always start with `--dry-run`; it prints the plan and writes nothing.

## Flags worth knowing

| Flag | Effect |
|------|--------|
| `--tab <name>` | target runtime table; default `common`, must be one of the 8 tables this project ships (`--allow-any-tab` to override) |
| `--<lang> "text"` | author one language by hand instead of machine-translating it (`--ja`, `--pt`, `--zhcn`, …) |
| `--no-pull` | stop at the sheet, leave CSV/.asset alone |
| `--stage-only` | stage in `template`, print the 16 translations, stop — no real tab, no project file. Use it to eyeball the machine translation first |
| `--overwrite` | let the sheet's copy replace what the project already has for these keys |
| `--force` | write the sheet row even if the key is already in that tab |
| `--create-missing` | create the CSV/.asset for a table this project does not ship yet |
| `--timeout N` | how long to wait for GOOGLETRANSLATE (default 90s) |

## Why it goes through the `template` tab

Every language cell in a real tab of this sheet holds **plain text**. Only
`template` holds formulas — `English-en` is `=C<row>` and the other 14 columns
are `=GOOGLETRANSLATE(C<row>, "en", <code>)`. That is the team's existing staging
convention, so the script follows it: stage the row there, wait for Sheets to
evaluate, then copy the **resolved text** into the real tab. The real tab stays
formula-free, which is what the CSV importer expects (and `fill-localize
pull`, in a project that has it).

The staging rows are left in `template` on purpose — that tab already mirrors
every shipped key, and deleting rows there would renumber the formulas of the
rows below.

## Things that will bite

- **GOOGLETRANSLATE is asynchronous.** A freshly written formula reads back as
  empty or `Loading...` for a few seconds. The script polls; if it times out it
  says so and leaves the staging rows in place so you can rerun rather than
  losing the work.
- **Machine translation has no game context.** It is fine for `Play` / `Shop`
  and wrong for anything with tone, a pun, or a `{0}` placeholder that has to
  stay in a specific position. Check the row before shipping, and pass
  `--<lang> "…"` for the ones you want authored.
- **The wrong tab means an invisible key.** Lookup is per `(table, key)`; a key
  sitting in `common` while the component reads `shop` renders as the raw key.
  The script refuses tabs this project has no CSV table for.
- **`--overwrite` is a downgrade for old keys.** The shipped `.asset` files hold
  hand-corrected copy that the sheet has since lost. Default behaviour adds new
  keys and keeps existing values; that default is correct far more often.
- **Vietnamese is meant to be authored.** Omit `--vi` and it gets machine-
  translated like the rest, with a warning. Pass it.
- **Unity has to reimport.** Open the Editor once after a run so the touched CSV
  and `.asset` files are picked up.
- **Line endings are preserved.** A table may be stored as LF or as CRLF, and
  on a checkout without `core.autocrlf` rewriting it in the other one turns
  "added one key" into a diff touching all 458 rows. This script keeps whatever
  the file already has — one key, one line. (`fill-localize pull` hardcodes
  CRLF; that is the divergence.)

## Boundary with `fill-localize`

| | add-localize | fill-localize |
|---|---|---|
| Input | key + copy you already decided | a screen (or the whole project) |
| Finds hardcoded text | no | yes (`localize_scan.py`) |
| Decides key names | no | yes, via `curation.json` |
| Translations | en + vi by hand, rest via GOOGLETRANSLATE | all 16 authored in `translations.json` |
| Touches prefabs | never | yes (`localize_patch.py`) |
| Scale | one key, or a short batch | a sweep |

`fill-localize` does not have to be installed. The Sheets auth and the
LanguageData `.asset` reader/writer are vendored into `scripts/sheet_io.py`,
lifted verbatim from that skill — this one ships in the default setup of every
new project, and a skill that silently needs another skill is broken on
arrival. Fix either copy and carry the fix across.

## Setup

- Python 3, standard library only (`cryptography` if present, else `openssl` on
  PATH — only for signing the service-account JWT). No pip install.
- A **service-account JSON key** whose `client_email` is an **Editor** on the
  sheet. The public link is enough to *read* a sheet; every write in this skill
  goes through the Sheets API, which has no anonymous write.
- Per-project values live in `.claude/project-profile.json`, never in the
  script — it ships byte-identical to every project:

```json
"localize": {
  "sheet": "https://docs.google.com/spreadsheets/d/<id>",
  "serviceAccount": "Key/service-account-gsheets.json",
  "csvRoot": "Assets/_Project/Features/_Shared/Localize/LocalizationData",
  "assetsRoot": "Assets/_Project/Features/_Shared/Localize/Resources/LocalizationData",
  "stagingTab": "template",
  "defaultTab": "common"
}
```

  Paths are repo-relative, so the command runs from any directory. `csvRoot` and
  `assetsRoot` are derived from `sourceRoot` when omitted. `--sheet`,
  `--service-account`, `--csv-root` and `--assets` override any of it for one
  run; `LOCALIZE_SHEET` overrides the sheet for a shell.

## Porting to a project that is not set up yet

The sheet has to look like the one this was written against: column `A` key,
`B` Duplicate, `C` English(Origin), `D` English-en, then one `Name-code` column
per language, and a staging tab whose language cells are
`=GOOGLETRANSLATE(C<row>, "en", <code>)`. Language codes come from the header
suffix (`Chinese-zhcn` -> `zhcn`), and the project is expected to store
`LocalizationData/<lang>/<tab>.csv` as `key~value`. A sheet shaped differently
needs `GT_CODE` and the column assumptions in `add_localize.py` revisited, not
just a new URL in the profile.
