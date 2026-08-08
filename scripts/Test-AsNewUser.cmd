@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Test-AsNewUser.ps1" %*
