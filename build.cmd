@echo off
rem OZPanorama · Zuka Force — compila o OZPanorama.exe e os testes com o compilador C# que vem com o Windows (.NET Framework 4.x).
rem Não instala nada nem altera o sistema. Resultado em build\
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" ( echo Compilador C# do .NET Framework 4 nao encontrado. & exit /b 1 )
if not exist build mkdir build
set REFS=/r:System.Web.Extensions.dll /r:System.Management.dll /r:System.Windows.Forms.dll
set RES=/resource:app\index.html,OZPanorama.index.html /resource:app\engine.js,OZPanorama.engine.js /resource:app\i18n.js,OZPanorama.i18n.js /resource:app\assets\icone.png,OZPanorama.icone.png /resource:app\surround.ps1,OZPanorama.surround.ps1 /resource:app\assets\jogos\ets2.jpg,OZPanorama.jogo.ets2.jpg /resource:app\assets\jogos\ats.jpg,OZPanorama.jogo.ats.jpg /resource:app\assets\jogos\iracing.jpg,OZPanorama.jogo.iracing.jpg /resource:app\assets\jogos\raceroom.jpg,OZPanorama.jogo.raceroom.jpg /resource:app\assets\jogos\ac.jpg,OZPanorama.jogo.ac.jpg /resource:app\assets\jogos\lmu.jpg,OZPanorama.jogo.lmu.jpg /resource:app\assets\jogos\msfs.jpg,OZPanorama.jogo.msfs.jpg
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /out:build\OZPanorama.exe /win32icon:app\icone.ico %REFS% %RES% /main:OZPanorama.Program src\Core.cs src\Games.cs src\Host.cs src\Window.cs || exit /b 1
"%CSC%" /nologo /target:exe /out:build\OZPanorama.Tests.exe %REFS% %RES% /main:OZPanorama.Tests.TestMain src\Core.cs src\Games.cs src\Host.cs src\Window.cs src\Tests.cs || exit /b 1
echo.
echo Compilado: build\OZPanorama.exe e build\OZPanorama.Tests.exe
if "%1"=="test" build\OZPanorama.Tests.exe
