[CmdletBinding(SupportsShouldProcess=$true)]
param(
    [ValidateSet('Standard','Full')][string]$Mode = 'Full',
    [string]$InstallDir
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($InstallDir)) { $InstallDir = $PSScriptRoot }
Import-Module (Join-Path $PSScriptRoot 'scripts\AdCDesktop.Common.psm1') -Force
$InstallDir = Get-FullInstallPath $InstallDir
$manifestPath = Join-Path $InstallDir 'desktop-style.json'
$manifest = Read-DesktopManifest $InstallDir
$backupPath = Join-Path $InstallDir 'Backups\original-desktop.json'
$backup = if (Test-Path -LiteralPath $backupPath) { Get-Content -LiteralPath $backupPath -Raw -Encoding UTF8 | ConvertFrom-Json } else { $null }
if ($backup -and [IO.Path]::GetFullPath([string]$backup.installRoot).TrimEnd('\') -ine $InstallDir) {
    throw 'The desktop backup belongs to another installation.'
}
if (!$PSCmdlet.ShouldProcess($InstallDir, ('Switch desktop effect to ' + $Mode))) { return }

$appearance = Get-ObjectValue $manifest 'appearance'
if ($null -eq $appearance) {
    $appearance = [pscustomobject]@{ hideDesktopIcons = $false; taskbarAutoHide = $false }
    $manifest | Add-Member -NotePropertyName appearance -NotePropertyValue $appearance -Force
}
if (!$appearance.PSObject.Properties['hideDesktopIcons']) { $appearance | Add-Member -NotePropertyName hideDesktopIcons -NotePropertyValue $false -Force }
if (!$appearance.PSObject.Properties['taskbarAutoHide']) { $appearance | Add-Member -NotePropertyName taskbarAutoHide -NotePropertyValue $false -Force }
$appearance.hideDesktopIcons = ($Mode -eq 'Full')
$appearance.taskbarAutoHide = ($Mode -eq 'Full')
if (!$manifest.PSObject.Properties['mode']) { $manifest | Add-Member -NotePropertyName mode -NotePropertyValue $Mode -Force }
else { $manifest.mode = if ($Mode -eq 'Full') { 'Full' } else { 'ControlPanel' } }
Write-DesktopJson $manifestPath $manifest

if ($Mode -eq 'Full') {
    & (Join-Path $PSScriptRoot 'Start-Desktop.ps1') -InstallDir $InstallDir
    Write-Host 'Full desktop effect applied: icons hidden, taskbar auto-hide on, Nexus and spectrum started.'
    return
}

Initialize-DesktopShell
$key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced')
try { $key.SetValue('HideIcons', 0, [Microsoft.Win32.RegistryValueKind]::DWord) } finally { $key.Dispose() }
[AdCDesktop.NativeShell]::RefreshIcons($true)
[AdCDesktop.NativeShell]::SetTaskbarAutoHide($false)
& (Join-Path $PSScriptRoot 'Start-Desktop.ps1') -InstallDir $InstallDir
Write-Host 'Standard desktop effect applied: icons and taskbar shown, Nexus and spectrum started.'
