---
name: add-localize
description: Add one or a handful of NEW localization keys you already know the wording for - the EZG MCP tool localize_add_keys writes the row into the team sheet (English + Vietnamese by hand, the other 14 languages machine-translated through the sheet's `template` staging tab, with EZG's shared service account - no key file), then the skill's script merges just those keys into the project's LocalizationData CSVs and runtime .assets. Any batch size - the tool works in short rounds the agent repeats on its own. Use when asked to "thêm key localize", "thêm key <x> vào sheet", "tạo key localize mới", "add a localize key", "add a new localization key", "cần key dịch cho nút này". NOT an audit tool - it never scans screens for hardcoded text, never decides key names for strings it was not given, and never touches prefabs; a sweep over screens is `fill-localize`.
---

# add-localize — one key, sheet to runtime asset

You already know the key name and the English (and ideally Vietnamese) copy —
one key or fifty. Three steps put them everywhere they have to exist:

```
  1. plan (script)        2. sheet (EZG MCP, rounds)               3. apply (script, each round)
  validate vs project  ->  template tab: key | =C<r> | GT...    ->  LocalizationData/<lang>/<tab>.csv
  print tool calls         target tab:  key | static text           Resources/.../<lang>/<tab>.asset
```

**Run it end to end without stopping to ask** — the only point that needs the
user is a key Google cannot translate (`needs_attention`). Between rounds, one
short progress line is enough ("vòng 2: 31/50 key đã xong").

The sheet is written by the **EZG MCP server** (`localize_add_keys`) with EZG's
shared Google service account. The key stays on that server: **never** ask for,
create or point to a service-account JSON in this project.

## 0. Prerequisites

- The EZG MCP server is connected — its tools appear as `mcp__ezg__…` (or under
  whatever name it was added with). If `localize_add_keys` is not available:
  ```bash
  claude mcp add --transport http --scope user ezg https://ezg-mcp.developer-a1f.workers.dev/mcp
  ```
  then `/mcp` → **ezg** → **Authenticate** with the @easygoing.vn account, and
  restart Claude Code if the tools still do not show. Do not fall back to any
  other way of writing the sheet.
- `.claude/project-profile.json` → `localize.sheet` is the project's
  localization workbook. Empty = stop and ask for the link (or run
  `/setup-project localize`).
- The sheet is shared with the service account as **Editor**. `gsheet_info`
  (no arguments) returns the email; a permission error from any step means the
  sheet owner has to share it — say exactly that.

## 1. Plan

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

It writes nothing. It normalises the keys, refuses a tab this project has no
CSV table for, and prints the **exact arguments** for `localize_add_keys` —
one call per 25 keys (`--- call 1/2 ---`), to run in order.
(Windows without `python3`: use `py` / `python`.)

## 2. Sheet — `localize_add_keys`

Call the tool with a printed call, unchanged. It stages the keys in `template`
and answers within ~40 s whatever the batch size (Claude Code cuts an MCP call
at 60 s), with a `status`:

