@echo off
setlocal
if not exist "%~dp0PresenterHelper\LummaSimulator.PresenterHelper.exe" (
  echo Presenter Helper ontbreekt. Pak het volledige pakket uit.
  exit /b 1
)
start "" "%~dp0PresenterHelper\LummaSimulator.PresenterHelper.exe"
