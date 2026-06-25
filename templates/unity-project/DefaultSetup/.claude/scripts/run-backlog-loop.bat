@echo off
REM [Project Name] - Run Backlog Loop entrypoint.
REM Interactive: pick a provider, then a model. Forwards any extra args to the wrapper.
REM Autonomous (redirected/empty stdin, e.g. spawned by /execute-backlog-tasks): every
REM prompt falls back to its pre-set default (Claude + wrapper default model), so it
REM never hangs and never errors on empty input.
REM
REM Usage:
REM   run-backlog-loop.bat                 (interactive menu)
REM   run-backlog-loop.bat -Model claude-opus-4-8 -MaxIterations 5   (extra args forwarded)

setlocal enabledelayedexpansion
set "SCRIPT_DIR=%~dp0"
set "EXTRA_ARGS=%*"

echo.
echo  ==========================================
echo   [Project Name] - Run Backlog Loop
echo  ==========================================
echo.
echo  [1] Claude   (default)
echo  [2] Codex
echo  [3] Gemini
echo.

REM set /p leaves the variable at its pre-set default when stdin is empty/redirected,
REM so an autonomous launch proceeds with Claude without hanging or erroring.
set "PROVIDER_CHOICE=1"
set /p "PROVIDER_CHOICE= Select provider (1-3) [default 1]: "

if "%PROVIDER_CHOICE%"=="1" goto claude
if "%PROVIDER_CHOICE%"=="2" goto codex
if "%PROVIDER_CHOICE%"=="3" goto gemini

echo  Invalid choice "%PROVIDER_CHOICE%" - defaulting to Claude.
goto claude

:claude
set "WRAPPER=run-backlog-loop-claude.ps1"
echo.
echo  Model:
echo   [1] claude-sonnet-4-6   (wrapper default)
echo   [2] claude-opus-4-8
echo   [3] claude-haiku-4-5-20251001
echo   [4] custom / pass-through (use -Model in args, or wrapper default)
echo.
set "MODEL_CHOICE=4"
set /p "MODEL_CHOICE= Select model (1-4) [default 4]: "
set "MODEL_ARG="
if "%MODEL_CHOICE%"=="1" set "MODEL_ARG=-Model claude-sonnet-4-6"
if "%MODEL_CHOICE%"=="2" set "MODEL_ARG=-Model claude-opus-4-8"
if "%MODEL_CHOICE%"=="3" set "MODEL_ARG=-Model claude-haiku-4-5-20251001"
goto run

:codex
set "WRAPPER=run-backlog-loop-codex.ps1"
echo.
echo  Model:
echo   [1] wrapper / CLI default   (default)
echo   [2] custom  (type a model id)
echo.
set "MODEL_CHOICE=1"
set /p "MODEL_CHOICE= Select model (1-2) [default 1]: "
set "MODEL_ARG="
if "%MODEL_CHOICE%"=="2" (
    set "CODEX_MODEL="
    set /p "CODEX_MODEL= Enter codex model id: "
    if not "!CODEX_MODEL!"=="" set "MODEL_ARG=-Model !CODEX_MODEL!"
)
goto run

:gemini
set "WRAPPER=run-backlog-loop-gemini.ps1"
echo.
echo  Model:
echo   [1] gemini-3.1-pro-preview   (wrapper default)
echo   [2] custom  (type a model id)
echo.
set "MODEL_CHOICE=1"
set /p "MODEL_CHOICE= Select model (1-2) [default 1]: "
set "MODEL_ARG="
if "%MODEL_CHOICE%"=="2" (
    set "GEMINI_MODEL="
    set /p "GEMINI_MODEL= Enter gemini model id: "
    if not "!GEMINI_MODEL!"=="" set "MODEL_ARG=-Model !GEMINI_MODEL!"
)
goto run

:run
echo.
echo  Launching %WRAPPER% %MODEL_ARG% %EXTRA_ARGS%
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%%WRAPPER%" %MODEL_ARG% %EXTRA_ARGS%
exit /b %errorlevel%
