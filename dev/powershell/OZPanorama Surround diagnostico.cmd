@echo off
rem OZPanorama - diagnostico do NVIDIA Surround. So le: nao muda nenhum ecra.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0app\surround.ps1" -Action diag > "%~dp0surround_diagnostico.txt" 2>&1
start "" notepad "%~dp0surround_diagnostico.txt"
