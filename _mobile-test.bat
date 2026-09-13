@echo off
cd /d "%~dp0"
echo [%date% %time%] ===== mobile-mode test start ===== >> "%~dp0mobile-test.log" 2>&1
npx --yes @deepseek-ai/dsh web --patch lan-access.yml --port 3090 --no-open >> "%~dp0mobile-test.log" 2>&1
echo [%date% %time%] ===== mobile-mode test exited ===== >> "%~dp0mobile-test.log" 2>&1
