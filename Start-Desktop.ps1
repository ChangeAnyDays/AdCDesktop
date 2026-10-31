[CmdletBinding(SupportsShouldProcess=$true)]
param([string]$InstallDir,[switch]$ControlPanel)
$ErrorActionPreference='Stop'
# Windows PowerShell 5.1 does not populate $PSScriptRoot inside param() defaults.
if ([string]::IsNullOrWhiteSpace($InstallDir)) { $InstallDir = $PSScriptRoot }
Import-Module (Join-Path $PSScriptRoot 'scripts\AdCDesktop.Common.psm1') -Force
$InstallDir=Get-FullInstallPath $InstallDir
$manifest=Read-DesktopManifest $InstallDir
if (!$PSCmdlet.ShouldProcess($InstallDir,'Start configured desktop components')) { return }
$components=Get-ObjectValue $manifest 'components'
$appearance=Get-ObjectValue $manifest 'appearance'
$wallpaper=Get-ObjectValue $manifest 'wallpaper'
$engine=[string](Get-ObjectValue $wallpaper 'enginePath')
$project=[string](Get-ObjectValue $wallpaper 'projectPath')
$changesShell=[bool](Get-ObjectValue $appearance 'hideDesktopIcons' $false) -or [bool](Get-ObjectValue $appearance 'taskbarAutoHide' $false)
$hasWallpaper=$engine -and $project -and (Test-Path -LiteralPath $engine -PathType Leaf) -and (Test-Path -LiteralPath $project -PathType Leaf)
# Reject missing components and other running installations before saving a
# backup, changing wallpaper, or applying any desktop appearance settings.
foreach ($name in @('nexus','rainmeter','translucenttb')) {
    $relative=[string](Get-ObjectValue $components $name)
    if ($relative) { $null=Assert-InstalledComponentReady $InstallDir $relative }
}
if ($ControlPanel) {
    $panelRelative=[string](Get-ObjectValue $components 'panel')
    $null=Assert-InstalledComponentReady $InstallDir $panelRelative
}
$backupPath=Join-Path $InstallDir 'Backups\original-desktop.json'
$backup=if(Test-Path -LiteralPath $backupPath){Get-Content -LiteralPath $backupPath -Raw -Encoding UTF8|ConvertFrom-Json}else{$null}
if($backup -and [IO.Path]::GetFullPath([string]$backup.installRoot).TrimEnd('\') -ine $InstallDir){throw 'The desktop backup belongs to another installation.'}
if (!(Test-Path -LiteralPath $backupPath) -and ($changesShell -or $hasWallpaper)) {
    Initialize-DesktopShell
    $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced')
    try { $hideIcons=if($key){$key.GetValue('HideIcons')}else{$null} } finally { if($key){$key.Dispose()} }
    $rendererRunning=@(Get-WallpaperRenderers $engine).Count -gt 0
    $originalProject=if($rendererRunning -and $hasWallpaper){Get-CurrentWallpaperProject $engine}else{$null}
    $backup=[pscustomobject][ordered]@{schemaVersion=1;installRoot=$InstallDir;createdAt=(Get-Date -Format 'o');hideIcons=$hideIcons;taskbarAutoHide=(([AdCDesktop.NativeShell]::TaskbarState() -band 1) -ne 0);wallpaper=(Get-ItemProperty -LiteralPath 'HKCU:\Control Panel\Desktop' -Name Wallpaper -ErrorAction SilentlyContinue).Wallpaper;wallpaperEngineWasRunning=$rendererRunning;originalWallpaperProject=$originalProject;wallpaperApplied=$false}
    Write-DesktopJson $backupPath $backup
}
if ($hasWallpaper -and [bool](Get-ObjectValue $backup 'wallpaperEngineWasRunning' $false) -and !(Get-ObjectValue $backup 'originalWallpaperProject')) {
    Write-Warning 'The running Wallpaper Engine selection could not be backed up. Its wallpaper was left unchanged; the other components will still start.'
    $hasWallpaper=$false
}
if ($hasWallpaper) {
    $running=@(Get-WallpaperRenderers $engine)
    if (!$running.Count) { Start-Process -FilePath $engine -WorkingDirectory ([IO.Path]::GetDirectoryName($engine)) -WindowStyle Hidden; Start-Sleep -Seconds 8 }
    Invoke-WallpaperControl $engine 'openWallpaper' $project
    if($null -eq $backup.PSObject.Properties['wallpaperApplied']){$backup|Add-Member -NotePropertyName wallpaperApplied -NotePropertyValue $true}else{$backup.wallpaperApplied=$true}
    Write-DesktopJson $backupPath $backup
    Start-Sleep -Seconds 2
}
if ($changesShell) {
    Initialize-DesktopShell
    if ([bool](Get-ObjectValue $appearance 'hideDesktopIcons' $false)) {
        $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced')
        try {$key.SetValue('HideIcons',1,[Microsoft.Win32.RegistryValueKind]::DWord)}finally{$key.Dispose()}
        [AdCDesktop.NativeShell]::RefreshIcons($false)
    }
    if ([bool](Get-ObjectValue $appearance 'taskbarAutoHide' $false)) { [AdCDesktop.NativeShell]::SetTaskbarAutoHide($true) }
}
foreach ($name in @('nexus','rainmeter','translucenttb')) {
    $relative=[string](Get-ObjectValue $components $name)
    if ($relative) { Start-InstalledComponent $InstallDir $relative }
}
if ($ControlPanel) {
    $panel=Resolve-InstallComponent $InstallDir ([string](Get-ObjectValue $components 'panel'))
    if (!(Test-Path -LiteralPath $panel -PathType Leaf)) { throw 'Control panel executable was not found.' }
    Start-Process -FilePath $panel -ArgumentList ('--root "' + $InstallDir + '"') -WorkingDirectory (Split-Path -Parent $panel)
}
Write-Host 'Configured desktop components are running. Closing the control panel does not close the spectrum.'
