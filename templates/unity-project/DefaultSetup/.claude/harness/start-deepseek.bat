@echo off
setlocal
title DeepSeek Harness (ccr)

REM ============================================================
REM  One-click launcher: secondary Claude Code harness routed to
REM  DeepSeek via claude-code-router (ccr).
REM  The main VSCode extension stays on Anthropic, untouched.
REM ============================================================

REM --- Project root the harness operates on ---
REM Priority:
REM   1. First argument:
REM        start-deepseek.bat C:\path\to\repo
REM   2. DEEPSEEK_REPO environment variable:
REM        set DEEPSEEK_REPO=C:\path\to\repo
REM   3. Repo that contains this file:
REM   <repo>\.claude\harness\start-deepseek.bat
if not "%~1"=="" (
  for %%I in ("%~1") do set "DEEPSEEK_REPO=%%~fI"
)
if not defined DEEPSEEK_REPO (
  for %%I in ("%~dp0..\..") do set "DEEPSEEK_REPO=%%~fI"
)
set "REPO=%DEEPSEEK_REPO%"

if not exist "%REPO%\" (
  echo Project folder does not exist: %REPO%
  echo.
  echo Usage: start-deepseek.bat C:\path\to\repo
  pause
  exit /b 1
)

REM --- Isolation: set USE_WORKTREE=1 to run in a separate git
REM     worktree so this harness never fights the main one over
REM     the same files. Leave 0 to run in the main repo. ---
set "USE_WORKTREE=0"
if not defined DEEPSEEK_WORKTREE set "DEEPSEEK_WORKTREE=%REPO%-ds"
set "WORKTREE=%DEEPSEEK_WORKTREE%"
if not defined DEEPSEEK_BRANCH set "DEEPSEEK_BRANCH=agent/deepseek"
set "BRANCH=%DEEPSEEK_BRANCH%"

REM --- Local ccr endpoint exposed while the router is running. ---
set "CCR_HOST=127.0.0.1"
set "CCR_PORT=3456"
set "CCR_BASE_URL=http://%CCR_HOST%:%CCR_PORT%"
set "CCR_MESSAGES_ENDPOINT=%CCR_BASE_URL%/v1/messages"
set "CCR_CONFIG=%USERPROFILE%\.claude-code-router\config.json"

if "%USE_WORKTREE%"=="1" (
  if not exist "%WORKTREE%" (
    echo Creating git worktree %WORKTREE% [%BRANCH%] ...
    pushd "%REPO%"
    git worktree add "%WORKTREE%" -b %BRANCH% 2>nul || git worktree add "%WORKTREE%" %BRANCH%
    popd
  )
  set "TARGET=%WORKTREE%"
) else (
  set "TARGET=%REPO%"
)

cd /d "%TARGET%"

echo.
echo   Harness : DeepSeek via ccr
echo   Folder  : %CD%
echo   Base URL: %CCR_BASE_URL%
echo   Messages: %CCR_MESSAGES_ENDPOINT%
echo   Config  : %CCR_CONFIG%
echo.

REM Restart router so any config.json edits are applied on launch.
call ccr restart >nul 2>&1

REM --dangerously-skip-permissions: this harness auto-runs every
REM tool (bash, edits, MCP) with NO confirmation. Scoped to this
REM launcher only - the main VSCode harness keeps its prompts.
REM MCP servers (unity, codegraph) auto-load from .mcp.json via
REM enableAllProjectMcpServers in .claude\settings.local.json.
call ccr code --dangerously-skip-permissions

echo.
echo (harness exited) - press any key to close...
pause >nul
