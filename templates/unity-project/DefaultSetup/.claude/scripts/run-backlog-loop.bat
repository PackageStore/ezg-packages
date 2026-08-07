@echo off
setlocal
set "SCRIPT_DIR=%~dp0"

echo.
echo  ==========================================
echo   BlazeSurvivor - Run Backlog Loop
echo  ==========================================
echo.
echo  [1] Claude
echo  [2] Codex
echo  [3] Gemini
echo.
set /p choice=" Select provider (1-3, default=1): "

if "%choice%"=="" goto claude
if "%choice%"=="1" goto claude
if "%choice%"=="2" goto codex
if "%choice%"=="3" goto gemini

echo  Invalid choice.
pause
exit /b 1

:claude
set "PROVIDER=claude"
goto run

:codex
set "PROVIDER=codex"
goto run

:gemini
set "PROVIDER=gemini"
goto run

:run
echo.
echo  Where should the agent work?
echo.
echo  [1] This checkout        - keeps the compile check + runtime smoke gates.
echo                             Do NOT edit files while the loop runs.
echo  [2] Separate worktree    - you keep working undisturbed, but there is NO
echo                             compile check and NO runtime smoke (the worktree
echo                             has no .sln and no Unity Editor). Merge and run
echo                             /compile-check yourself afterwards.
echo.
set /p modechoice=" Select mode (1-2, default=1): "

if "%modechoice%"=="2" (set "MODE=Worktree") else (set "MODE=Current")

echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%run-backlog-loop-core.ps1" -Provider %PROVIDER% -Mode %MODE% %*
exit /b %errorlevel%
