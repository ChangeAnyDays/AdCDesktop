[CmdletBinding(SupportsShouldProcess=$true)]
param([string]$InstallDir=$PSScriptRoot)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'scripts\AdCDesktop.Common.psm1') -Force
$InstallDir=Get-FullInstallPath $InstallDir
$manifest=Read-DesktopManifest $InstallDir
$backupPath=Join-Path $InstallDir 'Backups\original-desktop.json'
$backup=if(Test-Path -LiteralPath $backupPath){Get-Content -LiteralPath $backupPath -Raw -Encoding UTF8|ConvertFrom-Json}else{$null}
if ($backup -and [IO.Path]::GetFullPath([string]$backup.installRoot).TrimEnd('\') -ine $InstallDir) { throw 'The backup belongs to a different installation; nothing was changed.' }
if (!$PSCmdlet.ShouldProcess($InstallDir,'Stop only this installation and restore its original desktop settings')) { return }
$components=Get-ObjectValue $manifest 'components'
$knownPaths=@()
foreach($name in @('panel','rainmeter','nexus','translucenttb')){
    $relative=[string](Get-ObjectValue $components $name)
    if($relative){$knownPaths+=Resolve-InstallComponent $InstallDir $relative}
}
foreach($path in $knownPaths){
    $name=[IO.Path]::GetFileNameWithoutExtension($path)
    foreach($candidate in @(Get-Process -Name $name -ErrorAction SilentlyContinue)){
        try {if($candidate.Path -ieq $path){$again=Get-Process -Id $candidate.Id -ErrorAction SilentlyContinue;if($again -and $again.Path -ieq $path){Stop-Process -InputObject $again -Force;[void]$again.WaitForExit(5000)}}}catch{Write-Warning ("Could not stop this installation's component: " + $path + '. ' + $_.Exception.Message)}
    }
}
if($backup){
    Initialize-DesktopShell
    $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced')
    try{if($null -eq $backup.hideIcons){$key.DeleteValue('HideIcons',$false)}else{$key.SetValue('HideIcons',[int]$backup.hideIcons,[Microsoft.Win32.RegistryValueKind]::DWord)}}finally{$key.Dispose()}
    [AdCDesktop.NativeShell]::RefreshIcons($backup.hideIcons -ne 1)
    [AdCDesktop.NativeShell]::SetTaskbarAutoHide([bool]$backup.taskbarAutoHide)
    $wallpaper=Get-ObjectValue $manifest 'wallpaper'
    $engine=[string](Get-ObjectValue $wallpaper 'enginePath')
    if($engine -and (Get-ObjectValue $backup 'wallpaperApplied' $false)){
        $running=@(Get-WallpaperRenderers $engine)
        $originalProject=[string](Get-ObjectValue $backup 'originalWallpaperProject')
        if($running.Count -and $originalProject -and (Test-Path -LiteralPath $originalProject -PathType Leaf)){
            Invoke-WallpaperControl $engine 'openWallpaper' $originalProject
        }elseif(!(Get-ObjectValue $backup 'wallpaperEngineWasRunning' $true)){
            if($running.Count){Invoke-WallpaperControl $engine 'closeWallpaper' $null}
            if($backup.wallpaper -and (Test-Path -LiteralPath $backup.wallpaper -PathType Leaf)){[AdCDesktop.NativeShell]::SetWallpaper([string]$backup.wallpaper)}
        }else{
            Write-Warning 'The original dynamic wallpaper could not be reopened: Wallpaper Engine is no longer running or the original project file is missing. The icon and taskbar settings were still restored.'
        }
    }
}
Write-Host 'This installation was stopped and any available desktop backup was processed; review warnings above. No files were deleted and Wallpaper Engine was not killed.'
