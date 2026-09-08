@echo off
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe

"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll /out:"%~dp0CampusNetAutoLogin.exe" "%~dp0Program.cs"

if errorlevel 1 (
  echo Build failed.
  pause
) else (
  echo Built: %~dp0CampusNetAutoLogin.exe
  pause
)
