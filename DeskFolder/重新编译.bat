@echo off
setlocal
cd /d "%~dp0"
set FW=C:\Windows\Microsoft.NET\Framework64\v4.0.30319
tasklist /FI "IMAGENAME eq DeskFolder.exe" 2>nul | find /I "DeskFolder.exe" >nul
if not errorlevel 1 (
  echo DeskFolder.exe is running. Please right-click the tray icon and choose Exit first.
  pause
  exit /b 1
)
"%FW%\csc.exe" /nologo /target:winexe /platform:anycpu /out:DeskFolder.exe /win32manifest:app.manifest ^
 /r:"%FW%\System.Web.Extensions.dll" /r:System.dll /r:System.Core.dll /r:System.Xml.dll ^
 /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
 /r:"%FW%\WPF\WindowsBase.dll" /r:"%FW%\WPF\PresentationCore.dll" /r:"%FW%\WPF\PresentationFramework.dll" ^
 /r:"%FW%\System.Xaml.dll" Program.cs
if errorlevel 1 (
  echo Build failed. See error messages above.
  pause
  exit /b 1
)
echo Build OK: DeskFolder.exe updated.
pause