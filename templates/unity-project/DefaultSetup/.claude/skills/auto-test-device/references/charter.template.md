# Charter — <Tính năng>

**Status:** draft
<!-- draft = written by the agent from GDD/code, nobody has reviewed it · reviewed = a dev/GD has read it. Per-session
     results are NOT written here (they live in .git/auto-test-device/sessions/<session>/cases.jsonl) — this file only
     keeps oracles + cases, and is committed with the repo.
     The title placeholder, the date placeholder and the source placeholder (line "Source:") are filled in by
     `charter new`, which matches them literally (qaprofile.py) — do not translate or rename them in this template. -->
**Updated:** <YYYY-MM-DD>

Goal: <what the feature does for the player, 1–2 sentences>
Entry path: <what to tap from the main screen to get there; unlock condition; quick-unlock cheat if any>
Related state/conditions: <money, level, items, tutorial flags, time…>
Source: <GDD/TechSpec/code files/CSVs read>

## Oracles (expected values — every row needs a source; inferences are marked "inferred")

| Action | Expected | Source |
|---|---|---|
| <Buy pack 1> | <−100 diamonds, +1,000 gold> | <CoinPack.csv:2 — `qadevice.py csv CoinPack --where id=1`> |
| <…> | <…> | <…> |

## Test cases

Case IDs `P<priority>-<number>` are **fixed** (add new numbers, never renumber). The agent records results with
`qadevice.py note --case <this-file-name>/P0-01 --result pass|fail|blocked|skip [--bug N] "<note>"`.
Priorities: P0 happy path · P1 boundaries/state transitions · P2 interruptions · P3 persistence/consistency · P4 visual/environment · P5 performance.

| # | Case | Main steps | Expected | Oracle |
|---|---|---|---|---|
| P0-01 | <Full main loop, first pass> | <…> | <…> | <oracle row> |
| P0-02 | <Full main loop, second pass (does state reset?)> | <…> | <…> | <…> |
| P1-01 | <Boundary: resource = 0 / 1 unit short / exact price / surplus> | <…> | <button changes state correctly, money never negative> | <…> |
| P1-02 | <Invalid state transition (claim before done, buy while processing)> | <…> | <blocked, no doubling> | <…> |
| P2-01 | <System Back on the main popup> | `key back` | <closes exactly 1 layer> | — |
| P2-02 | <Background 5 s / 60 s mid-flow> | `app background --secs 5` | <returns to the right screen, timer correct> | <…> |
| P2-03 | <Kill + reopen after "committing" (buy/claim)> | `app restart` | <nothing lost, nothing doubled> | <…> |
| P2-04 | <Spam the main button 5 times> | `tap … --times 5` | <handled once> | <…> |
| P2-05 | <Network loss on an action that needs the server> | `net off` | <decent error, retry available, no reward granted> | <…> |
| P3-01 | <Same number matches across HUD/popup/tooltip> | <…> | <…> | <…> |
| P3-02 | <Build update keeps data> | `build update latest` | <state unchanged> | qa.verified.statePaths |
| P4-01 | <Visual audit of every screen of the feature (auto-audit + `audit`)> | <…> | <no text overflow/clipping, no exposed keys, no covered buttons> | — |
| P4-02 | <Environment matrix> | `env matrix --quick` · `pseudoloc on` | <layout holds at large font / dark / other screens> | — |
| P5-01 | <Perf when entering/leaving the feature 3 times> | `perf` | <RAM does not grow > 150 MB, FPS ≥ 25> | — |

## Case design notes (why these cases were chosen, which ones were deliberately left out)

- <…>
