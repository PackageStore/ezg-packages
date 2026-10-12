# run-backlog — STEP 1b — Requires gate

Conditional part of [`SKILL.md`](SKILL.md) (see its *Conditional sections* table). Applies when the task body declares `**Requires:**`. Every rule here is as binding as SKILL.md itself.

### 1b. Requires gate (only when the task declares `**Requires:**`)

Currently one requirement token is defined: `unity-editor` (the task authors prefabs / needs a live Editor, e.g. `/new-ui`- and `/new-package`-backed tasks).

#### 0. Worktree mode short-circuit — do NOT probe

If `$AGENT_MODE = worktree`, the requirement can never be met: the worktree is a
separate Unity project folder and the only live Editor is attached to the dev's
checkout. Probing would find that Editor and tempt you into refreshing/playing the
**wrong project**. Skip the probe entirely and go straight to step 2 below (defer,
or `EDITOR_REQUIRED` if every remaining task needs the Editor).

#### 1. Probe the Editor — retry-with-wait, NEVER single-shot (current mode only)

**Why:** "The Editor is open" ≠ "the MCP bridge answers *this instant*". Discovery is a port scan that needs the Editor's main thread to respond; right after a heavy previous task (asset import, a large AssetBundle rebuild, or a domain/assembly reload) the main thread is blocked and a one-shot `unity_list_instances` returns **zero instances even though the Editor is up**. A single-shot gate turns that transient into a false hard `EDITOR_REQUIRED` pause. So classify the result, and retry only the genuinely-ambiguous case:

Probe `mcp__unity__unity_list_instances` and classify:

- **This project's Editor is listed** → requirement met. If several instances are listed, `mcp__unity__unity_select_instance` the one whose project path matches this repo (**never** stop to ask a human — the loop is autonomous). If that instance is mid-compile, poll `mcp__unity__unity_editor_state` until it is NOT compiling, then continue to STEP 2 normally.
- **MCP reachable but only OTHER projects are listed** (a different game's Editor) → a genuine "not live for this project". Do NOT retry — go to step 2.
- **MCP unreachable / call errored or timed out / zero instances returned** → treat as *maybe-busy, not maybe-absent*. Wait ~5 s (`sleep 5`) and re-probe. Repeat up to **4 attempts (~20 s total)**. If any attempt lists this project → requirement met (first bullet). Only after the full retry budget is exhausted without the project Editor ever appearing → treat as NOT met and go to step 2.

#### 2. Requirement NOT met (retry budget exhausted, or MCP reachable with only other projects):

   - Check whether any OTHER task remains that could run headless: `grep -L '\*\*Requires:\*\*' "$BACKLOG_ROOT"/todo/*.md` (cheap, deterministic — reads no task body beyond the marker).
   - **Some headless task exists** → defer this one and let the loop continue:
     ```bash
     python3 .claude/scripts/backlog-ops.py defer <NNN>
     ```
     Then output: `DEFERRED — task <NNN> requires unity-editor (not live); moved to the tail of TODO. Re-run picks the next task.` and STOP this iteration (the loop runner treats it as a normal iteration end and starts the next one).
   - **Every remaining TODO task requires the editor** → pause the loop exactly like the empty-backlog flow: write `EDITOR_REQUIRED` into `$BACKLOG_ROOT/state` (no commit — it is inside `.git/`), and output: `EDITOR_REQUIRED — all remaining tasks need a live Unity Editor. Open the Editor (and re-run in current mode — worktree mode can never satisfy this), delete $BACKLOG_ROOT/state, then re-run.` (Without the state write, a headless loop would defer-cycle forever.)
