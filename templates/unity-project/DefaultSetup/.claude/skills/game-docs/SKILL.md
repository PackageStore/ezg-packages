---
name: game-docs
description: Search, read and (only when asked) edit this project's external design docs - Google Sheets listed in .claude/project-profile.json under gameDocs.sources - through the EZG MCP tools gsheet_search / gsheet_read / gsheet_write / gsheet_delete_rows, which use EZG's shared service account (no key file in the project). Use when the user asks about game design/monetization/economy numbers, balance tables, IAP/IAA/gacha config, or says "tra tài liệu dự án", "check the docs", "trong sheet có gì", "sửa sheet design". Do not use for in-repo docs already covered by another skill (read those directly), and not for adding localization keys (that is add-localize).
---

# Game Docs — the project's design sheets, through EZG MCP

The design docs live outside the repo (Google Sheets). This skill finds the
right sheet for a question, reads only what is needed, and edits a sheet only
when the user asks for that edit.

Every call goes through the **EZG MCP server**, which acts as EZG's shared
Google service account. The key stays on that server: **never** ask for, create
or point to a service-account JSON in this project.

## 0. Prerequisites

- The EZG MCP server is connected — its tools appear as `mcp__ezg__gsheet_…`
  (or under whatever name it was added with). If they are missing:
  ```bash
  claude mcp add --transport http --scope user ezg https://ezg-mcp.developer-a1f.workers.dev/mcp
  ```
  then `/mcp` → **ezg** → **Authenticate** with the @easygoing.vn account
  (restart Claude Code if the tools still do not show).
- A sheet is reachable only once its owner **shared** it with the service
  account: **Viewer** to read, **Editor** to write. `gsheet_info` without
  arguments returns the email. On a permission error, say exactly that — it is
  the first thing to check.

## Sources

`.claude/project-profile.json` → `gameDocs.sources`, one entry per doc:

```json
"gameDocs": {
  "sources": [
    {
      "key": "monetization",
      "type": "google_sheet",
      "url": "https://docs.google.com/spreadsheets/d/<id>",
      "title": "XX_Monetization",
      "description": "what this doc covers, so a question picks the right key"
    }
  ]
}
```

- Pick the source whose `description` matches the question (gacha rates, IAP
  pricing → monetization; formulas, building lists → overview…).
- Empty list = ask the user for the sheet link and what it covers, then add the
  entry (and remind them to share it with the service account).
- Older projects kept this list in `.claude/skills/game-docs/sources.json` —
  if that file still exists, move its entries into the profile (installing a
  new version of this skill replaces the whole skill folder) and delete it,
  together with any `service-account.json` next to it.

## Reading

1. **What tabs exist** — `gsheet_info { spreadsheet: <url> }`: tab names and
   sizes. Never guess a tab name.
2. **Locate** — `gsheet_search { spreadsheet, query, tab?, max_hits? }`: one line
   per hit — tab, A1 cell, value, and the whole row. Often the row already
   answers the question.
3. **Read narrowly** — `gsheet_read { spreadsheet, tab, range: "A1:F50" }`:
   TSV whose first line is JSON metadata (real range, first row number,
   `truncated` + where to continue). Tabs run to ~1000 rows: search first, then
   read a range; do not dump a whole tab when the search already answered.
   `render: "formula"` shows formulas instead of results; `format: "json"`
   keeps numbers typed.

## Writing back

These are **shared team documents** — write only when the user explicitly asks
for the edit, and always in this order:

1. **Read the current values** of every cell you will change
   (`gsheet_read` on the narrowest range that holds them).
2. **Derive each new value from what you read**, one cell at a time — never a
   blind find/replace. ID maps often overlap (e.g. `0→1` and `1→2` in the same
   column): a sequential replace cascades and corrupts the data, while a
   per-cell edit list computed from one snapshot cannot.
3. **Dry run** — `gsheet_write { spreadsheet, edits: [{tab, cell, value, expect}], dry_run: true }`
   with `expect` = the value you read. Show the user the before → after list.
4. **Write** — the same call without `dry_run`. If anyone changed one of those
   cells since you read it, the whole write is refused (`mismatches`): read
   again and rebuild the edits.
5. **Report** the cells changed. The result's `changes[].before` is the undo
   list (write them back with `gsheet_write` to revert); Google Sheets version
   history is the other undo.

`input_option` defaults to `USER_ENTERED` (numbers stay numbers, a leading `=`
is a formula — like typing); `RAW` stores text as-is. Max 500 cells per call.

## Deleting rows

Only when the user asks for rows to go. Every row below moves up, so a row
number is only good until the next delete or insert.

1. **Read the rows** (`gsheet_search` / `gsheet_read`) and note each row's key —
   the value in its key column (usually A).
2. **Dry run** — `gsheet_delete_rows { spreadsheet, tab, rows: [{row, expect: <key>}], dry_run: true }`
   (`match_column` if the key is not in A). Show the user exactly which rows go.
3. **Delete** — the same call without `dry_run`. If any row no longer holds its
   `expect`, nothing is deleted (`mismatches`): read again. All rows of one call
   go in one atomic request.
4. **Report** — the result's `removed` holds each deleted row (formulas kept);
   restoring means version history, or re-inserting rows by hand and writing
   `removed` back with `gsheet_write`. Re-read before any further edit by row
   number.

Row 1 (the header) is refused unless the user explicitly wants it gone
(`allow_header_row: true`). Max 200 rows per call. Renaming tabs, inserting rows
and deleting columns are not supported — tell the user to do those by hand.

## Answering the user

Cite the tab (and row, if precise) the numbers came from — these are live design
docs that change, so do not present values as certain if unsure which tab or
version is authoritative; ask when two tabs disagree.
