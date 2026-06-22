#!/usr/bin/env bash
set -uo pipefail

main() {
  # ============================================================
  #  One-click launcher: secondary Claude Code harness routed to
  #  DeepSeek via claude-code-router (ccr).
  #  The main VSCode extension stays on Anthropic, untouched.
  # ============================================================

  resolve_dir() {
    local dir="$1"
    if [[ -d "$dir" ]]; then
      (cd "$dir" && pwd -P)
    else
      printf '%s\n' "$dir"
    fi
  }

  local script_dir
  script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)" || return 1

  # --- Project root the harness operates on ---
  # Priority:
  #   1. First argument:
  #        ./start-deepseek.command /path/to/repo
  #   2. DEEPSEEK_REPO environment variable:
  #        export DEEPSEEK_REPO=/path/to/repo
  #   3. Repo that contains this file:
  #        <repo>/.claude/harness/start-deepseek.command
  local repo
  if [[ $# -gt 0 && -n "${1:-}" ]]; then
    repo="$(resolve_dir "$1")"
  elif [[ -n "${DEEPSEEK_REPO:-}" ]]; then
    repo="$(resolve_dir "$DEEPSEEK_REPO")"
  else
    repo="$(resolve_dir "$script_dir/../..")"
  fi

  if [[ ! -d "$repo" ]]; then
    echo "Project folder does not exist: $repo"
    echo
    echo "Usage: ./start-deepseek.command /path/to/repo"
    return 1
  fi

  # --- Isolation: set USE_WORKTREE=1 to run in a separate git
  #     worktree so this harness never fights the main one over
  #     the same files. Leave 0 to run in the main repo. ---
  local use_worktree worktree branch
  use_worktree="${USE_WORKTREE:-0}"
  worktree="${DEEPSEEK_WORKTREE:-$repo-ds}"
  branch="${DEEPSEEK_BRANCH:-agent/deepseek}"

  # --- Local ccr endpoint exposed while the router is running. ---
  local ccr_host ccr_port ccr_base_url ccr_messages_endpoint ccr_config
  ccr_host="${CCR_HOST:-127.0.0.1}"
  ccr_port="${CCR_PORT:-3456}"
  ccr_base_url="http://$ccr_host:$ccr_port"
  ccr_messages_endpoint="$ccr_base_url/v1/messages"
  ccr_config="$HOME/.claude-code-router/config.json"
  local claude_model="opus"

  local target
  if [[ "$use_worktree" == "1" ]]; then
    if [[ ! -d "$worktree" ]]; then
      echo "Creating git worktree $worktree [$branch] ..."
      (
        cd "$repo" &&
          (git worktree add "$worktree" -b "$branch" 2>/dev/null || git worktree add "$worktree" "$branch")
      ) || return 1
    fi
    target="$worktree"
  else
    target="$repo"
  fi

  cd "$target" || return 1

  echo
  echo "  Harness : DeepSeek via ccr"
  echo "  Folder  : $(pwd -P)"
  echo "  Base URL: $ccr_base_url"
  echo "  Messages: $ccr_messages_endpoint"
  echo "  Config  : $ccr_config"
  echo "  Model   : $claude_model"
  echo

  # Restart router so any config.json edits are applied on launch.
  ccr restart >/dev/null 2>&1 || true

  # --dangerously-skip-permissions: this harness auto-runs every
  # tool (bash, edits, MCP) with NO confirmation. Scoped to this
  # launcher only - the main VSCode harness keeps its prompts.
  # MCP servers (unity, codegraph) auto-load from .mcp.json via
  # enableAllProjectMcpServers in .claude/settings.local.json.
  ccr code --model "$claude_model" --dangerously-skip-permissions
}

main "$@"
STATUS=$?

echo
echo "(press Enter to close this window)"
read -r _

exit "$STATUS"
