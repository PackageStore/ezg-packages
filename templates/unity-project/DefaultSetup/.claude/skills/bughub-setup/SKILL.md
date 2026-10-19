---
name: bughub-setup
description: Install BugHub (the shared Bug Logger — a server that turns bugs QA submits into a GitLab Issue + a Discord thread) for the current Unity project in one command, idempotent — checks the `bughub` MCP is connected, (admin account) registers/fixes the project on the server via `bughub_admin_project_upsert` (plus assigns `members` when the dev says "A làm lead, B QA…" / "A is lead, B QA…"), (non-admin) only checks the project exists via `GET /v1/projects/<code>/form`, checks the `com.ezg.buglogger` package, writes `projectCode` into the game's `BugLoggerSettings` asset and `bugHub.projectCode` in `.claude/project-profile.json`, checks the cheat gate + prints how to add QA devices to the cheat allowlist (Remote Config), reminds to install `/auto-clear` for `/fix-bug --watch`. The dev only answers the Discord forum ID (and confirms the project code if there is none yet). Re-running on an installed project → every step `ok`, nothing duplicated. Use when the user says "/bughub-setup", "setup bughub", "cài bughub", "install bughub", "cài bug logger cho project", "install bug logger for the project", "đăng ký project lên BugHub", "register the project on BugHub", "gắn project vào BugHub". Does NOT fix bugs (→ `/fix-bug`), does NOT install/upgrade CI (→ EZG MCP `autobuild_setup`), does NOT change server config beyond `bughub_admin_project_upsert`.
---

# BugHub Setup — install BugHub for the current project

Architecture (the contract, source of truth): BugHub is **one** Cloudflare Worker for every project. The game
client only configures the **project code** (`projectCode`); the server does everything involving GitLab/Discord.
The dev machine talks to the server through the **`bughub` MCP** (Claude Code exposes its tools as
`mcp__bughub__bughub_*`) and exactly one public endpoint `GET /v1/projects/<code>/form`.

**Hard rules of this skill:**

1. **Never** use the GitLab/Discord CLI or REST API in any form; do not touch server secrets/config beyond
   `bughub_admin_project_upsert`. Server operations go only through `bughub_*` tools or
   `GET <endpoint>/v1/projects/<code>/form`. (`git fetch`/`git show` of the build branch in step 7 is plain git, not an API.)
2. **Idempotent:** every step reads the current state first and only writes when it differs. A second run on
   an installed project → every step `ok`, no duplicate asset/key/project.
3. **Don't ask what can be inferred.** Only ask the dev: confirm the project code (when there is none), the
   Discord forum ID, the feedback channel ID (optional), `gitlabProject` (only when `origin` is not gitlab.com) —
   and only when the project is **not yet** registered on the server.
4. A failed step does **not** stop the whole skill (except step 2): record that step's result, continue with
   the next step, collect everything in the step 9 report table.

Each step's result is one of: `ok` (already correct) · `created` (newly created) · `updated` (fixed a differing
value) · `skipped (<reason>)` · `dev to do: <specific task>`.

**Variables used throughout:**

