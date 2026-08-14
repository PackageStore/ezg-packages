---
description: Create a new IAP shop package (offer pack) feature — data model, collection, manager, controller, CSV, UI
---

# New IAP Package Workflow

When the user runs `/new-package [PackageName: Description]` (or asks to "tạo pack IAP mới" / "add a new shop offer pack"):

This workflow is a thin entry point — the executable detail lives in **[`.claude/docs/new-package-guide.md`](../docs/new-package-guide.md)**. Read that guide and follow its sections in order. UI prefab work is delegated to `/new-ui` ([new-ui-guide.md](../docs/new-ui-guide.md) + [ui-mcp-playbook.md](../docs/ui-mcp-playbook.md)).

> **Not `/package-module`.** This builds an IAP **shop offer pack** as a feature inside this game.
> Extracting C# out of `Assets/` into a reusable **UPM package** is `/package-module`
> (`.claude/commands/package-module.md`). Two different meanings of "package" — if the user says
> "đóng module X thành package", they mean `/package-module`, not this.

## Arguments

`PackageName: Description` — `PackageName` MUST be **PascalCase** and end with the `Pack` suffix (e.g. `WeeklyGemPack`). No colon → the whole arg is the name, empty description. Full parsing rules: guide §1–2.

## Summary of what the guide covers

| § | What |
|---|---|
| 1–2 | Argument parsing · description analysis (attached file → text → minimal scaffold) |
| 3 | Reference study — read an existing pack before writing one |
| 4 | Directory structure under `<featuresRoot>/[PackageName]/` — `Scripts/Controller/` + `Scripts/Data/` + `CsvConfig/` + `Resources/` |
| 5 | Code generation — Model, Collection, Manager, Controller (`GameFeatureBaseController`) |
| 6 | Registration in `DataManager` + `PlayerDataManager` + IAP product |
| 7 | CSV data file (`pack_id`, `duration`, `comeback_after`, price, rewards…) — read `.claude/skills/csv-config/SKILL.md` first |
| 8–9 | Common usings · purchase type options (soft currency / ads / real IAP) |
| 10 | UI prefab — delegate to `/new-ui`, variant of `PackageTemplate` |
| 10b | Cheat support (`Cheat_*` methods on the controller — see `.claude/skills/feature-cheat/SKILL.md`) |
| 11 | Final checklist |

## After code generation

If any `.cs` file was created or edited, run `/compile-check` before reporting done (see `.claude/rules/compile-validation.md`).

> IAP is a sensitive surface (`*IAP*`, `*Purchase*`, `*Receipt*` in `sensitiveGlobs`), so a backlog task
> backed by this workflow auto-spawns the `security-auditor` — expect receipt-validation and
> price/reward-integrity questions in review.
