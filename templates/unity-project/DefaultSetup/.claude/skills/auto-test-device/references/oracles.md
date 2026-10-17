# Oracles & invariants — how to know what "correct" is

Read at STEP 3 of SKILL.md. Goal: every logic conclusion has a **measurement** and a **source**. EZG template convention
names here (`PlayerDataManager`, `TimeManager`, `UnlockFeature`, `CoinPack`…) are default examples — the project's real names
are read from `qa.conventions` / `QaProfile.md` / the code.

## 1. Oracle vs invariant

| | Oracle | Invariant |
|---|---|---|
| What | The correct value for **one specific action** ("buy pack 1 → −100 diamonds, +1,000 gold") | Something **always true** regardless of the action ("money never negative", "total money in − out = balance") |
| Source | CSV (`csv`), GDD, TechSpec, a formula in a Service | The template's system design + common sense of game economies |
| Checked | Per case, right after the action | After each **group** of cases (economy, upgrades…), at the start and end of the session |
| Wrong → | Logic bug of that action | A more serious bug: some action breaks a rule that individual cases did not catch |

The charter lists **both**: the oracle table per case, the feature's list of invariants.

## 2. Standard invariant table for games on the EZG template

| Invariant | How to check (with probe) | Without probe | Wrong = |
|---|---|---|---|
| **Currencies never negative, never NaN** | `state PlayerDataManager.PlayerResource` (or a path in `qa.verified.statePaths`) after each case group | numbers on the HUD | high (exploit/underflow) |
| **Displayed price = deducted price = CSV price** | `csv <PackCsv> --where id=<n> --col price` ↔ text on the button ↔ Δ `state` | text on the button ↔ Δ HUD number | high |
| **Received reward = CSV reward, exactly once** | `csv <RewardCsv>` ↔ Δ `state` before/after; spam `--times 5` Δ still equals one share | Δ HUD number | high |
| **IAP price on the sheet = CSV pack** | `csv <IapPackCsv>` (productId, reference price) ↔ store sheet (open, read, **cancel**) | same | high (wrong pack) / medium (wrong name) |
| **Unlocks at the right condition** | `csv UnlockFeature` (or equivalent table) ↔ `state <PlayerData unlock module>` ↔ locked/unlocked button on screen | locked/unlocked button + current level | high |
| **Time uses the game clock (`TimeManager`), not `DateTime.Now`** | `env clock +1d` (emulator) → timer/daily does not jump if the game guards against it; `app background --secs 60` → timer advances exactly 60s | same | medium (gd if the design allows it) |
| **Save round-trip** | `state` X → `app restart` → `state` X unchanged; `build update --keep-data` keeps it intact | HUD numbers before/after | critical (lost) / high (drifted) |
| **Displayed numbers consistent across screens** | the same value on HUD/popup/detail after an action — `ui --find <text>` | screenshots of the screens | medium |
| **Red dot = something to do** | red dot shown ↔ the matching `state` module has an unclaimed item; done → it disappears | same | low/medium |
| **Tracking: 1 action = 1 event** | `track --since <mark> --expect <event>`; spam → still 1 | — | medium |
| **No exceptions in game code** | `logs --all` at the end of each case group | logcat | high |

The game adds its own invariants (energy ≤ max, storage ≤ capacity, map stars ≤ total stations…) → write them into the
charter + `QaProfile.md` so later sessions can reuse them.

## 3. Getting expected values — `csv`, `state`, `call`

**`csv`** — reads exactly the CSV file the game imports (per `qa.conventions.csvConfigGlob`), never the GDD instead:

```bash
$QA csv CoinPack                              # whole table (file name without extension, case-insensitive)
$QA csv CoinPack --where id=2 --col price     # one cell — cite it in the charter as "csv CoinPack:2"
$QA csv UnlockFeature --where feature=Shop    # unlock condition
```

GDD differs from CSV → do not pick a side yourself: bug `--type gd` quoting both sources, and check the feature against the
CSV (that is what the game runs).

**`state`** — reads the real state, before and after:

```bash
$QA state PlayerDataManager.PlayerResource                       # module snapshot (the tool truncates long output — --out f.json if you need to parse it)
$QA state PlayerDataManager.PlayerResource.Gold                  # a single value
```

- Paths verified in earlier sessions: `qa.verified.statePaths` / `QaProfile.md` — use them directly. A new path that works →
  `stop` records it.
- Read the PlayerData module code (`Scripts/Data/Player*.cs`) to know the right member; do not guess.
- Snapshot before the action → action → snapshot after → Δ only in the fields that action must change. Other fields changing
  too = a finding (side effect).

**`call`** — only call **read**/aggregate functions (static Service: `GetTotalX`, `CanAffordY`, `IsUnlocked`) to check
invariants; write functions (add money, unlock) are only for setting up conditions (SKILL.md rule 1) and go into
`--precondition`.

## 4. Tracking / analytics

Important actions (purchase, reward claim, screen open, ad view, level complete) must emit the right event name, the right
number of times:

```bash
$QA mark "P0-02 - mua gói 1"
$QA tap --text "Mua" --expect "Đã mua" --as "Bấm Mua gói 1"
$QA track --since "P0-02 - mua gói 1" --expect purchase_pack       # event name from TrackingEventName (or the type in qa.conventions.trackingEventNameType)
```

- `track` filters logs by `qa.conventions.trackingLogPattern` (default `Tracking|Firebase|AppsFlyer`); the game logs tracking
  differently → fix the pattern in the profile, note it in `QaProfile.md`.
- Missing event → medium. Duplicate event on spam → medium (skews the data). Wrong main parameter (pack id, price) → medium.
- A release build with tracking logs off → `track` sees nothing: write "không kiểm được tracking (build release)", not a bug.

## 5. Save-migration — a build update keeps the data

When the repo/device has a build **older** than the one under test (an old artifact, the store version installed):

```bash
# running the old build with a save that has progress (or set it up with cheats)
$QA build update <apk|aab|path> --keep-data      # installs over it, records version before/after, snapshots verified.statePaths before/after → migration.md
```

Read `migration.md`: every field in the snapshot must be **equal** or **changed with an explanation** (changelog/new CSV
changed a formula → derived values change). Lost progress, reset to defaults, lost items/currency → **critical**. The first
screen after the update ("what's new" popup, tutorial repeating, long loading) → check by eye. No old build → mark the
migration case `skip`, remind the dev to keep the previous build's artifact in the repo/CI.

## 6. Without the probe

You still have oracles: `csv` reads from the repo, numbers off the original `.png` (Read the original image, not the
downscaled view), `logs` for exceptions/tracking. Lost: exact `state`, `call`, `tap --text`. Write "hộp đen" (black-box) in
the report so the dev rebuilds with the probe.