| Variable | Source |
|---|---|
| `<endpoint>` | env `BUGHUB_ENDPOINT` if set; otherwise the `DEFAULT_ENDPOINT=` line in `.claude/scripts/bughub-wait.sh` (read the file, don't type it from memory); if that file doesn't exist → `https://bug-reporter.developer-a1f.workers.dev`. Strip the trailing `/`. Must match `^https://[A-Za-z0-9.-]+(:[0-9]+)?$` (http only for `localhost`/`127.0.0.1`); otherwise → stop and report the odd value. A host other than `bug-reporter.developer-a1f.workers.dev` → state the host explicitly in every command printed for the dev (the `claude mcp add` command leads to a Google sign-in — the dev must see where they are signing in) |
| `<code>` | step 1 |

Commands in this skill are written for bash. On Windows run them in Git Bash, replacing `python3` with `py`;
steps that have their own PowerShell command list it alongside.

---

## Step 1 — Project code

```bash
python3 .claude/scripts/project_profile.py bugHub      # → {"projectCode": "..."}
```

- `projectCode` has a value → use it as `<code>`. Check it matches the regex `^[A-Za-z0-9_-]+$` (same rule as
  `bughub-wait.sh`); mismatch → stop this step, `dev to do: fix bugHub.projectCode`.
- Empty (or the `project_profile.py` script is missing) → **propose** a code from the project codename: the last
  segment of the `origin` path (without `.git`), or the repo folder name when there is no `origin` — UPPERCASED
  (`sm007` → `SM007`). If step 5 finds a `BugLoggerSettings` asset with a non-empty `projectCode`, propose that
  value. Ask the dev to confirm or type another code; validate with the same regex. Don't settle it yourself
  before the dev answers.
- The profile and the `BugLoggerSettings` asset carry two **different** codes → ask the dev which to use, don't overwrite on your own.

The server compares codes case-insensitively; always write the form the dev confirmed.

## Step 2 — Is the `bughub` MCP connected? (the only step allowed to stop the whole skill)

Look for the `bughub_whoami` tool in the tool list (deferred tool → ToolSearch query `bughub`). Missing, or
calling `bughub_whoami` returns an auth error / 401 → **stop the skill**, print exactly:

```
claude mcp add --transport http --scope user bughub <endpoint>/mcp
```

then: in Claude Code type `/mcp` → pick `bughub` → **Authenticate** (sign in with a Google `@easygoing.vn`
account), then run `/bughub-setup` again. After adding the MCP the Claude Code session must be reopened for the
tools to appear. Don't run `claude mcp add` on the dev's behalf (it edits the dev machine's user-level config).

Network error / 5xx (not an auth error) → also stop, report that the server is not responding, with the verbatim error.

## Step 3 — Project on the server

`bughub_whoami` → `{email, person, isAdmin}`.

### 3a. Admin (`isAdmin = true`)

1. `bughub_admin_projects` → find the row whose code matches `<code>` (case-insensitive).
2. **Exists** → `bughub_admin_project_doctor(code: <code>)` (read-only).
   - Every doctor step `ok` and the project row has intake on → result `ok`. Do **not** call upsert, do **not** ask the dev anything.
   - Some step differs / `error` / `warn` (e.g. step 6 `discord_command`: the bot does not have the "Đưa vào BugHub" command yet) → call
     `bughub_admin_project_upsert` again with `code`, `gitlabProject`,
     `forumChannelId` (and `feedbackChannelId` if any) **taken from the existing project row** — don't ask the dev again —
     and `intakeEnabled` **kept at its current value**. Only when a value is missing from that row, ask the dev for
     exactly that value. Print the full result table. Result `updated` (or `dev to do` if an `error` remains, see the error table below).
   - Intake is **off** → another admin may have turned it off on purpose (spam, out of quota). Ask the dev whether to
     turn it back on; only on yes upsert with `intakeEnabled: true` (other values taken from the existing row as above),
     otherwise result `dev to do: intake <code> is off — turn it on in /admin when ready`.
3. **Does not exist** → gather the inputs, then `bughub_admin_project_upsert`:
   - `gitlabProject` — inferred from `git remote get-url origin`:
     ```bash
     git remote get-url origin 2>/dev/null | python3 -c "import re,sys; u=sys.stdin.read().strip(); m=re.match(r'^(?:https?://(?:[^@/]+@)?gitlab\.com/|git@gitlab\.com:|ssh://git@gitlab\.com/)(.+?)(?:\.git)?/?$', u); print(m.group(1) if m else '')"
     ```
     Prints `group/project` (e.g. `ezg-sm-space/sm007`). Empty (no `origin`, or `origin` is not
     gitlab.com) → ask the dev for the GitLab project path as `group/project`. The value (inferred or typed) must
     match `^[A-Za-z0-9_.-]+(/[A-Za-z0-9_.-]+)+$` and must not contain `..`; otherwise → ask again once.
   - `forumChannelId` — **ask the dev** for the project's Discord forum ID (right-click the forum → Copy Channel ID; needs
     Developer Mode on). Validate `^[0-9]{17,20}$`; otherwise → ask again once.
   - `feedbackChannelId` — ask the dev, optional (Enter to skip = feedback goes to the default report channel).
     If given, validate as above.
   - Call `bughub_admin_project_upsert(code, gitlabProject, forumChannelId, feedbackChannelId?, intakeEnabled: true)`
     (with `members` if the dev named members — item 5).
   - **Print the full per-step result table** the server returns (`ok | created | fixed | warn: <reason> | error: <reason>`)
     — no summarizing, no dropped rows. Every row `ok/created/fixed` → result `created`; `warn` rows don't block
     (see the error table below).
4. After 2/3: `GET /form` (command in 3b) must return `200` with `intakeEnabled` true (unless the dev just chose to keep
   intake off). Otherwise → note it in the report.
5. **Members** — only when the dev named them in the command/chat ("A làm lead, B QA, C dev" / "A is lead, B QA, C dev");
   not named → skip, **don't ask**. Pass `members: [{who, roles}]` in the same `bughub_admin_project_upsert` call of item 2/3
   (project exists and doctor `ok` → call `bughub_admin_project_upsert(code, members)` separately). `who` = exactly what
   the dev typed (name, email, the part before `@`, Discord id); `roles` ⊂ `lead | dev | qa | art | gd`, replaces that
   person's roles **in this project** (`[]` = remove from the project), people not named stay unchanged. The server looks up
   the directory and checks permissions **before** writing anything:
   - `unknown_person` → `bughub_admin_people_list(query: <who>)` for a fuzzy match, let the dev pick; nobody →
     `dev to do: a BugHub owner adds <who> to the directory (bughub_admin_people_sync)`.
   - `ambiguous_person` → print `candidates` (name + email), ask the dev to pick, then call again with that person's email.
   - `inactive_person` → `dev to do: reactivate <name> (bughub_admin_person_upsert active: true)`.
   Print the resulting `members[]` table (`added | changed | removed | unchanged | error`). Step result: `updated` if
   anyone changed.

**`error` rows in the upsert table** — the server only turns intake on when steps 1–4 have no error, so these are manual tasks:

| Error at step | Record as `dev to do:` |
|---|---|
| 1 (resolve GitLab / permissions) | add the BugHub bot account as **Maintainer** of GitLab project `<gitlabProject>`, then rerun the skill |
| 2–3 (label / webhook) | usually the same Maintainer-permission cause as above |
| 4 (Discord) | grant the bot on the forum: View Channel, Send Messages, Send Messages in Threads, Create Public Threads, Attach Files, Manage Threads; check the forum ID is right; the forum has room for the status tags (max 20 tags) |
| `not_admin` (tool result `isError`) | the account just lost admin rights → do as in 3b |
| `forbidden` `people.assign-lead` | only an owner/admin can assign or remove the `lead` role — ask a BugHub admin |
| 6 `discord_command` = `warn: …` (doesn't block intake) | the bot's "Đưa vào BugHub" message-menu command could not be registered — only affects moving threads QA opened themselves into BugHub; report it to a BugHub admin with the full `warn` row (usually the bot token / the Worker's `DISCORD_APPLICATION_ID`) |

### 3b. Not an admin

Do not call any `bughub_admin_*` tool. Only check:

```bash
TMP=$(mktemp)
HTTP=$(curl -sS --max-time 15 -o "$TMP" -w '%{http_code}' "<endpoint>/v1/projects/<code>/form")
INTAKE=$(python3 -c "import json,sys; print(str(json.load(open(sys.argv[1])).get('intakeEnabled')).lower())" "$TMP" 2>/dev/null)
rm -f "$TMP"; echo "$HTTP intake=$INTAKE"
```

| Result | Record |
|---|---|
| `200 intake=true` | `ok` |
| `200 intake=false` | `dev to do: ask a BugHub admin to run /bughub-setup (or the /admin page) to turn on intake for <code>` |
| `404` | `dev to do: project <code> is not registered or has intake off (the server returns 404 for both) — ask a BugHub admin to run /bughub-setup on this repo` (send along the `gitlabProject` inferred in 3a + the Discord forum ID) |
| other / network error | `skipped (server not responding: <status/error>)` |

## Step 4 — Package `com.ezg.buglogger`

```bash
[ -f Packages/com.ezg.buglogger/package.json ] && echo "embedded"
grep -q '"com.ezg.buglogger"' Packages/manifest.json && echo "manifest"
```

- Either one present → `ok`.
- Neither:
  1. Search the Feature Hub: MCP `ezg` → `search_feature_hub` (query `buglogger`).
  2. Found → `get_install_steps` for that package. Check the `install` snippet really installs `com.ezg.buglogger` (it
     names exactly that package id and does nothing else), show it to the dev and get a one-line confirmation, only then
     run it via `unity_execute_code` (needs this project's Editor open, see step 5 for how to pick the instance); confirm
     with the `verify` snippet. Result
     `created`. No Editor → print the install steps, result `dev to do: install com.ezg.buglogger with the steps above`.
  3. Not published (not found) → `dev to do: com.ezg.buglogger is not published on the Feature Hub — publish it
     with /package-module or copy the embedded package from a project that has it`. Step 5 will be `skipped`.

Don't hand-edit `Packages/manifest.json`.

## Step 5 — `BugLoggerSettings` asset

The client loads the asset with `Resources.Load("BugLoggerSettings")`, so the asset must be named exactly
`BugLoggerSettings.asset` and sit **directly** inside a `Resources/` folder. The serialized field `projectCode` is
the package's contract.

**Default path when it must be created** (`<featuresRoot>` = `python3 .claude/scripts/project_profile.py featuresRoot`):
`<featuresRoot>/_Shared/BugLogger/Resources/BugLoggerSettings.asset` if the folder `<featuresRoot>/_Shared/BugLogger/`
already exists (older projects still build their own Bug Logger screen there — since `com.ezg.buglogger` 0.2.0 the UI
lives in the package), otherwise `Assets/Resources/BugLoggerSettings.asset`. This path is
inserted into a C# string in 5a, so it must match `^Assets/[A-Za-z0-9_./ -]+/Resources/BugLoggerSettings\.asset$`;
otherwise → use 5b.

### 5a. This project's Unity Editor is available (main path)

1. `unity_list_instances` → pick the instance whose `projectPath` **matches this repo's folder** (`git rev-parse --show-toplevel`);
   compare paths, not names — the port of a closed Editor may have been taken by another project. No matching instance → 5b.
2. **Editor shared with another session?** `unity_agents_list`. Another agent active → still OK to run
   (creating/editing one `.asset` imports no script, no domain reload), but do **not** stop their Play mode,
   and note in the report that the Editor is shared. `unity_editor_state` compiling → wait until it finishes.
   The snippet deliberately does **not** call `AssetDatabase.Refresh()` (Refresh would also import `.cs` files another
   session is half-way through editing → compile + domain reload): folders are created with `AssetDatabase.CreateFolder`,
   the asset with `CreateAsset`.
3. Run `unity_execute_code` with the snippet below, replacing `__CODE__` and `__PATH__`. **The payload must be ASCII
   only** (Unity MCP mangles non-ASCII characters) — the project code and path must both be ASCII, and so must comments
   in the snippet; a path with odd characters → go to 5b.

```csharp
// bughub-setup step 5: create or update Resources/BugLoggerSettings.asset (ASCII only)
const string CODE = "__CODE__";
const string DEFAULT_PATH = "__PATH__";
System.Type type = null;
foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
{
    type = asm.GetType("Ezg.BugLogger.BugLoggerSettings");
    if (type != null) break;
}
if (type == null) return "error: type Ezg.BugLogger.BugLoggerSettings not found (package missing or not compiled)";

var inResources = new System.Collections.Generic.List<string>();
var elsewhere = new System.Collections.Generic.List<string>();
foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:" + type.Name))
{
    var p = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
    if (p.EndsWith("/Resources/BugLoggerSettings.asset")) inResources.Add(p); else elsewhere.Add(p);
}
if (inResources.Count > 1) return "error: duplicate settings assets: " + string.Join(", ", inResources);

string path;
string result;
UnityEngine.Object asset;
if (inResources.Count == 1)
{
    path = inResources[0];
    asset = UnityEditor.AssetDatabase.LoadAssetAtPath(path, type);
    if (asset == null) return "error: cannot load " + path;
    result = "ok";
}
else
{
    path = DEFAULT_PATH;
    var parts = path.Split('/');
    var folder = parts[0];
    for (int i = 1; i < parts.Length - 1; i++)
    {
        var next = folder + "/" + parts[i];
        if (!UnityEditor.AssetDatabase.IsValidFolder(next)) UnityEditor.AssetDatabase.CreateFolder(folder, parts[i]);
        folder = next;
    }
    asset = UnityEngine.ScriptableObject.CreateInstance(type);
    UnityEditor.AssetDatabase.CreateAsset(asset, path);
    result = "created";
}

var so = new UnityEditor.SerializedObject(asset);
var prop = so.FindProperty("projectCode");
if (prop == null) return "error: field projectCode not found on " + type.FullName;
if (prop.stringValue != CODE)
{
    prop.stringValue = CODE;
    so.ApplyModifiedPropertiesWithoutUndo();
    UnityEditor.EditorUtility.SetDirty(asset);
    if (result == "ok") result = "updated";
}
UnityEditor.AssetDatabase.SaveAssets();
var warn = elsewhere.Count > 0 ? " | warn: settings outside Resources (not loaded at runtime): " + string.Join(", ", elsewhere) : "";
return result + " " + path + " projectCode=" + CODE + warn;
```

| Returned result | Record |
|---|---|
| `ok …` / `updated …` / `created …` | that exact word + path. `created`/`updated` → remind the dev to commit the asset (and the `.meta` when `created`) |
| `error: … not found (package …)` | `skipped (package not compiled — see step 4)` |
| `error: duplicate …` | `dev to do: delete the extras, keep only one Resources/BugLoggerSettings.asset` — don't delete on your own |
| `error: cannot load …` / `error: field projectCode …` | `dev to do: check asset <path> (broken script reference?)` — don't delete/recreate on your own |
| `… warn: settings outside Resources …` | keep the main result, mention the warning in the report — don't delete/move on your own |

Re-running the snippet on an already-correct asset → returns `ok`, creates no second asset, writes no file.

### 5b. No Editor (MCP not connected / Editor not open / only another project's Editor)

1. Find an existing asset by the script GUID (no Editor needed):
   ```bash
   META=$(ls Packages/com.ezg.buglogger/Runtime/Settings/BugLoggerSettings.cs.meta \
            Library/PackageCache/com.ezg.buglogger*/Runtime/Settings/BugLoggerSettings.cs.meta 2>/dev/null | head -1)
   GUID=$([ -n "$META" ] && sed -n 's/^guid: //p' "$META")
   if [ -z "$GUID" ]; then echo "NO-META"; else grep -rl --include='*.asset' "guid: $GUID" Assets; fi
   ```
2. Exactly one asset named `BugLoggerSettings.asset` found in a `Resources/` folder:
   - The line `  projectCode: <code>` is already correct → `ok`.
   - Differs → edit **exactly that line** in the YAML (touch no other line), result `updated`:
     ```bash
     python3 - "<asset path>" "<code>" <<'PY'
     import re, sys
     path, code = sys.argv[1], sys.argv[2]
     text = open(path, encoding="utf-8").read()
     new, n = re.subn(r"(?m)^(  projectCode:).*$", lambda m: m.group(1) + " " + code, text, count=1)
     if n != 1:
         sys.exit("projectCode line not found")
     open(path, "w", encoding="utf-8", newline="").write(new)
     PY
     ```
3. Not found (or it prints `NO-META` — the `.cs.meta` could not be found) → do **not** write a new asset YAML yourself. Result
   `dev to do: open the Editor and rerun /bughub-setup, or create it by hand: Assets > Create > Ezg > Bug Logger Settings
   at <default path>, set Project Code = <code>`. More than one asset → same as the `duplicate` row in 5a.

## Step 6 — `bugHub.projectCode` in `.claude/project-profile.json`

The profile file has a hand-written format (blank lines between groups, compact arrays) — do **not** `json.dump` the
whole file back. Edit in place, then verify with `json.loads`:

```bash
python3 - .claude/project-profile.json "<code>" <<'PY'
import json, os, re, sys
path, code = sys.argv[1], sys.argv[2]
text = open(path, encoding="utf-8").read() if os.path.exists(path) else "{\n}\n"
data = json.loads(text)
hub = data.get("bugHub")
if isinstance(hub, dict) and hub.get("projectCode") == code:
    print("ok"); sys.exit(0)
quoted = json.dumps(code)
m = re.search(r'"bugHub"\s*:\s*\{[^{}]*\}', text)
if m:
    block = m.group(0)
    if re.search(r'"projectCode"\s*:', block):
        block = re.sub(r'("projectCode"\s*:\s*)"[^"]*"', lambda x: x.group(1) + quoted, block, count=1)
    else:
        block = re.sub(r'\{', '{\n    "projectCode": ' + quoted + ',', block, count=1)
        block = re.sub(r',(\s*)\}$', r'\1}', block)
    new, result = text[:m.start()] + block + text[m.end():], "updated"
else:
    body = text.rstrip()
    if not body.endswith("}"):
        sys.exit("error: profile is not a JSON object")
    inner = body[:-1].rstrip()
    lead = "\n" if inner.endswith("{") else ",\n\n"
    new = inner + lead + '  "bugHub": {\n    "projectCode": ' + quoted + '\n  }\n}\n'
    result = "created" if os.path.exists(path) else "created (new file)"
check = json.loads(new)
if check.get("bugHub", {}).get("projectCode") != code:
    sys.exit("error: edit did not produce bugHub.projectCode")
open(path, "w", encoding="utf-8", newline="").write(new)
print(result)
PY
```

Prints `ok` / `updated` / `created` → record that exact word. `error: …` → no file written (the script exits before
writing), result `dev to do: add "bugHub": {"projectCode": "<code>"} to project-profile.json`.
Verify: `python3 .claude/scripts/project_profile.py bugHub` must print exactly `<code>`.

## Step 7 — Cheat for QA devices (who sees the Bug Logger)

The Bug Logger is in **every** build; the floating button (the package UI, `BugLoggerUI.LauncherVisible`) only shows
when the game turns it on via cheat (`GameSystems.isCheat` — the template sets it in `DebugToolsCheatGate`). Cheat can be
enabled in every build for exactly the devices on the project's test-device list — package `com.ezg.cheat-gate`
(`CheatGate`), project code declared in `GameConstant.CheatGateProject`. Check whether the project already uses
CheatGate with the right BugHub code:

```bash
f=$(grep -rl "CheatGateProject[[:space:]]*=" Assets --include="*.cs" | head -1)
[ -n "$f" ] || echo "NO-CHEATGATE"
[ -n "$f" ] && grep -ho 'CheatGateProject[[:space:]]*=[[:space:]]*"[^"]*"' "$f"
grep -rl "static bool CanEnableCheatFromRemoteConfig" Assets --include="*.cs" | head -1 | sed 's/^/OLD-GATE: /'
```

| Situation | Result |
|---|---|
| `NO-CHEATGATE` (with or without `OLD-GATE`) | `dev to do: the project does not use com.ezg.cheat-gate yet — install it from the Feature Hub and integrate it per the package README (drop the old Remote Config whitelist)` |
| `CheatGateProject = ""` | `dev to do: set GameConstant.CheatGateProject = "<code>" in <f>` |
| `CheatGateProject = "<not code>"` | `dev to do: GameConstant.CheatGateProject is "<value>", BugHub is "<code>" — make them match` |
| `CheatGateProject = "<code>"`, no `OLD-GATE` | `ok` |
| `CheatGateProject = "<code>"`, `OLD-GATE` still present | `dev to do: the old gate CanEnableCheatFromRemoteConfig is still in <file> — remove it so only CheatGate remains` |

The skill does **not** edit the gate itself (it touches the store build's cheat, the dev must decide). Always print the guide for adding QA devices:

1. On the QA device, open the game (a build with the integration) → Settings → tap the version 10 times → the game shows
   `Device ID: XXXX-XXXX-XXXX` (already copied).
2. The project's Lead / QA in BugHub adds the device via MCP `ezg`: `test_device_add` with project `<code>`, that device_id
   and a label (who holds which device). Takes effect after ~1-2 minutes; QA taps 10 times again to open cheat.
3. Remove a device: `test_device_remove`; list devices: `test_device_list`. Remote Config
   `enable_cheat_default` / `enable_cheat_default_device` is no longer used.

## Step 8 — `/auto-clear` for `/fix-bug --watch`

`/fix-bug --watch` needs the `/auto-clear` Stop hook (once per machine; macOS uses iTerm2):

```bash
bash .claude/skills/auto-clear/scripts/auto-clear.sh status
# Windows: powershell -ExecutionPolicy Bypass -File .claude/skills/auto-clear/scripts/auto-clear.ps1 status
```

| Printed line | Result |
|---|---|
| `HOOK installed …` | `ok` |
| `HOOK not-installed` / `HOOK broken …` | `dev to do: /auto-clear install (once on this machine)` |
| `UNSUPPORTED …` (not running in iTerm2 / Windows Terminal) | `dev to do: run /fix-bug --watch in iTerm2 (macOS), then /auto-clear install` |
| script missing | `dev to do: install the auto-clear skill from the Feature Hub (AI Feature tab)` |

Don't run `install` yourself — it edits the dev's `~/.claude/settings.json`.

Watch window branch: `python3 .claude/scripts/bughub-watch.py config` → `mode: "follow"` (empty `branch`, the
default) means watch fixes + pushes to whatever branch it runs on. Not an error, not a row in the table — just add
to the dev to-do list: "open a separate pane on the branch that should receive fixes, then type `/fix-bug --watch`; to
have the bot run on its own branch, declare `bugHub.watch` (branch / baseBranch / mergeToBase / worktree) in
`.claude/project-profile.json`". Don't pick a branch or worktree on the dev's behalf.

## Step 9 — Report

One table with all 8 rows, then the dev to-do list (if any):

```
| Step | Result |
|---|---|
| 1. Project code | ok — SM007 |
| 2. MCP bughub | ok — <email> (admin|dev) |
| 3. Project on server | created | ok | updated | dev to do: … |
| 4. Package com.ezg.buglogger | ok (embedded|manifest) | … |
| 5. BugLoggerSettings | ok | created | updated — <path> | … |
| 6. project-profile.json | ok | created | updated |
| 7. QA device cheat | ok | dev to do: … |
| 8. auto-clear | ok | dev to do: … |
```

- Step 3 just called upsert → paste the server's full table below the table above.
- Step 7 → always include the 3-line guide for adding QA devices to the cheat allowlist.
- Changed files (asset + `.meta` when newly created, `project-profile.json`) listed as absolute `file://` links
  per `.claude/rules/output-format.md`. The skill does **not** commit — the dev commits (e.g. `/push-in-session`).
- Every step `ok` → one final line: "BugHub is ready for `<code>` — QA (devices in the cheat allowlist) submit bugs from any build, devs
  fix them with `/fix-bug`. For a bug thread QA opened themselves in the forum: right-click the message → Apps → **Đưa vào BugHub**."
