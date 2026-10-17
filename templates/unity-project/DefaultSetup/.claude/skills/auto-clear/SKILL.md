---
name: auto-clear
description: Push the session's work via /push-in-session, then automatically /clear the very Claude Code session that is running right after the turn ends — only after the push is the flag set for the exact iTerm2 pane (macOS) / Windows Terminal console (Windows, experimental); a Stop hook reads the flag and types "/clear" + Enter into that exact pane. Use when the user says "/auto-clear", "xong việc thì tự clear", "clear sau khi xong", "auto clear when done", "clear after finishing", or the prompt/goal has a "/clear" step after completion. "/auto-clear # UI:" to set the commit prefix + tag, "/auto-clear off" to cancel, "/auto-clear install" to register the hook on a new machine.
---

# Auto Clear

**Push first, then clear:** run `/push-in-session` to commit + push exactly the files this session changed, and
only after the push automatically `/clear` the **running** Claude Code session right after the turn ends — the dev
doesn't type it, doesn't open a new session, and loses no unpushed work. Claude Code has no built-in way to clear
(skills/hooks cannot invoke built-in commands), so this skill only **sets a flag**; a Stop hook (registered once per
machine) reads the flag and then **types `/clear` + Enter into the exact pane** running Claude.

| | macOS (iTerm2) | Windows (Windows Terminal / console) — **EXPERIMENTAL** |
|---|---|---|
| Script | `bash .claude/skills/auto-clear/scripts/auto-clear.sh <sub>` | `powershell -ExecutionPolicy Bypass -File .claude/skills/auto-clear/scripts/auto-clear.ps1 <sub>` |
| Targets the pane by | UUID in `ITERM_SESSION_ID` + tty match with the `claude` process | PID of the `claude` process → `AttachConsole` + `WriteConsoleInput` |
| Flag | `~/.claude/auto-clear/<uuid>.flag` (+ `<uuid>.retry`) | `~/.claude/auto-clear/pid-<pid>.flag` (+ `pid-<pid>.retry`) |

Both target by pane/process, **not** by the focused window — a dev standing in another pane won't get keystrokes
by mistake. Below, `<script>` is the "Script" row for the right OS in the table above.

## 0. Read the argument

| Arg | What to do |
|---|---|
| (empty) | Push, then set the flag — per sections 1–4 |
| prefix and/or `Tag:` (e.g. `# UI:`, `* Play:`, `Bal:`) | Same as (empty); the prefix (`+`/`*`/`#`) + tag are passed verbatim to `/push-in-session` (its section 3.6). Tag not in push-in-session's tag table → stop and ask, don't invent one |
| `--then "<prompt>"` (combinable with prefix/tag, e.g. `/auto-clear # Play: --then "/fix-bug --watch"`) | Same as (empty), but set the flag with `--then` (section 3): after `/clear` the hook waits, then types `<prompt>` + Enter into the **same pane**. The prompt must be one line, no control characters, ≤ 200 characters — otherwise the script returns `INVALID_THEN …` and the flag is **not** set → tell the dev, don't fix the prompt yourself |
| `off` | `<script> off` — removes the flag (including a pending `--then` prompt) **and** the API-error retry flag (`retry-arm`), reports back. **No** push |
| `status` | `<script> status` — also prints a `THEN <prompt>` line when the flag has a pending prompt, plus `FAILHOOK` + `RETRY_ARMED count=k/<max>` (`∞` = unlimited) |
| `probe` | `<script> probe` — detects the pane/console, types **nothing** (installation check) |
| `type-test` | `<script> type-test` — really types `auto-clear type-test ok` + Enter into the pane/console of the claude running this command. Run it from a Claude Code Bash command: the text must show up as a queued user message on the next turn — proof that keystroke injection works on this machine. **No** push |
| `retry-arm [--prompt "<prompt>"] [--max N]` | **API-error retry** (section 5) for the current pane — called by long-running unattended skills (e.g. `/fix-bug`). `--max 0` = retry without limit. **No** push. macOS + Windows |
| `retry-off` | Removes the current pane's API-error retry flag. **No** push |
| `install` | `<script> install` — registers the Stop + StopFailure hooks in `~/.claude/settings.json`. It edits user-level settings, so **ask the dev first** unless they explicitly asked for it |
| `uninstall` | `<script> uninstall` — removes both hooks |

Only the (empty) / prefix-tag branches push; the other sub-commands only run the script.

## 1. Order: other work → push → set the flag

- `/auto-clear` comes with other work in the prompt/goal → finish all the other work first (code, compile-check
  per `.claude/rules/compile-validation.md`, report). Then `/push-in-session` (section 2). Setting the flag
  (section 3) is the **last tool call** of the turn; after it only write the final answer and stop — no more
  tool calls.
- Under `/goal`: only push + set the flag when the goal condition is **fully met**. Set it too early while the goal
  blocks stopping, and `/clear` gets typed while work is still going on.
