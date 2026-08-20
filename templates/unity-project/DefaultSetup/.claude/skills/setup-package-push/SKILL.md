---
name: setup-package-push
description: One-time, per-machine setup that lets /package-module push to the ezg-packages monorepo over SSH instead of a GitHub PAT. Checks/installs the GitHub CLI (gh), runs its one-time browser login (gh auth login --git-protocol ssh --web), generates an SSH key if the machine doesn't have one, registers it on the user's GitHub account (gh ssh-key add), and verifies both SSH auth to github.com and Write access to PackageStore/ezg-packages. Idempotent — safe to re-run, exits early if everything already works. Used when the user says "setup package push", "cài SSH cho package-module", "tôi chưa có SSH", "cấp quyền deploy package không cần PAT". Requires the user already has (or an admin has granted) GitHub Write access to PackageStore/ezg-packages — this skill cannot grant that, only prove/verify it.
---

# Setup Package Push — SSH onboarding for `/package-module`

`/package-module` pushes straight to `main` of the `ezg-packages` monorepo. It needs git write
access to that repo, resolved in this priority order (see that skill's **Git authentication**
section): **SSH first, GitHub PAT as a fallback**. This skill sets up the SSH path once per
machine so the user never has to create/store a PAT.

Nothing here touches the current repo or any game code — it only touches this machine's SSH
keys and the user's own GitHub account settings.

**Usually invoked automatically, not by hand.** `/package-module` auto-invokes this skill inline
(at its STEP 4.0) the first time it finds SSH not set up — the user does not need to run
`/setup-package-push` themselves first. Running it directly (e.g. to pre-provision a new
machine, or to re-diagnose after access changes) is also fine — it's idempotent either way.

**One human-interactive step, by design.** `gh auth login` requires the user to approve a
one-time code in their browser (OAuth device flow) — this is a GitHub security control, not
something this skill can or should script around. Everything else below is fully automated.

> **If running inside Claude Code:** `gh auth login --web` blocks waiting for browser approval
> and can hang a tool call. Ask the user to run that one command themselves in their own
> terminal (or paste it after `!` in the Claude Code prompt) — then continue the rest of this
> skill once they confirm it succeeded.

---

## Pipeline

```
[0] CHECK      → already working? (ssh -T git@github.com) → skip to [4]
[1] GH CLI     → ensure `gh` is installed; ensure `gh auth login` is done
[2] SSH KEY    → default key present? register it. Missing? generate + register.
[3] RE-VERIFY  → ssh -T git@github.com again
[4] REPO PERM  → confirm Write access to PackageStore/ezg-packages (report only — cannot grant it)
[5] REPORT     → done; /package-module will auto-detect SSH from now on
```

---

## STEP 0 — Check if this machine already works

```bash
# macOS zsh/bash
if ssh -o BatchMode=yes -o ConnectTimeout=5 -T git@github.com 2>&1 | grep -qi 'successfully authenticated'; then
  echo "SSH to GitHub already works on this machine — nothing to set up."
  # jump to STEP 4 (repo permission check)
fi
```
```powershell
# Windows PowerShell
$sshTest = ssh -o BatchMode=yes -o ConnectTimeout=5 -T git@github.com 2>&1
if ($sshTest -match 'successfully authenticated') {
  Write-Host "SSH to GitHub already works on this machine — nothing to set up."
  # jump to STEP 4 (repo permission check)
}
```
`ssh -T git@github.com` always exits non-zero (GitHub closes the shell channel after the
greeting) — check the **text**, never the exit code.

If this already passes, skip straight to STEP 4. Do not touch existing keys/config.

---

## STEP 1 — GitHub CLI (`gh`)

1. **Install if missing:**
   ```bash
   # macOS / Linux
   if ! command -v gh >/dev/null 2>&1; then
     if command -v brew >/dev/null 2>&1; then
       brew install gh
     else
       echo "Homebrew not found. Install gh manually: https://github.com/cli/cli#installation" >&2
       exit 1
     fi
   fi
   ```
   ```powershell
   # Windows
   if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
     if (Get-Command winget -ErrorAction SilentlyContinue) {
       winget install --id GitHub.cli -e --source winget
     } else {
       Write-Host "winget not found. Install gh manually: https://github.com/cli/cli#installation"
       exit 1
     }
   }
   ```

