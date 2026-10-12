# run-backlog — STEP 2a–2b — base branch + worktree sync

Conditional part of [`SKILL.md`](SKILL.md) (see its *Conditional sections* table). Applies when `AGENT_MODE=worktree` (STEP 2.0 in SKILL.md skips all of this in current mode). Every rule here is as binding as SKILL.md itself.

### 2a. Resolve the base branch (worktree mode)

```bash
[ "$HAS_REMOTE" = "1" ] && git fetch origin      # local-only repo: nothing to fetch
```

Resolve in this order:

```bash
BASE_BRANCH="${AGENT_BASE_BRANCH:-}"  # captured by loop runner
if [ -z "$BASE_BRANCH" ]; then
  BASE_BRANCH=$(git rev-parse --abbrev-ref HEAD)     # ad-hoc single /run-backlog invocation
fi
# Starting from an agent branch is ALLOWED (a previous loop leaves HEAD there). Never
# stop for it — resolve the real base from the recorded config, then the repo default.
case "$BASE_BRANCH" in
  ""|HEAD|agent/dev|agent/dev-*)
    BASE_BRANCH=$(git config "$(python3 .claude/scripts/project_profile.py gitConfigPrefix).agentBaseBranch" 2>/dev/null || true) ;;
esac
case "$BASE_BRANCH" in
  ""|agent/dev|agent/dev-*) BASE_BRANCH=$(python3 .claude/scripts/project_profile.py defaultBaseBranch) ;;   # profile default, last resort
esac
```

- **No stop here.** An agent branch / detached `HEAD` is a normal starting point, not an error — the fallback chain above always yields a usable base. Log which branch was resolved and continue.

**Work branch name.** Use `$WORK_BRANCH` from the loop runner. If it is unset, derive it
exactly the way the runner does — `agent/dev-` plus the base with every `/` replaced by `-`:

```bash
WORK_BRANCH="${AGENT_BRANCH:-agent/dev-$(printf '%s' "$BASE_BRANCH" | tr '/' '-')}"
```

The slashes MUST be flattened. Git stores refs as files, so a branch named `Dev1` and a
branch named `Dev1/agent/dev` cannot coexist — creating the second fails with
`cannot lock ref ...: 'refs/heads/Dev1' exists`. Never build the name by appending a
path segment to the base branch.

**Merge source = "latest":** prefer the remote tip `origin/$BASE_BRANCH` whenever it exists (freshest); fall back to the local `$BASE_BRANCH` when the base has no remote tracking branch — which is always the case when `HAS_REMOTE=0`.

### 2b. Sync the worktree branch

The loop runner already created (or reused) the worktree and put it on `$WORK_BRANCH`,
and your cwd is that worktree. You only need to bring the base in:

```bash
if [ "$HAS_REMOTE" = "1" ]; then
  git pull origin "$WORK_BRANCH"                   # skip if the remote branch doesn't exist yet
fi

if [ "$HAS_REMOTE" = "1" ] && git show-ref --verify --quiet "refs/remotes/origin/$BASE_BRANCH"; then
  git merge --no-edit "origin/$BASE_BRANCH"        # freshest tip
else
  git merge --no-edit "$BASE_BRANCH"               # local-only repo, or base never pushed
fi

git config "$(python3 .claude/scripts/project_profile.py gitConfigPrefix).agentBaseBranch" "$BASE_BRANCH"   # reporting convenience only, not next-run input
```

- If the merge reports **conflicts** → STOP with:
  `BASE_MERGE_CONFLICT — merging <BASE_BRANCH> into <WORK_BRANCH> conflicts. Resolve manually, commit, then re-run.`
  DO NOT auto-resolve.
- **Never `git checkout` another branch here.** Git refuses to check out a branch that is
  already checked out in another worktree, and switching would desync the runner.

> Branch model: each loop run captures whichever non-agent branch is checked out when the runner starts. The repo-local `<gitConfigPrefix>.agentBaseBranch` config is updated only as a reporting convenience; it does not select the next loop's base. The user manually merges `$WORK_BRANCH -> <base branch>` after running the manual verification steps.
