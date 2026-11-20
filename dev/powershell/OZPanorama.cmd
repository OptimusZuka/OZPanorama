@echo off
rem OZPanorama - Zuka Force. Abre o configurador no browser (nao precisa de instalar nada).
start "" powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0app\server.ps1"
