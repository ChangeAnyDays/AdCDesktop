[CmdletBinding(SupportsShouldProcess=$true)]
param(
    [string]$InstallDir,
    [ValidateSet('ControlPanel','Full')][string]$Mode = 'ControlPanel',
    [switch]$Offline,
    [string]$CacheDir,
    [string]$PreparedComponentsPath,
    [string]$PreparedControlPanelPath,
    [switch]$IncludeNexus,
    [switch]$IncludeTaskbar,
    [string]$WallpaperEnginePath,
    [string]$WallpaperProject,
    [switch]$NoShortcuts,
    [switch]$Launch
)

$ErrorActionPreference = 'Stop'
# Nexus is part of the default install. Pass -IncludeNexus:$false for a
# spectrum-only installation; -IncludeNexus remains accepted for scripts.
if (!$PSBoundParameters.ContainsKey('IncludeNexus')) { $IncludeNexus = $true }
Import-Module (Join-Path $PSScriptRoot 'scripts\AdCDesktop.Common.psm1') -Force
if (!$InstallDir) {
    $InstallDir = if (Test-Path -LiteralPath 'D:\') { 'D:\AdCDesktop' } else { Join-Path $env:LOCALAPPDATA 'AdCDesktop' }
}
$InstallDir = Get-FullInstallPath $InstallDir
if ($InstallDir -ieq [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')) { throw 'Choose an installation directory separate from the source repository.' }
if (!$CacheDir) { $CacheDir = Join-Path $PSScriptRoot '.cache\dependencies' }
$CacheDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($CacheDir)
$existingManifest = if (Test-Path -LiteralPath (Join-Path $InstallDir 'desktop-style.json')) { Read-DesktopManifest $InstallDir } else { $null }
if ($existingManifest -and !$PSBoundParameters.ContainsKey('Mode')) { $Mode = [string](Get-ObjectValue $existingManifest 'mode' $Mode) }
if ($existingManifest -and (Get-ObjectValue $existingManifest 'mode') -eq 'Full' -and $Mode -eq 'ControlPanel') {
    throw 'A Full installation cannot be changed to ControlPanel in place. Restore its desktop first, then choose a separate installation directory for ControlPanel mode.'
}
if ($IncludeTaskbar -or $WallpaperEnginePath -or $WallpaperProject) {
    if ($Mode -ne 'Full') { throw 'TranslucentTB and wallpaper require -Mode Full.' }
}

# This branch must remain before every filesystem, registry, native or process mutation.
if ($WhatIfPreference) {
    [pscustomobject]@{
        Plan = 'Install only; do not start applications or change the desktop'
        InstallDir = $InstallDir; Mode = $Mode; Offline = [bool]$Offline
        Components = @('Rainmeter portable','Welfare clock skin data','RetrOcOlOr spectrum skin data','AdCDesktop control panel')
        Nexus = [bool]$IncludeNexus; TransparentTaskbar = [bool]$IncludeTaskbar
        DesktopShortcuts = (!$NoShortcuts -and !$Offline)
        LaunchRequested = [bool]$Launch
        Note = 'No downloads, installers, build, directory creation or desktop changes were performed.'
    }
    return
}
if ($env:OS -ne 'Windows_NT') { throw 'This project requires Windows 11 x64 and Windows PowerShell 5.1 or newer.' }
if (!$PSCmdlet.ShouldProcess($InstallDir, 'Install AdCDesktop without starting it')) { return }

$dependencyManifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'config\dependencies.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$rainmeterExe = Join-Path $InstallDir 'Rainmeter\Rainmeter.exe'
$spectrumIni = Join-Path $InstallDir 'Rainmeter\Skins\RetrOcOlOr\Visualizer\Visualizer.ini'
$variablesIni = Join-Path $InstallDir 'Rainmeter\Skins\RetrOcOlOr\@Resources\Variables.inc'
$clockIni = Join-Path $InstallDir 'Rainmeter\Skins\Welfare\Clock\Clock.ini'

if ($Offline) {
    $componentRoot = if ($PreparedComponentsPath) { Get-FullInstallPath $PreparedComponentsPath } else { $InstallDir }
    foreach ($relative in @('Rainmeter\Rainmeter.exe','Rainmeter\Skins\RetrOcOlOr\Visualizer\Visualizer.ini','Rainmeter\Skins\RetrOcOlOr\@Resources\Variables.inc','Rainmeter\Skins\Welfare\Clock\Clock.ini')) {
        if (!(Test-Path -LiteralPath (Join-Path $componentRoot $relative) -PathType Leaf)) { throw "Offline mode requires prepared component: $relative. It never runs installers." }
    }
    if ($IncludeNexus -and !(Test-Path -LiteralPath (Join-Path $componentRoot 'Nexus\Nexus.exe') -PathType Leaf)) { throw 'Offline Nexus must already be prepared.' }
    if ($IncludeTaskbar -and !(Test-Path -LiteralPath (Join-Path $componentRoot 'TranslucentTB\TranslucentTB.exe') -PathType Leaf)) { throw 'Offline TranslucentTB must already be prepared.' }
}

$newSpectrum = !(Test-Path -LiteralPath $spectrumIni)
$newClock = !(Test-Path -LiteralPath $clockIni)
[void][IO.Directory]::CreateDirectory($InstallDir)

if ($PreparedComponentsPath) {
    $prepared = Get-FullInstallPath $PreparedComponentsPath
    if ($prepared -eq $InstallDir) { throw 'PreparedComponentsPath must differ from InstallDir.' }
    foreach ($component in @('Rainmeter','Nexus','TranslucentTB')) {
        $source = Join-Path $prepared $component
        $destination = Join-Path $InstallDir $component
        if ((Test-Path -LiteralPath $source -PathType Container) -and !(Test-Path -LiteralPath $destination)) { Copy-Item -LiteralPath $source -Destination $destination -Recurse }
    }
}

if (!(Test-Path -LiteralPath $rainmeterExe)) {
    if ($Offline) { throw 'Rainmeter is not prepared; offline mode will not run its installer.' }
    $installer = Get-VerifiedArchive (Get-Dependency $dependencyManifest 'rainmeter') $CacheDir
    $arguments = '/S /PORTABLE=1 /RESTART=0 /VERSION=64 /D=' + (Join-Path $InstallDir 'Rainmeter')
    Write-Host 'Installing the official portable Rainmeter package. No desktop component will be started.'
    $process = Start-Process -FilePath $installer -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $rainmeterExe)) { throw 'Portable Rainmeter installation did not complete.' }
}

foreach ($skin in @(@{Id='welfare';Name='Welfare';Target=$clockIni}, @{Id='retrocolor';Name='RetrOcOlOr';Target=$spectrumIni})) {
    if (!(Test-Path -LiteralPath $skin.Target)) {
        if ($Offline) { throw "Offline skin is missing: $($skin.Name)" }
        $dependency = Get-Dependency $dependencyManifest $skin.Id
        $archive = Get-VerifiedArchive $dependency $CacheDir
        Install-SkinData $archive $dependency $skin.Name (Join-Path $InstallDir 'Rainmeter\Skins') $CacheDir
    }
}

Add-Type -AssemblyName System.Windows.Forms
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$width = [Math]::Min(700, [Math]::Max(200, $screen.Width - 80))
$x = [Math]::Max(0, [int](($screen.Width - $width) / 2))
$y = [Math]::Max(0, $screen.Height - 201)
$settingsPath = Join-Path $InstallDir 'Rainmeter\Rainmeter.ini'
if (!(Test-Path -LiteralPath $settingsPath) -or !$existingManifest) {
    # All generated machine-specific paths stay in the installation directory, never in Git.
    if (Test-Path -LiteralPath $settingsPath) { Copy-Item -LiteralPath $settingsPath -Destination ($settingsPath + '.before-adcdesktop') -Force }
    $settings = @"
[Rainmeter]
Language=2052
SkinPath=$(Join-Path $InstallDir 'Rainmeter\Skins')\
DisableDragging=1
NormalStayDesktop=1

[Welfare\Clock]
Active=1
WindowX=50%
WindowY=76%
AnchorX=50%
AnchorY=50%
AlwaysOnTop=-2
Draggable=0
ClickThrough=1
SavePosition=1
KeepOnScreen=1

[RetrOcOlOr\Visualizer]
Active=1
WindowX=$x
WindowY=$y
AnchorX=0
AnchorY=0
AlwaysOnTop=-2
Draggable=0
ClickThrough=1
SavePosition=1
KeepOnScreen=1
AlphaValue=255
OnHover=0

[illustro\Clock]
Active=0
[illustro\Disk]
Active=0
[illustro\System]
Active=0
[illustro\Welcome]
Active=0
"@
    Write-Utf8File $settingsPath $settings
}
if ($newSpectrum) {
    Set-IniValue $spectrumIni 'Variables' 'BarWidth' ([string]$width)
    Set-IniValue $spectrumIni 'Variables' 'BarHeight' '60'
    Set-IniValue $spectrumIni 'MeterWaweBar' 'Y' '0'
    Set-IniValue $spectrumIni 'MeasureAudio' 'Sensitivity' '45'
}
$clockVariables = Join-Path $InstallDir 'Rainmeter\Skins\Welfare\@Resources\Variables.inc'
if ($newClock) {
    Set-IniValue $clockIni 'Rainmeter' 'LeftMouseDoubleClickAction' '!ToggleConfig "Welfare\Settings" "Settings.ini"'
    Set-IniValue $clockVariables 'Variables' 'Format' '%H'
    Set-IniValue $clockVariables 'Variables' 'Hidden' '0'
    Set-IniValue $clockVariables 'Variables' 'Hidden2' '1'
}
# Welfare 1.1 uses %#Format#:%M and Hidden=#Hidden#, which Rainmeter 4.5
# renders as garbled text and shows both 12h and 24h meters. Repair only the
# broken layout so existing custom clocks are left alone.
if ((Test-Path -LiteralPath $clockIni) -and (Test-Path -LiteralPath $clockVariables)) {
    $clockText = Get-Content -LiteralPath $clockIni -Raw -Encoding UTF8
    if ($clockText -match '(?m)^Format=%#Format#:%M') {
        Set-IniValue $clockIni 'MeasureTime' 'Format' '%H:%M'
        Set-IniValue $clockIni 'Meter24hTime' 'Hidden' '0'
        Set-IniValue $clockIni 'Meter12hTime' 'Hidden' '1'
        Set-IniValue $clockVariables 'Variables' 'Format' '%H'
    }
    if ($clockText -match '@include2=#@#Language\\#Language#\.inc') {
        Set-IniValue $clockIni 'Variables' '@include2' '#@#Language\English.inc'
    }
}

$tbbRelative = [string](Get-ObjectValue (Get-ObjectValue $existingManifest 'components') 'translucenttb')
if ($IncludeTaskbar) {
    if ([Environment]::OSVersion.Version.Build -lt 22000) { throw 'The pinned TranslucentTB portable build requires Windows 11.' }
    $tbbPath = Join-Path $InstallDir 'TranslucentTB\TranslucentTB.exe'
    if (!(Test-Path -LiteralPath $tbbPath)) {
        if ($Offline) { throw 'TranslucentTB is not prepared.' }
        $archive = Get-VerifiedArchive (Get-Dependency $dependencyManifest 'translucenttb') $CacheDir
        Expand-VerifiedZip $archive (Join-Path $InstallDir 'TranslucentTB')
        if (!(Test-Path -LiteralPath $tbbPath)) {
            $nestedExe = Get-ChildItem -LiteralPath (Join-Path $InstallDir 'TranslucentTB') -Filter TranslucentTB.exe -File -Recurse | Select-Object -First 1
            if (!$nestedExe) { throw 'The official TranslucentTB archive has an unsupported layout.' }
            $tbbRelative = $nestedExe.FullName.Substring($InstallDir.Length + 1)
        }
    }
    if (!$tbbRelative) { $tbbRelative = 'TranslucentTB\TranslucentTB.exe' }
    $tbbSettings = Join-Path (Split-Path -Parent (Resolve-InstallComponent $InstallDir $tbbRelative)) 'settings.json'
    if (!(Test-Path -LiteralPath $tbbSettings)) { Write-DesktopJson $tbbSettings @{desktop_appearance=@{accent='clear';color='#00000000';show_line=$false};visible_window_appearance=@{enabled=$false};maximized_window_appearance=@{enabled=$false}} }
}

$nexusRelative = [string](Get-ObjectValue (Get-ObjectValue $existingManifest 'components') 'nexus')
if ($IncludeNexus) {
    $nexusExe = Join-Path $InstallDir 'Nexus\Nexus.exe'
    if (!(Test-Path -LiteralPath $nexusExe)) {
        if ($Offline) { throw 'Nexus is not prepared.' }
        if (Test-Path -LiteralPath 'HKCU:\Software\WinSTEP2000\Nexus') { throw 'An existing Nexus profile was found. Keep that installation or export its settings before installing a second copy; no existing profile was changed.' }
        $dependency = Get-Dependency $dependencyManifest 'nexus'
        $archive = Get-VerifiedArchive $dependency $CacheDir
        $setupDirectory = Join-Path $CacheDir 'nexus-setup'
        Expand-VerifiedZip $archive $setupDirectory
        $setup = Join-Path $setupDirectory ([string](Get-ObjectValue $dependency 'installerFileName' 'NexusSetup.exe'))
        $expected = [string](Get-ObjectValue $dependency 'installerSha256')
        if ($expected -notmatch '^[a-fA-F0-9]{64}$' -or (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash -ine $expected) { throw 'Nexus installer failed SHA256 verification.' }
        Write-Host 'Nexus is optional proprietary freeware. Windows may ask for administrator approval; approve the official installer in Windows to continue. This script does not approve it for you.'
        $arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /TASKS= /DIR="' + (Join-Path $InstallDir 'Nexus') + '"'
        $process = Start-Process -FilePath $setup -ArgumentList $arguments -PassThru -Wait
        if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $nexusExe)) { throw 'Nexus installation was cancelled or did not complete.' }
        $run = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run', $true)
        if ($run) { try { $value = [string]$run.GetValue('Nexus'); if ($value -like ('*' + $nexusExe + '*')) { $run.DeleteValue('Nexus', $false) } } finally { $run.Dispose() } }
        $profile = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\WinSTEP2000\Nexus')
        try { $profile.SetValue('GenAutoRun','False',[Microsoft.Win32.RegistryValueKind]::String) } finally { $profile.Dispose() }
        # A fresh Nexus profile starts as a top dock (orientation 2). Put the
        # first Dock at the bottom so the default install is immediately usable;
        # existing profiles never reach this block.
        $docks = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\WinSTEP2000\NeXuS\Docks', $true)
        if ($docks) {
            try {
                if ([string]$docks.GetValue('DockOrientation1') -ne '3') {
                    $docks.SetValue('DockOrientation1','3',[Microsoft.Win32.RegistryValueKind]::String)
                    $docks.SetValue('DockDocked1','True',[Microsoft.Win32.RegistryValueKind]::String)
                }
            } finally { $docks.Dispose() }
        }
    }
    $nexusRelative = 'Nexus\Nexus.exe'
}

