# Builds the harness, makes a throwaway copy of the original game files and runs every check.
# The real game folder is only read.
$ErrorActionPreference = 'Stop'
$real = 'G:\Game Files\LucasArts\Star Wars Force Commander\Resource'
$here = $PSScriptRoot
$run  = Join-Path $here 'run\Resource'
if (Test-Path (Join-Path $here 'run')) { Remove-Item (Join-Path $here 'run') -Recurse -Force -Confirm:$false }
New-Item -ItemType Directory -Force (Join-Path $run 'forcecommand') | Out-Null
Copy-Item "$real\focom_original.exe" "$run\focom.exe"
Copy-Item "$real\dgVoodoo_original.conf" "$run\dgVoodoo.conf"
Copy-Item "$real\ddraw.dll" "$run\ddraw.dll"
Copy-Item "$real\forcecommand\fcv1p1_original.rpk" "$run\forcecommand\fcv1p1.rpk"
Copy-Item "$real\forcecommand\forcecommand_pre_bldgs.rpk" "$run\forcecommand\forcecommand.rpk"
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /platform:x86 "/out:$here\harness.exe" /r:System.Windows.Forms.dll "$here\harness.cs"
if ($LASTEXITCODE -ne 0) { exit 1 }
& $csc /nologo /platform:x86 "/out:$here\shot.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll "$here\shot.cs"
if ($LASTEXITCODE -ne 0) { exit 1 }
& "$here\harness.exe" (Resolve-Path "$here\..\dist\ForceCommanderModInstaller.exe").Path $run $real
exit $LASTEXITCODE