- Task unfinished / blocked / compile or test failure (`COMPILE_BLOCKED`…) → **don't** push, **don't** set the
  flag; report to the dev as usual (the dev needs to read the error before the context is lost). Exception: `/fix-bug --watch` with a
  `released` bug — that skill has already stashed the half-done fix and written the error into the report, so still push (yields
  `NO_CHANGES`) + set the flag with `--then "/fix-bug --watch"` so the loop keeps running (fix-bug section 5).
- The prompt already asked for a separate `/push-in-session` and this turn has already pushed → don't push a second time; re-running
  only yields `NO_CHANGES` if no file has been changed since.
- Inside `/run-backlog` → do **not** use this skill: the loop commits by itself (STEP 9) and opens a new session
  for each task by itself.

## 2. `/push-in-session` BEFORE setting the flag (mandatory)

Read and follow [`push-in-session`](../push-in-session/SKILL.md) exactly — file list built from the transcript,
`git_prepare_scoped` → message `<prefix> Tag: <subject>` → `git_push`. Don't `git add` / commit /
push any other way, don't add `git status` / `git diff` beyond that skill's command budget. Unity files that
dirty themselves (scenes/SOs re-synced by tools after `Assets/Refresh`) and don't belong to the session stay as they are — push-in-session
already lists them under `--- DIRTY OUTSIDE SESSION ---`.

| push-in-session result | Set the flag? |
|---|---|
| Push succeeded (including after `git push -u origin HEAD` when the branch has no upstream yet) | **Yes** |
| `NO_CHANGES` (session only read/analyzed, or everything was pushed earlier) | **Yes** — the report says "nothing to push" |
| No `origin` — committed locally, push skipped | **Yes** — the report states clearly that the commit is not pushed |
| Push error (branch behind, auth, network, hook reject) / commit error | **No** — report the error verbatim, stop |
| Had to stop and ask the dev (tag not in the table, unsure which files belong to the session…) | **No** — ask the dev, wait for the answer before continuing |

## 3. Set the flag with the report

Report = exactly the content of the final answer: the file list (per `.claude/rules/output-format.md`), push-in-session's
report (commit message, committed files, dirty files outside the session, push status), dev to-dos.
Pass it via stdin so it is saved — after `/clear` the on-screen report may be gone.

macOS:
```bash
bash .claude/skills/auto-clear/scripts/auto-clear.sh arm --stdin <<'EOF'
<report>
EOF
```

Windows:
```bash
powershell -ExecutionPolicy Bypass -File .claude/skills/auto-clear/scripts/auto-clear.ps1 arm --stdin <<'EOF'
<report>
EOF
```

