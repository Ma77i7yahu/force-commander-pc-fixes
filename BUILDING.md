# Building

You don't need any of this to install the mods. It's for anyone who wants to rebuild the installer or port the patches.

## Installer only

`src/PatchData.cs` already holds every patch, so the installer builds with the C# compiler that ships with Windows (.NET Framework 4):

```
cd src
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /optimize /out:ForceCommanderModInstaller.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll Installer.cs PatchData.cs
```

## Regenerating the patches

`src/build.cmd` runs `gen.py` first, which rebuilds `PatchData.cs` and `PATCHES.txt` from the tested files in the game's `Resource` folder: every changed range of `focom.exe` is assigned to an option (it stops on an unknown one), and a capstone check makes sure no option's code refers to another's. The tested files are modified game files, so they aren't in this repo. The AI option moves one script object to the end of `fcv1p1.rpk`; the installer rebuilds it from the player's own file plus the new bytes, so no game data is stored. `build.cmd` expects Python (with capstone) from MSYS2's MinGW-w64 at `G:\msys64`, so change that path to suit your machine.

## Tests

`test/harness.cs` loads the installer by reflection and checks its output against the tested files (53 checks), and `test/run_tests.ps1` sets up the scratch copy of the stock files it needs (about 300 MB; delete it afterwards). `test/shot.cs` renders the installer window to a PNG.
