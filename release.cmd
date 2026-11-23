@echo off
rem Runs release.ps1 without changing the system execution policy.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0release.ps1" %*
