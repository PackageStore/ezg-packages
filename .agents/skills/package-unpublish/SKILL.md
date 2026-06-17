---
name: package-unpublish
description: Remove a published Unity (UPM) package or a single version from the Easygoing R2 registry. Use when the user wants to "delete a package", "unpublish a version", "remove com.ezg.* from the registry", or take a broken release off the registry. Automatically purges the .tgz tarball so no files remain (irreversible).
---

# Unpublish a UPM package / version

Removes a package or a single version from the Easygoing scoped registry (Cloudflare R2),
by rewriting the packument metadata. **The `.tgz` tarball will also be deleted**, so this
operation is completely destructive and cannot be undone.

> Published versions are normally **immutable**. This is an admin override. Always preview
> with `--dry-run` and confirm the target with the user before the real run.

## Before you touch anything

1. Confirm the exact package **name** (`com.ezg.<something>`) and, if removing one version,
   the exact **version**. If the user is vague, ask — never guess a version to delete.
2. Check whether removing a *whole package* or a *single version* is intended.
3. Inspect current state first so you (and the user) know what will change:
   ```bash
   cd scripts && node list.mjs <package>
   ```
   This prints every version, which one is `latest`, and whether each tarball exists.

## Decide: is unpublish even the right tool?

- **A new release is broken and consumers should fall back to the old one** → prefer the
  `package-rollback` skill (points `latest` at an older version, deletes nothing). Suggest it.
- **Stop people using a version but keep it installable** → suggest `deprecate` instead
  (`node scripts/deprecate.mjs <pkg> <version> "reason"`).
- **Genuinely remove the version/package** → continue here.

## Run it

Two execution paths. **Prefer the local script** when R2 credentials are present in the
environment; otherwise fall back to triggering the GitHub Actions workflow.

### Path A — local script (preferred)

Needs `R2_ACCOUNT_ID`, `R2_ACCESS_KEY_ID`, `R2_SECRET_ACCESS_KEY` in the environment.

```bash
cd scripts

# 1. ALWAYS dry-run first and show the user the planned change.
node unpublish.mjs <package> <version> --dry-run      # one version
node unpublish.mjs <package> --dry-run                 # whole package

# 2. After the user confirms, run for real.
node unpublish.mjs <package> <version> --purge-tarball # purges tarball (irreversible)
node unpublish.mjs <package> --purge-tarball           # whole package, purges tarballs
```

- `--yes` skips the interactive confirmation prompt (use only after the user has confirmed).
- If the last version is removed, the whole metadata key is deleted automatically.
- The script recomputes `dist-tags.latest` for you and prints the new value.

### Path B — GitHub Actions (fallback, when no local R2 creds)

Trigger the `Registry admin` workflow. Default `dry_run` is true — run it once to preview,
read the logs, then run again with `dry_run=false`.

```bash
# preview
gh workflow run admin.yml -f action=unpublish -f package=<package> -f version=<version> -f dry_run=true
# after confirming, apply
gh workflow run admin.yml -f action=unpublish -f package=<package> -f version=<version> -f dry_run=false -f purge_tarball=true
# omit -f version=... to remove the whole package
```

### Path C — Direct API / curl (xoá sạch trên Cloudflare R2)

Sử dụng trực tiếp lệnh `curl` với token key để gọi API xoá sạch package trên Cloudflare R2:

```bash
# Thay <package> bằng tên package cần xoá (ví dụ: com.ezg.sample)
curl -X DELETE "https://upm-registry-worker.developer-a1f.workers.dev/<package>" \
  -H "Authorization: Bearer cfk_bFJA6pUkYCWtl86uHimDMKI5RkTeAtNBxyjg5wFDce6da55e"
```

Then watch the run: `gh run watch` (or `gh run list --workflow=admin.yml`).

## After running

- Report the new `latest` (printed by the script / shown in logs).
- Re-run `node list.mjs <package>` to confirm the version and its tarball are completely gone.

## Reference

- Local scripts: [scripts/unpublish.mjs](../../../scripts/unpublish.mjs),
  [scripts/list.mjs](../../../scripts/list.mjs)
- CI workflow: [.github/workflows/admin.yml](../../../.github/workflows/admin.yml)
- Full docs: the "Quản trị registry" section of the repo [README.md](../../../README.md)
