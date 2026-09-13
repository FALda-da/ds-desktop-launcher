@echo off
chcp 65001 >nul
title DSH Mobile Mode
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-mobile.ps1"
echo.
pause
