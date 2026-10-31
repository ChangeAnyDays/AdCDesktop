[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $PSScriptRoot ('.artifacts\install-' + [Guid]::NewGuid().ToString('N'))
$prepared = Join-Path $testRoot 'prepared'
$panel = Join-Path $testRoot 'panel'
$target = Join-Path $testRoot 'installed'
$utf8 = New-Object Text.UTF8Encoding($true)
$fixtureDirectories = @(
    (Join-Path $prepared 'Rainmeter\Skins\Welfare\Clock'),
    (Join-Path $prepared 'Rainmeter\Skins\RetrOcOlOr\Visualizer'),
    (Join-Path $prepared 'Rainmeter\Skins\RetrOcOlOr\@Resources'),
    $panel
)
foreach ($directory in $fixtureDirectories) { [void][IO.Directory]::CreateDirectory($directory) }
# Deliberately invalid executables prove that offline preparation never launches them.
[IO.File]::WriteAllText((Join-Path $prepared 'Rainmeter\Rainmeter.exe'),'fixture only - do not execute',$utf8)
[IO.File]::WriteAllText((Join-Path $panel 'AdCDesktop.exe'),'fixture only - do not execute',$utf8)
[IO.File]::WriteAllText((Join-Path $prepared 'Rainmeter\Skins\Welfare\Clock\Clock.ini'),"[Rainmeter]`r`nUpdate=1000`r`n",$utf8)
[IO.File]::WriteAllText((Join-Path $prepared 'Rainmeter\Skins\RetrOcOlOr\Visualizer\Visualizer.ini'),"[Rainmeter]`r`nUpdate=15`r`n[Variables]`r`nBarWidth=700`r`nBarHeight=60`r`n[MeasureAudio]`r`nSensitivity=45`r`nFFTDecay=0`r`n",$utf8)
[IO.File]::WriteAllText((Join-Path $prepared 'Rainmeter\Skins\RetrOcOlOr\@Resources\Variables.inc'),"[Variables]`r`nColor2=18,237,247`r`nColor3=245,11,185`r`n",$utf8)

function Get-DesktopShortcutNames {
    $desktop = [Environment]::GetFolderPath('Desktop')
    if ([string]::IsNullOrWhiteSpace($desktop) -or !(Test-Path -LiteralPath $desktop)) { return @() }
    return @(Get-ChildItem -LiteralPath $desktop -Filter '*.lnk' -File -Force | ForEach-Object Name | Sort-Object)
}
function Get-DesktopProcessIds {
    return @((Get-Process -Name Rainmeter,Nexus,TranslucentTB,wallpaper32,wallpaper64 -ErrorAction SilentlyContinue) | ForEach-Object Id | Sort-Object)
}
function Get-HideIconsValue {
    $settings = Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' -Name HideIcons -ErrorAction SilentlyContinue
    if ($null -eq $settings) { return '<unset>' }
    return [string]$settings.HideIcons
}
$shortcutsBefore = @(Get-DesktopShortcutNames)
$processIdsBefore = @(Get-DesktopProcessIds)
$iconsBefore = Get-HideIconsValue

& (Join-Path $repositoryRoot 'Install.ps1') -InstallDir $target -Mode ControlPanel -Offline -NoShortcuts -IncludeNexus:$false -PreparedComponentsPath $prepared -PreparedControlPanelPath $panel

foreach ($relative in @('Rainmeter\Rainmeter.exe','Rainmeter\Rainmeter.ini','SpectrumControl\AdCDesktop.exe','desktop-style.json','Start-Desktop.ps1','Restore-Desktop.ps1')) {
    if (!(Test-Path -LiteralPath (Join-Path $target $relative) -PathType Leaf)) { throw "Offline installation did not create $relative" }
}
$manifest = Get-Content -LiteralPath (Join-Path $target 'desktop-style.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($null -eq $manifest) { throw 'Installation manifest is not valid JSON.' }

# A full, offline fixture exercises Unicode/spaced paths without executable code or media.
$fullTarget = Join-Path $testRoot '完整桌面 Full Fixture'
$engineRoot = Join-Path $testRoot '假引擎 Engine Fixture'
$wallpaperSource = Join-Path $testRoot '素材 Samples\3113287126'
foreach ($directory in @((Join-Path $prepared 'Nexus'),(Join-Path $prepared 'TranslucentTB'),$engineRoot,$wallpaperSource)) {
    [void][IO.Directory]::CreateDirectory($directory)
}
[IO.File]::WriteAllText((Join-Path $prepared 'Nexus\Nexus.exe'),'fixture only - do not execute',$utf8)
[IO.File]::WriteAllText((Join-Path $prepared 'TranslucentTB\TranslucentTB.exe'),'fixture only - do not execute',$utf8)
$enginePath = Join-Path $engineRoot 'wallpaper32.exe'
[IO.File]::WriteAllText($enginePath,'fixture only - do not execute',$utf8)
$sourceProject = Join-Path $wallpaperSource 'project.json'
$project = [ordered]@{
    title='Dome 测试素材'; workshopid='3113287126'; file='scene.json'
    general=@{properties=@{
        clock=@{value=$true}; date=@{value=$true}; day=@{value=$true}
        mediavisualizer=@{value=$true}; audiovisualizer=@{value=$true}
        musicvolume=@{value=0.6}; keepUnrelatedProperty=@{value='保留此值'}
    }}
}
[IO.File]::WriteAllText($sourceProject,($project | ConvertTo-Json -Depth 12),$utf8)
[IO.File]::WriteAllText((Join-Path $wallpaperSource 'scene.pkg'),'fixture data - not a real wallpaper package',$utf8)
[IO.File]::WriteAllText((Join-Path $wallpaperSource 'preview.gif'),'fixture preview - not a real image',$utf8)
$sourceHashes = @{}
foreach ($file in @('project.json','scene.pkg','preview.gif')) {
    $sourceHashes[$file] = (Get-FileHash -LiteralPath (Join-Path $wallpaperSource $file) -Algorithm SHA256).Hash
}
$includeTaskbar = [Environment]::OSVersion.Version.Build -ge 22000
& (Join-Path $repositoryRoot 'Install.ps1') -InstallDir $fullTarget -Mode Full -Offline -NoShortcuts -PreparedComponentsPath $prepared -PreparedControlPanelPath $panel -IncludeNexus -IncludeTaskbar:$includeTaskbar -WallpaperEnginePath $enginePath -WallpaperProject $sourceProject

$fullManifestPath = Join-Path $fullTarget 'desktop-style.json'
$fullManifest = Get-Content -LiteralPath $fullManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$privateProject = Join-Path $fullTarget 'Wallpaper\Dome\project.json'
if ($fullManifest.mode -cne 'Full' -or $fullManifest.installRoot -ine [IO.Path]::GetFullPath($fullTarget)) { throw 'Full install did not preserve the Unicode/spaced installation path.' }
if (!$fullManifest.appearance.hideDesktopIcons -or !$fullManifest.appearance.taskbarAutoHide) { throw 'Full manifest did not record its requested appearance.' }
if ($fullManifest.wallpaper.enginePath -ine [IO.Path]::GetFullPath($enginePath) -or $fullManifest.wallpaper.projectPath -ine [IO.Path]::GetFullPath($privateProject)) { throw 'Full manifest did not point to the explicit engine and private wallpaper copy.' }
if ($fullManifest.components.nexus -cne 'Nexus\Nexus.exe') { throw 'Full manifest lost its optional Nexus component.' }
if ($includeTaskbar -and $fullManifest.components.translucenttb -cne 'TranslucentTB\TranslucentTB.exe') { throw 'Full manifest lost its optional taskbar component.' }
$privateData = Get-Content -LiteralPath $privateProject -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($propertyName in @('clock','date','day','mediavisualizer','audiovisualizer')) {
    if ($privateData.general.properties.$propertyName.value -ne $false) { throw "The private wallpaper copy did not disable duplicate $propertyName." }
}
if ($privateData.general.properties.musicvolume.value -ne 0) { throw 'The private wallpaper copy did not mute its bundled music.' }
if ($privateData.general.properties.keepUnrelatedProperty.value -cne '保留此值') { throw 'Private wallpaper preparation changed an unrelated property.' }
foreach ($file in @('project.json','scene.pkg','preview.gif')) {
    if ((Get-FileHash -LiteralPath (Join-Path $wallpaperSource $file) -Algorithm SHA256).Hash -ne $sourceHashes[$file]) { throw 'Private-copy preparation modified the original wallpaper source.' }
    if ($file -ne 'project.json' -and (Get-FileHash -LiteralPath (Join-Path $fullTarget ('Wallpaper\Dome\' + $file)) -Algorithm SHA256).Hash -ne $sourceHashes[$file]) { throw 'Private-copy preparation altered the copied wallpaper data.' }
}

# Reinstallation must preserve current user choices, custom presets and private edits.
$userSettings = Join-Path $fullTarget 'Rainmeter\Rainmeter.ini'
$userSkin = Join-Path $fullTarget 'Rainmeter\Skins\RetrOcOlOr\Visualizer\Visualizer.ini'
$userColors = Join-Path $fullTarget 'Rainmeter\Skins\RetrOcOlOr\@Resources\Variables.inc'
$text = Get-Content -LiteralPath $userSettings -Raw -Encoding UTF8
$text = $text.Replace('AlphaValue=255','AlphaValue=178') + "`r`n[IndependentUserSkin]`r`nActive=1`r`nWindowX=222`r`n"
[IO.File]::WriteAllText($userSettings,$text,$utf8)
$text = (Get-Content -LiteralPath $userSkin -Raw -Encoding UTF8) -replace '(?m)^BarWidth=\d+','BarWidth=913'
[IO.File]::WriteAllText($userSkin,$text,$utf8)
$text = (Get-Content -LiteralPath $userColors -Raw -Encoding UTF8).Replace('Color2=18,237,247','Color2=20,30,40')
[IO.File]::WriteAllText($userColors,$text,$utf8)
$privateData | Add-Member -NotePropertyName userFixture -NotePropertyValue 'keep private edits' -Force
[IO.File]::WriteAllText($privateProject,($privateData | ConvertTo-Json -Depth 12),$utf8)
$personalPreset = Join-Path $fullTarget 'SpectrumControl\my-preset.json'
[IO.File]::WriteAllText($personalPreset,'{"name":"personal fixture preset"}',$utf8)
$preservedFiles = @($userSettings,$userSkin,$userColors,$privateProject,$personalPreset)
$preservedHashes = @{}
foreach ($file in $preservedFiles) { $preservedHashes[$file] = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash }
& (Join-Path $repositoryRoot 'Install.ps1') -InstallDir $fullTarget -Offline -NoShortcuts -PreparedComponentsPath $prepared -PreparedControlPanelPath $panel
foreach ($file in $preservedFiles) {
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $preservedHashes[$file]) { throw ('Reinstallation overwrote a user setting: ' + [IO.Path]::GetFileName($file)) }
}
$reinstalledManifest = Get-Content -LiteralPath $fullManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($reinstalledManifest.mode -cne 'Full' -or $reinstalledManifest.components.nexus -cne $fullManifest.components.nexus -or $reinstalledManifest.components.translucenttb -cne $fullManifest.components.translucenttb -or $reinstalledManifest.wallpaper.projectPath -ine $privateProject) { throw 'Reinstallation dropped the existing Full profile or optional components.' }

# Rejecting an in-place downgrade must leave the existing installation untouched.
$manifestHashBeforeDowngrade = (Get-FileHash -LiteralPath $fullManifestPath -Algorithm SHA256).Hash
$downgradeRejected = $false
try {
    & (Join-Path $repositoryRoot 'Install.ps1') -InstallDir $fullTarget -Mode ControlPanel -Offline -NoShortcuts -IncludeNexus:$false -PreparedComponentsPath $prepared -PreparedControlPanelPath $panel
} catch {
    if ($_.Exception.Message -notmatch 'Full installation cannot be changed to ControlPanel in place') { throw }
    $downgradeRejected = $true
}
if (!$downgradeRejected) { throw 'An in-place Full to ControlPanel downgrade was allowed.' }
if ((Get-FileHash -LiteralPath $fullManifestPath -Algorithm SHA256).Hash -ne $manifestHashBeforeDowngrade) { throw 'Rejected downgrade changed the installation manifest bytes.' }
foreach ($file in $preservedFiles) {
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $preservedHashes[$file]) { throw 'Rejected downgrade changed an existing user configuration.' }
}

if ((@(Get-DesktopShortcutNames) -join '|') -cne ($shortcutsBefore -join '|')) { throw 'Offline installation changed desktop shortcuts.' }
if ((@(Get-DesktopProcessIds) -join '|') -cne ($processIdsBefore -join '|')) { throw 'Offline installation started or stopped a desktop process.' }
if ((Get-HideIconsValue) -cne $iconsBefore) { throw 'Offline installation changed desktop icon visibility.' }
if (Test-Path -LiteralPath (Join-Path $target 'Backups')) { throw 'Install captured a real desktop backup before any Start action.' }
if (Test-Path -LiteralPath (Join-Path $fullTarget 'Backups')) { throw 'Full install captured a real desktop backup before any Start action.' }
Write-Output 'PASS: offline ControlPanel and Full fixtures, Unicode/spaced paths, private wallpaper preparation and non-destructive reinstallation; no applications or desktop settings changed.'
