@echo off
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set CORE=src\ScreenFit.cs src\FolderCleaner.cs src\FiveMIni.cs src\CleanTargets.cs src\FiveMLocator.cs src\AppSettings.cs src\FiveMProcess.cs src\HostsFix.cs src\Ts3.cs src\SaltyChat.cs src\Downloader.cs
set REFS=/r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Web.Extensions.dll
if not exist bin mkdir bin

echo [1/3] Tests bauen ...
"%CSC%" /nologo /codepage:65001 /target:exe /out:bin\Tests.exe %REFS% %CORE% tests\Tests.cs || goto :fail

echo [2/3] Tests ausfuehren ...
bin\Tests.exe || goto :fail
if "%1"=="test" exit /b 0

echo [3/3] RetroRoleplayTool.exe bauen ...
"%CSC%" /nologo /codepage:65001 /optimize+ /target:winexe /out:bin\RetroRoleplayTool.exe ^
  /win32icon:assets\app.ico /win32manifest:assets\app.manifest /resource:assets\banner.png,GTACleaner.banner.png ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll %REFS% ^
  %CORE% src\NeonControls.cs src\ExtrasPage.cs src\BotsPage.cs src\MainForm.cs src\Program.cs src\AssemblyInfo.cs || goto :fail
rem Laeuft das Programm gerade, laesst es sich nicht ueberschreiben, aber umbenennen.
if exist RetroRoleplayTool.old.exe del RetroRoleplayTool.old.exe >nul 2>&1
copy /y bin\RetroRoleplayTool.exe RetroRoleplayTool.exe >nul 2>&1 || (
  ren RetroRoleplayTool.exe RetroRoleplayTool.old.exe && copy /y bin\RetroRoleplayTool.exe RetroRoleplayTool.exe >nul
) || goto :fail

echo.
echo Fertig: RetroRoleplayTool.exe
exit /b 0

:fail
echo.
echo BUILD FEHLGESCHLAGEN
exit /b 1
