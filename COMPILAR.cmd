@echo off
setlocal
cd /d "%~dp0"
set "AD_COMPILER=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist NAudio.dll (
  powershell.exe -NoProfile -File "%~dp0RESTAURAR.ps1"
  if errorlevel 1 exit /b 1
)
if not exist assets\AudioDuo.ico (
  if not exist lib mkdir lib
  "%AD_COMPILER%" /nologo /target:exe /out:lib\Artwork.exe /reference:System.Drawing.dll herramientas\Artwork.cs
  if errorlevel 1 exit /b 1
  lib\Artwork.exe "%~dp0assets"
  if errorlevel 1 exit /b 1
)
"%AD_COMPILER%" /nologo /warnaserror /codepage:65001 /target:winexe /platform:x64 /optimize+ /win32icon:assets\AudioDuo.ico /win32manifest:codigo\app.manifest /out:AudioDuo.nuevo.exe /reference:System.dll /reference:System.Core.dll /reference:System.Xml.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:NAudio.dll codigo\*.cs
if errorlevel 1 exit /b 1
start "" /wait AudioDuo.nuevo.exe --self-test
if errorlevel 1 exit /b 1
move /y AudioDuo.nuevo.exe AudioDuo.exe >nul
if errorlevel 1 (
  echo Cierra AudioDuo desde Salir y vuelve a compilar. La version anterior se conserva.
  exit /b 1
)
echo AudioDuo compilado y validado.
exit /b 0
