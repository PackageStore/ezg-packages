---
name: package-rollback
description: Roll back the `latest` tag of a Unity (UPM) package to an older published version on the Easygoing R2 registry, without deleting anything. Use when the user wants to "downgrade a package", "roll back latest", "point latest at the old version", or recover from a broken release. Fully reversible — deletes no versions or tarballs.
---

# Roll back a UPM package's `latest`

Points `dist-tags.latest` of a package at an **older, already-published** version. Consumers
resolving "latest" then get that version. **Nothing is deleted** — every version and tarball
stays on R2 — so this is the safest reaction to a broken release and is fully reversible
(roll back again to move `latest` elsewhere).

This is the preferred fix when "the newest version is broken, get people back on the old one".
If the user instead wants the bad version *gone*, use the `package-unpublish` skill.

## Before you touch anything

1. Confirm the package **name** (`com.ezg.<something>`).
2. Inspect what's published so you pick a valid rollback target:
   ```bash
   cd scripts && node list.mjs <package>
   ```
   This lists every version, shows the current `latest`, and which tarballs exist. The target
   version **must already be published** (present in the list) — you cannot point `latest` at a
   version that doesn't exist.
3. Confirm the target version with the user (e.g. "roll `latest` back from 1.2.3 to 1.2.2?").

## Run it

**Prefer the local script** when R2 credentials are in the environment; otherwise fall back to
GitHub Actions.

### Path A — local script (preferred)

Needs `R2_ACCOUNT_ID`, `R2_ACCESS_KEY_ID`, `R2_SECRET_ACCESS_KEY` in the environment.

```bash
cd scripts

# 1. ALWAYS dry-run first; show the user the planned "X -> Y" change.
node rollback.mjs <package> <target-version> --dry-run

# 2. After the user confirms, run for real.
node rollback.mjs <package> <target-version>
```

- `--yes` skips the interactive confirmation (use only after the user has confirmed).
- The script refuses if `<target-version>` isn't a published version, and is a no-op if
  `latest` already points there.

### Path B — GitHub Actions (fallback, when no local R2 creds)

```bash
# preview (dry_run defaults to true)
gh workflow run admin.yml -f action=rollback -f package=<package> -f version=<target-version> -f dry_run=true
# after confirming, apply
gh workflow run admin.yml -f action=rollback -f package=<package> -f version=<target-version> -f dry_run=false
```

Watch it: `gh run watch` (or `gh run list --workflow=admin.yml`).

## After running

- Report the new `latest` value (printed by the script / shown in logs).
- Confirm with `node list.mjs <package>` that `latest` now points at the target.
- Remind the user this is reversible: run the rollback again with a different target to move
  `latest`, and the previously-newer version is still published (not deleted).

## Reference

- Local scripts: [scripts/rollback.mjs](../../../scripts/rollback.mjs),
  [scripts/list.mjs](../../../scripts/list.mjs)
- CI workflow: [.github/workflows/admin.yml](../../../.github/workflows/admin.yml)
- Full docs: the "Quản trị registry" section of the repo [README.md](../../../README.md)