2. **Login if not already authenticated:**
   ```bash
   if ! gh auth status --hostname github.com >/dev/null 2>&1; then
     gh auth login --hostname github.com --git-protocol ssh --web
   fi
   ```
   ```powershell
   if (-not (gh auth status --hostname github.com *>$null; $?)) {
     gh auth login --hostname github.com --git-protocol ssh --web
   }
   ```
   Per the note at the top: if you (the agent) cannot complete an interactive browser approval,
   hand this exact command to the user and wait for them to confirm it finished before continuing.

---

## STEP 2 — SSH key

Reuse the **default** key path (`~/.ssh/id_ed25519`) so plain `ssh`/`git` picks it up
automatically — no `~/.ssh/config` edits, no risk of shadowing a key the user already has
configured for something else.

```bash
# macOS zsh/bash
key="$HOME/.ssh/id_ed25519"
if [ ! -f "$key" ]; then
  ssh-keygen -t ed25519 -f "$key" -N "" -C "$(whoami)@$(hostname)-ezg-packages"
fi
title="ezg-packages ($(hostname))"
if ! gh ssh-key list 2>/dev/null | grep -qF "$(cut -d' ' -f2 "$key.pub")"; then
  gh ssh-key add "$key.pub" --title "$title"
fi
```
```powershell
# Windows PowerShell
$key = Join-Path $env:USERPROFILE '.ssh\id_ed25519'
if (-not (Test-Path $key)) {
  ssh-keygen -t ed25519 -f $key -N '""' -C "$env:USERNAME@$env:COMPUTERNAME-ezg-packages"
}
$title = "ezg-packages ($env:COMPUTERNAME)"
$pubKeyToken = (Get-Content "$key.pub") -split ' ' | Select-Object -Index 1
$existing = gh ssh-key list 2>$null
if (-not ($existing -match [regex]::Escape($pubKeyToken))) {
  gh ssh-key add "$key.pub" --title $title
}
```

- If `~/.ssh/id_ed25519` already exists but is **not yet registered** on GitHub (common case: a
  dev generated a key long ago and never uploaded it), this still adds it — no new key is
  created, the existing one is just registered.
- If a default key exists, is already listed via `gh ssh-key list`, but STEP 0/3's `ssh -T`
  still fails → stop here. Don't guess further (wrong file permissions, ssh-agent not running,
  a custom `~/.ssh/config` routing github.com elsewhere, etc. are all machine-specific — report
  what you found and ask the user to check it, rather than rewriting their SSH config).

---

## STEP 3 — Re-verify

Re-run the STEP 0 check. Must now print "successfully authenticated" (or whatever GitHub's
greeting says for the user's account) before moving on.

---

## STEP 4 — Confirm write access to `PackageStore/ezg-packages`

SSH auth proves the account can talk to GitHub — it does **not** prove that account can push to
this specific repo. Check it if `gh` is available and authenticated:

```bash
perm="$(gh api repos/PackageStore/ezg-packages --jq '.permissions.push' 2>/dev/null)"
```
```powershell
$perm = gh api repos/PackageStore/ezg-packages --jq '.permissions.push' 2>$null
```

- `true` → good, report success.
- `false` / API error (404, 403) → **this skill cannot grant repo access.** Tell the user:
  *"SSH is set up correctly, but this GitHub account doesn't have Write access to
  `PackageStore/ezg-packages` yet. Ask an org admin to add you as a collaborator with the Write
  role."* Stop here — do not attempt any workaround (no shared/admin token, no PAT as a
  substitute for real authorization).
- `gh` unavailable/not authenticated → skip this check and say so; the real test happens the
  first time `/package-module` pushes.

---

## STEP 5 — Report

One or two sentences: SSH is set up and verified (or what's still missing + who to ask), and
that `/package-module` will now use it automatically — nothing to configure in that skill, no
`EZG_PACKAGES_PAT` needed on this machine.

---

## Guardrails

- **Never** create, ask for, or store a GitHub PAT here — that is the fallback path the other
  skill owns, not this one's job.
- **Never** rewrite an existing `~/.ssh/config` or overwrite an existing key file.
- **Never** attempt to grant repo access yourself (adding collaborators, changing team
  membership) — that's an admin action outside this skill's scope; only report status.
- Idempotent: safe to re-run any time (e.g. new machine, key rotated, lost access) — STEP 0
  always short-circuits when nothing needs to change.
