@echo off
rem Cria o atalho "OZPanorama" com o icone no ambiente de trabalho.
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut([Environment]::GetFolderPath('Desktop')+'\OZPanorama.lnk'); $s.TargetPath='%~dp0OZPanorama.cmd'; $s.WorkingDirectory='%~dp0'; $s.IconLocation='%~dp0app\icone.ico'; $s.WindowStyle=7; $s.Save()"
echo Atalho criado no ambiente de trabalho.
timeout /t 3 >nul
