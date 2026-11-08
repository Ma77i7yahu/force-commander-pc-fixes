@echo off
rem Rebuilds PatchData.cs + ..\dist\PATCHES.txt from the tested files, then the installer.
rem Usage: build.cmd ["path to Force Commander\Resource"]
rem Needs Python with capstone (for the check that the options don't depend on each other).
cd /d "%~dp0"
G:\msys64\mingw64\bin\python.exe gen.py %1 || exit /b 1
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /optimize /out:..\dist\ForceCommanderModInstaller.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll Installer.cs PatchData.cs || exit /b 1
