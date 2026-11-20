@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0app\server.ps1" > "%~dp0diagnostico.txt" 2>&1
