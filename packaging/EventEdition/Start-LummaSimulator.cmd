@echo off
setlocal
powershell.exe -NoLogo -NoProfile -File "%~dp0Demo.ps1" -Action App
set "result=%errorlevel%"
if not "%result%"=="0" pause
exit /b %result%
