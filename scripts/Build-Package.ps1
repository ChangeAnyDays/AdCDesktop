[CmdletBinding()]
param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (!$SkipBuild) { & (Join-Path $PSScriptRoot 'Build-ControlPanel.ps1') }
$payload = Join-Path $root 'artifacts\control-panel'
foreach ($file in @('AdCDesktop.exe','AdCDesktop.exe.config','QuickStart.html')) {
    if (!(Test-Path -LiteralPath (Join-Path $payload $file) -PathType Leaf)) { throw "Missing build output: $file" }
}
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $payload 'AdCDesktop.exe')).FileVersion
if ($version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'The build does not have a valid file version.' }
$packageRoot = Join-Path $root 'artifacts\packages'
# A fresh staging directory prevents old or personal files entering a later ZIP.
$stage = Join-Path $packageRoot ('staging\AdCat-ControlCenter-' + $version + '-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory((Join-Path $stage 'control-panel'))
foreach ($file in @('AdCDesktop.exe','AdCDesktop.exe.config','QuickStart.html')) {
    Copy-Item -LiteralPath (Join-Path $payload $file) -Destination (Join-Path $stage 'control-panel') -Force
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-ControlCenter.ps1') -Destination $stage -Force
Copy-Item -LiteralPath (Join-Path $root 'Restore-Desktop.ps1') -Destination $stage -Force
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $stage -Force
$cmd = '@echo off' + "`r`n" + 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-ControlCenter.ps1" -Launch' + "`r`n" + 'pause' + "`r`n"
[IO.File]::WriteAllText((Join-Path $stage 'Install.cmd'),$cmd,[Text.Encoding]::ASCII)
Copy-Item -LiteralPath (Join-Path $root 'docs\PACKAGE-README.txt') -Destination (Join-Path $stage 'START-HERE.txt') -Force
$zip = Join-Path $packageRoot ('AdCat-ControlCenter-' + $version + '.zip')
Compress-Archive -LiteralPath (Join-Path $stage 'control-panel'),(Join-Path $stage 'Install-ControlCenter.ps1'),(Join-Path $stage 'Restore-Desktop.ps1'),(Join-Path $stage 'Install.cmd'),(Join-Path $stage 'START-HERE.txt'),(Join-Path $stage 'LICENSE') -DestinationPath $zip -Force
$hash = Get-FileHash -LiteralPath $zip -Algorithm SHA256
[IO.File]::WriteAllText(($zip + '.sha256'),($hash.Hash + '  ' + [IO.Path]::GetFileName($zip) + "`r`n"),[Text.Encoding]::ASCII)
Write-Output $zip
