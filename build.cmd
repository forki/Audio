@echo off
cls

.paket\paket.exe restore
if errorlevel 1 (
  exit /b %errorlevel%
)

dotnet tool install --tool-path tools fake-cli
tools\fake.exe build --target %*