$panelOutput = if ($PreparedControlPanelPath) { Get-FullInstallPath $PreparedControlPanelPath } else { Join-Path $PSScriptRoot 'artifacts\control-panel' }
if (!(Test-Path -LiteralPath (Join-Path $panelOutput 'AdCDesktop.exe'))) {
    if ($PreparedControlPanelPath) { throw 'PreparedControlPanelPath must contain AdCDesktop.exe.' }
    & (Join-Path $PSScriptRoot 'scripts\Build-ControlPanel.ps1') -OutputDir $panelOutput
}
if (!(Test-Path -LiteralPath (Join-Path $panelOutput 'AdCDesktop.exe'))) { throw 'The control panel build output is missing.' }
[void][IO.Directory]::CreateDirectory((Join-Path $InstallDir 'SpectrumControl'))
Get-ChildItem -LiteralPath $panelOutput -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $InstallDir ('SpectrumControl\' + $_.Name)) -Force }
[void][IO.Directory]::CreateDirectory((Join-Path $InstallDir 'scripts'))
foreach ($file in @('AdCDesktop.Common.psm1','DesktopShell.cs')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('scripts\' + $file)) -Destination (Join-Path $InstallDir ('scripts\' + $file)) -Force }
foreach ($file in @('Start-Desktop.ps1','Restore-Desktop.ps1','Switch-Desktop.ps1')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $InstallDir $file) -Force }
foreach ($name in @('Start-Desktop','Restore-Desktop','Switch-Desktop')) { [IO.File]::WriteAllText((Join-Path $InstallDir ($name + '.cmd')), "@echo off`r`npowershell.exe -NoProfile -ExecutionPolicy Bypass -File `"%~dp0$name.ps1`" -InstallDir `"%~dp0.`" %*`r`nif errorlevel 1 pause`r`n", [Text.Encoding]::ASCII) }

$engine = if ($Mode -eq 'Full' -and (!$Offline -or $WallpaperEnginePath)) { Find-WallpaperEngine $WallpaperEnginePath } else { $null }
if ($WallpaperProject -and !(Test-Path -LiteralPath $WallpaperProject -PathType Leaf)) { throw 'WallpaperProject must point to a local project.json owned or subscribed to by this user.' }
$project = if ($WallpaperProject) { $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($WallpaperProject) } elseif ($engine) { Find-DomeProject $engine } else { $null }
if ($existingManifest -and !$WallpaperProject -and !$WallpaperEnginePath) {
    $engine = Get-ObjectValue (Get-ObjectValue $existingManifest 'wallpaper') 'enginePath' $engine
    $project = Get-ObjectValue (Get-ObjectValue $existingManifest 'wallpaper') 'projectPath' $project
}
if ($project -and (!$existingManifest -or $WallpaperProject)) {
    $projectData = Get-Content -LiteralPath $project -Raw -Encoding UTF8 | ConvertFrom-Json
    $isDome = ([string](Get-ObjectValue $projectData 'workshopid' '') -eq '3113287126') -or ((Split-Path -Leaf (Split-Path -Parent $project)) -eq '3113287126')
    if ($isDome) {
        $sourceFolder = Split-Path -Parent $project
        if (!(Test-Path -LiteralPath (Join-Path $sourceFolder 'scene.pkg') -PathType Leaf)) { throw 'The subscribed Dome wallpaper is incomplete. Let Steam finish its download before installing this preset.' }
        $privateFolder = Join-Path $InstallDir 'Wallpaper\Dome'
        [void][IO.Directory]::CreateDirectory($privateFolder)
        foreach ($fileName in @('project.json','scene.pkg','preview.gif')) {
            $sourceFile = Join-Path $sourceFolder $fileName
            $destinationFile = Join-Path $privateFolder $fileName
            if ((Test-Path -LiteralPath $sourceFile -PathType Leaf) -and ([IO.Path]::GetFullPath($sourceFile) -ine [IO.Path]::GetFullPath($destinationFile))) { Copy-Item -LiteralPath $sourceFile -Destination $destinationFile -Force }
        }
        $properties = Get-ObjectValue (Get-ObjectValue $projectData 'general') 'properties'
        foreach ($name in @('clock','date','day','mediavisualizer','audiovisualizer')) {
            $property = Get-ObjectValue $properties $name
            if ($property) { $property.value = $false }
        }
        $music = Get-ObjectValue $properties 'musicvolume'
        if ($music) { $music.value = 0 }
        $project = Join-Path $privateFolder 'project.json'
        Write-DesktopJson $project $projectData
    }
}
$manifest = [ordered]@{
    schemaVersion=1; mode=$Mode; installRoot=$InstallDir; installedAt=(Get-Date -Format 'o')
    components=[ordered]@{rainmeter='Rainmeter\Rainmeter.exe';panel='SpectrumControl\AdCDesktop.exe';nexus=$nexusRelative;translucenttb=$tbbRelative}
    appearance=[ordered]@{hideDesktopIcons=($Mode -eq 'Full');taskbarAutoHide=($Mode -eq 'Full')}
    wallpaper=[ordered]@{enginePath=$engine;projectPath=$project;workshopId='3113287126';workshopUrl='https://steamcommunity.com/sharedfiles/filedetails/?id=3113287126'}
}
Write-DesktopJson (Join-Path $InstallDir 'desktop-style.json') $manifest
if (!$NoShortcuts -and !$Offline) {
    $shell = New-Object -ComObject WScript.Shell
    foreach ($item in @(@{Name='ADC 总控台';Target=(Join-Path $InstallDir 'SpectrumControl\AdCDesktop.exe');Arguments=('--root "' + $InstallDir + '"')},@{Name='启动AdC桌面美化';Target=(Join-Path $InstallDir 'Start-Desktop.cmd');Arguments=''},@{Name='完整桌面效果';Target=(Join-Path $InstallDir 'Switch-Desktop.cmd');Arguments='-Mode Full'},@{Name='标准桌面效果';Target=(Join-Path $InstallDir 'Switch-Desktop.cmd');Arguments='-Mode Standard'},@{Name='恢复原桌面';Target=(Join-Path $InstallDir 'Restore-Desktop.cmd');Arguments=''})) {
        $shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) ($item.Name + '.lnk')
        if (Test-Path -LiteralPath $shortcutPath) { $old = $shell.CreateShortcut($shortcutPath); if ($old.TargetPath -ine $item.Target) { $shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) ('AdCDesktop-' + $item.Name + '.lnk') } }
        if (Test-Path -LiteralPath $shortcutPath) { $old = $shell.CreateShortcut($shortcutPath); if ($old.TargetPath -ine $item.Target) { Write-Warning "Existing shortcut was left untouched: $shortcutPath"; continue } }
        $shortcut = $shell.CreateShortcut($shortcutPath); $shortcut.TargetPath=$item.Target; $shortcut.Arguments=$item.Arguments; $shortcut.WorkingDirectory=$InstallDir; $shortcut.IconLocation=(Join-Path $InstallDir 'SpectrumControl\AdCDesktop.exe') + ',0'; $shortcut.Save()
    }
}
if ($Launch -and !$Offline) {
    Write-Host "Installed in $InstallDir. Starting the configured desktop components..."
    $startScript = Join-Path $InstallDir 'Start-Desktop.ps1'
    if (!(Test-Path -LiteralPath $startScript -PathType Leaf)) { throw 'Start-Desktop.ps1 is missing; installation files are incomplete.' }
    & $startScript -InstallDir $InstallDir
}
else {
    Write-Host "Installed in $InstallDir. Nothing was started and the desktop was not changed."
    if ($Launch -and $Offline) { Write-Warning '-Launch is ignored in Offline mode.' }
}
if ($Mode -eq 'Full' -and (!$engine -or !$project)) { Write-Host 'Wallpaper is optional. Install/own Wallpaper Engine and subscribe to Workshop item 3113287126, then re-run with -WallpaperEnginePath and -WallpaperProject if detection is unavailable.' }
if ($nexusRelative) { Write-Host 'Nexus layout and Dock entries are configured from its Preferences; this installer does not import another user profile.' }
Write-Output ([pscustomobject]@{InstallDir=$InstallDir;Mode=$Mode;Manifest=(Join-Path $InstallDir 'desktop-style.json');Offline=[bool]$Offline;Launched=[bool]($Launch -and !$Offline)})
