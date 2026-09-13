@echo off
chcp 65001 >nul
cd /d "%~dp0"

rem ---- pinned engine version (edit dsh-engine.txt or run update-engine.ps1) ----
rem An empty file = follow npm latest (auto-upgrade = unstable, not recommended).
set "SPEC=@deepseek-ai/dsh"
set "VER="
if exist "%~dp0dsh-engine.txt" for /f "usebackq tokens=* delims=" %%a in ("%~dp0dsh-engine.txt") do if not defined VER set "VER=%%a"
if defined VER set "SPEC=@deepseek-ai/dsh@%VER%"

echo [%date% %time%] ===== DSH web server start (engine %SPEC%) ===== >> "%~dp0dsh-server.log" 2>&1

rem ---- self-heal the profile config before the engine reads it ----
if exist "%~dp0dsh-preflight.ps1" powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0dsh-preflight.ps1" >> "%~dp0dsh-server.log" 2>&1

rem ---- start the engine; all output goes to dsh-server.log ----
call npx --yes "%SPEC%" web >> "%~dp0dsh-server.log" 2>&1

echo [%date% %time%] ===== DSH web server exited ===== >> "%~dp0dsh-server.log" 2>&1