| `status` | Do |
|---|---|
| `done` | run `apply_command` (step 3); next printed call, or finished |
| `in_progress` | run `apply_command` if the result has one (keys finished this round), then call `localize_add_keys` **again at once with `next_call`, exactly as given** — no question to the user, no re-planning. It continues the same staging rows; repeat until the status changes |
| `needs_attention` | run `apply_command` if present, then ask the user about `failed` (key + language + Google's message): **(a)** type that language by hand → call `retry_call` with the text added to that entry as a field (`"ja": "…"`); **(b)** try again later → `retry_call` as is; **(c)** skip the key |

- A key already in its target tab is not written again (`skipped`), but it is
  still part of `apply_command` — rerunning add-localize is how a key that
  reached the sheet but not the project gets pulled in. `force` writes a
  second row; ask before using it.
- Unsure about the sheet? `"dry_run": true` checks tabs, headers and existing
  keys and writes nothing.
- Never build a call by hand from `staging_rows` / `round` — copy `next_call`
  / `retry_call`. Calling the plan's JSON again for keys already staged would
  stage them twice.

## 3. Apply — every round that wrote keys

Run the round's `apply_command` verbatim:

```bash
python3 .claude/skills/add-localize/scripts/add_localize.py --apply-url '<url>'
```

The script fetches exactly the rows that round wrote (a signed link, valid 7
days) and merges them into every language's CSV and `.asset` — additive, an
existing value is kept unless `--overwrite`. **Do not copy translations out of
the tool result**; the link carries them. `--dry-run` reports without writing.
Apply after each round rather than once at the end: if the session stops
midway, the sheet and the project still agree. (`--apply <result.json>` and
`--apply -` for stdin still take a saved result with an inline `apply` block.)

## Flags worth knowing

| Flag | Step | Effect |
|------|------|--------|
| `--tab <name>` | plan | target runtime table; default `common`, must be one of the tables this project ships (`--allow-any-tab` to override) |
| `--<lang> "text"` | plan | author one language by hand instead of machine-translating it (`--ja`, `--pt`, `--zhcn`, …) |
| `--stage-only` | plan | the tool stages in `template` and returns the 16 translations, nothing else — no real tab, no project file. Use it to eyeball the machine translation first |
| `--force` | plan | the tool writes the sheet row even if the key is already in that tab |
| `--apply-url <url>` | apply | the round's `apply_command`; repeatable |
| `--overwrite` | apply | let the sheet's copy replace what the project already has for these keys |
| `--create-missing` | apply | create the CSV/.asset for a table this project does not ship yet |
| `--dry-run` | apply | report what would change, write nothing |

## Why it goes through the `template` tab

Every language cell in a real tab of this sheet holds **plain text**. Only
`template` holds formulas — `English-en` is `=C<row>` and the other 14 columns
are `=GOOGLETRANSLATE(C<row>, "en", <code>)`. That is the team's existing staging
convention, so the tool follows it: stage the row there, wait for Sheets to
evaluate, then copy the **resolved text** into the real tab. The real tab stays
formula-free, which is what the CSV importer expects (and `fill-localize
pull`, in a project that has it).

The staging rows are left in `template` on purpose — that tab already mirrors
every shipped key, and deleting rows there would renumber the formulas of the
rows below.

## Things that will bite

- **GOOGLETRANSLATE is slow and sometimes fails.** A fresh formula reads back
  as `Loading...` for seconds (minutes for hundreds at once), and now and then
  as `#VALUE!` "Google Translate internal error." — which never clears by
  itself. The tool finishes what it can each round, re-enters a failing cell so
  Google translates it again, and only after 3 rounds in error reports the key
  as `failed` instead of holding the rest of the batch.
- **Machine translation has no game context.** It is fine for `Play` / `Shop`
  and wrong for anything with tone, a pun, or a `{0}` placeholder that has to
  stay in a specific position. Check the result before shipping, and pass
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
- **Line endings are preserved.** A table may be stored as LF, as CRLF, or
  mixed (mostly LF with a few CRLF rows from a hand edit — common), and on a
  checkout without `core.autocrlf` rewriting it turns "added one key" into a
  diff touching every row. So the CSV is patched, never re-serialised: a new
  key is appended as one line in the ending most rows use, `--overwrite`
  replaces only that key's line, every other byte stays. (`fill-localize pull`
  hardcodes CRLF; that is the divergence.)
- **Two people at once.** The tool checks every staging row still holds its own
  key before writing the target tab and stops if someone else wrote there; rerun
  after looking at the sheet.

## Boundary with `fill-localize`

| | add-localize | fill-localize |
|---|---|---|
| Input | key + copy you already decided | a screen (or the whole project) |
| Finds hardcoded text | no | yes (`localize_scan.py`) |
| Decides key names | no | yes, via `curation.json` |
| Translations | en + vi by hand, rest via GOOGLETRANSLATE | all 16 authored in `translations.json` |
| Touches prefabs | never | yes (`localize_patch.py`) |
| Scale | one key, or a short batch | a sweep |

`fill-localize` does not have to be installed. The LanguageData `.asset`
reader/writer is vendored into `scripts/sheet_io.py`, lifted verbatim from that
skill — this one ships in the default setup of every new project, and a skill
that silently needs another skill is broken on arrival. Fix either copy and
carry the fix across.

## Setup

- Python 3, standard library only. No pip install, no key file.
- The EZG MCP server connected and authenticated (step 0).
- Per-project values live in `.claude/project-profile.json`, never in the
  script — it ships byte-identical to every project:

```json
"localize": {
  "sheet": "https://docs.google.com/spreadsheets/d/<id>",
  "csvRoot": "Assets/_Project/Features/_Shared/Localize/LocalizationData",
  "assetsRoot": "Assets/_Project/Features/_Shared/Localize/Resources/LocalizationData",
  "stagingTab": "template",
  "defaultTab": "common"
}
```

  Paths are repo-relative, so the command runs from any directory. `csvRoot` and
  `assetsRoot` are derived from `sourceRoot` when omitted. `--sheet`,
  `--csv-root` and `--assets` override any of it for one run; `LOCALIZE_SHEET`
  overrides the sheet for a shell. A leftover `serviceAccount` key from older
  profiles is ignored — delete it, and delete the key file it pointed to.

## Porting to a project that is not set up yet

The sheet has to look like the one this was written against: column `A` key,
`B` Duplicate, `C` English(Origin), `D` English-en, then one `Name-code` column
per language, and a staging tab whose language cells are
`=GOOGLETRANSLATE(C<row>, "en", <code>)`. Language codes come from the header
suffix (`Chinese-zhcn` -> `zhcn`), and the project is expected to store
`LocalizationData/<lang>/<tab>.csv` as `key~value`. The tool refuses a sheet
missing any of the 16 language columns, naming what is missing. Then share the
sheet with the service account as Editor.
