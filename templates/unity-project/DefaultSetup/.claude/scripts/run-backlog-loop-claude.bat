@echo off
REM Wrapper to run the Claude backlog loop with execution policy bypassed.

setlocal
set "SCRIPT_DIR=%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%run-backlog-loop-claude.ps1" %*
endlocal
pause
