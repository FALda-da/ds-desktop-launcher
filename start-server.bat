@echo off
cd /d "%~dp0"
echo [%date% %time%] ===== DSH web server start ===== >> "%~dp0dsh-server.log" 2>&1
npx --yes @deepseek-ai/dsh web >> "%~dp0dsh-server.log" 2>&1
echo [%date% %time%] ===== DSH web server exited ===== >> "%~dp0dsh-server.log" 2>&1