`--then "<prompt>"` in the argument → add it to the arm command alongside `--stdin` (any order), wrapping the prompt in
**single quotes** so the shell doesn't expand `$`/backticks (`'` inside the prompt → `'\''`):
`<script> arm --stdin --then '<prompt>' <<'EOF' …`. The script validates the prompt **before** saving the report or setting the flag.

## 4. Read the output

| Output | What to do |
|---|---|
| `ARMED ...` | Final answer = report + exactly 1 line: "The session will /clear itself after I stop — report saved at `~/.claude/auto-clear/last-report.md`." (with `then=<prompt>` → add "and then run `<prompt>` automatically"). Do **not** say "cleared" — the clear happens after the turn ends. |
| `INVALID_THEN ...` | The `--then` prompt is invalid, the flag was **not** set (push already done) → give the dev the verbatim reason, remind them to type `/clear` themselves. |
| `NOT_INSTALLED ...` | The hook is not registered on this machine (or points at a script that was deleted/moved) → ask the dev whether to run `install`. Yes → `install` then `arm` again (**don't** push again); no → remind the dev to type `/clear` themselves. |
| `UNSUPPORTED ...` | Not running in iTerm2 (macOS) / no `claude` process found → remind the dev to type `/clear` themselves (push already done). |

## 5. Retry when the turn dies from an API error (macOS + Windows)

Claude API errors (`Your organization has disabled Claude subscription access`, rate limit, overloaded,
5xx, session lost…) kill the turn — the model is no longer running, so **the skill cannot retry by itself**. Claude Code
fires the `StopFailure` hook (with `error`: `oauth_org_not_allowed`, `authentication_failed`, `rate_limit`,
`overloaded`, `server_error`…) instead of `Stop`, and `install` registers that hook.

- `<script> retry-arm [--prompt "<prompt>"] [--max N]` sets the flag `~/.claude/auto-clear/<uuid>.retry` (Windows:
  `pid-<pid>.retry`; pid + tty + prompt + `max`, `count=0`). Default prompt:
  "Tiếp tục: lượt trước dừng vì lỗi API — làm tiếp đúng bước đang dở."
  (Windows: the ASCII English version "Continue: the previous turn stopped on an API error - resume the exact step in progress.")
  — same prompt rules as `--then`. Calling it again re-arms from `count=0`. Keep a passed-in prompt
  ASCII: the Windows version types it character by character into the console, and characters outside the keyboard are the most fragile part.
- On each `StopFailure`: the right pane + the right armed `claude` process (Windows: flag keyed by the `claude` process pid) → `count+1`, wait `AUTO_CLEAR_RETRY_DELAY`
  (default **30 seconds**), then type the prompt + Enter. More than `max` consecutive times → stop typing, log `failhook give-up` — the dev
  handles it by hand (e.g. sign in again / wait for the org to re-enable access). `max` = `--max N` at arm time, otherwise
  `AUTO_CLEAR_RETRY_MAX` (default **5**); `--max 0` = **unlimited** — for loops that must never
  stop (e.g. `/fix-bug`): retype every 30 seconds until the API answers or the dev runs `retry-off` / `off`.
- A turn that ends normally (`Stop`) resets `count` to 0. The flag survives `/clear`; remove it with `retry-off` or `off`.
- `retry-arm` results: `RETRY_ARMED …` · `NOT_INSTALLED` (the machine has an old install with only the Stop hook → `install` again, ask
  the dev unless they explicitly asked) · `UNSUPPORTED` (macOS outside iTerm2 / no `claude` process found) — none of them block the work of the skill
  that calls it.
- Windows flags are keyed by PID, and Windows reuses PIDs: `arm` / `retry-arm` delete flags of `claude` processes that have exited (log
  `stale-flag removed`).

## Installing on a new machine

1. `<script> install` — idempotent, backs up to `~/.claude/settings.json.bak-auto-clear`, keeps every other hook
   intact. The hook lives in **user-level** settings: the project's `.claude/settings.json` is committed for the whole team,
   while a hook that types into a pane is each machine's own business. One hook serves **every project** on the machine — install it
   from any project, no need to reinstall per project.
2. `<script> probe` — macOS must print `ok-dry tty=/dev/ttysN`; Windows must print `ok-dry attached pid=N`.
   The first time on macOS the OS asks for permission to control iTerm2 → Allow.
   Then `<script> type-test` from a Bash command **inside Claude Code**: it prints `-> ok` and the next turn receives the message
   `auto-clear type-test ok` = keystroke injection really works (Windows: this is the only step that can verify `WriteConsoleInput` on that
   machine — `/clear`, `--then` and API-error retry all use this same typing path).
3. A new hook usually takes effect immediately; if the first turn doesn't clear → open `/hooks` once or restart
   Claude Code.

`install` copies the script to `~/.claude/auto-clear/auto-clear.sh` (Windows: `.ps1`) and makes the hook run that
copy, **not** something inside the repo — deleting/moving/copying the project elsewhere doesn't kill the hook. Every `arm`
re-syncs the copy, so updating the skill via the Feature Hub makes the hook use the new version right away. Machines with
an old install (hook pointing straight into a repo, e.g. another project's `.agents/scripts/auto-clear.sh`) still work;
run `install` once to switch to the copy.

**Several projects on one machine share one copy.** The script carries a `# auto-clear-version: N` line; `arm` /
`retry-arm` / `install` do **not** overwrite a copy with a higher number (`install` prints `KEPT_NEWER: …` and still registers the hooks
on the newer copy; log `keep hook copy vN`). A script **without** that line (from before this mechanism) still overwrites
unconditionally → every project on the machine must update this skill, otherwise an old project downgrades the whole machine's hook on every
`arm` (Windows: the old version has a `failhook` that does nothing → API-error retry silently dies for every project). Changing the hook side →
bump N in **both** `.sh` and `.ps1`.

Log: `~/.claude/auto-clear/auto-clear.log` — `hook ... -> ok` / `tty-mismatch` / `pane-not-found` /
`attach-failed`; with `--then` there is an extra line `hook-then ... prompt=<prompt> -> ok` (or `skip: …` when
`/clear` could not be typed — the prompt is never typed on its own). API-error retry: `failhook … error=<code> try k/<max>` (`∞` when unlimited; Windows:
`try k/<max|unlimited>`), `failhook give-up`, `retry-reset`; `type-test … -> ok`; Windows adds `stale-flag removed`.

## Limitations

- macOS: iTerm2 only. Claude running in tmux/ssh inside iTerm → tty mismatch → the hook skips (no clear,
  no mistyping). Terminal.app / VS Code / desktop app → `UNSUPPORTED`.
- Windows: the flag / hook / settings logic is tested with PowerShell (checked for Windows PowerShell 5.1 compatibility); only
  the typing part (`AttachConsole` + `WriteConsoleInput`) can be verified only on a real Windows machine → after `install` run
  `probe` + `type-test` once. Claude running in WSL is not supported. `auto-clear.ps1` must stay **pure ASCII**
  (PowerShell 5.1 reads BOM-less files as ANSI).
- The dev typing exactly while the hook waits (~1 second) → the text may get mixed with `/clear`. Change the delay with env
  `AUTO_CLEAR_DELAY`.
- `--then`: one-line prompt, no control characters, ≤ 200 characters; only typed into the exact armed pane/process
  (tty match / re-attach by pid, as for `/clear`). Wait between `/clear` and the prompt = env `AUTO_CLEAR_THEN_DELAY`
  (default 3 seconds); `AUTO_CLEAR_DRY` applies to both typings. The hook has a 30-second `timeout` — don't set
  `AUTO_CLEAR_DELAY` + `AUTO_CLEAR_THEN_DELAY` above ~20 seconds, or the hook gets killed midway and the prompt is not typed.
