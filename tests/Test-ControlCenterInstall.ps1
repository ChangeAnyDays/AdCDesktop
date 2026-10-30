[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$installer = Join-Path $repositoryRoot 'scripts\Install-ControlCenter.ps1'
$buildPayload = Join-Path $repositoryRoot 'artifacts\control-panel'
$files = @('AdCDesktop.exe','AdCDesktop.exe.config','QuickStart.html')
foreach ($file in $files) {
    if (!(Test-Path -LiteralPath (Join-Path $buildPayload $file) -PathType Leaf)) { throw 'Build the control panel before running the update installer fixtures.' }
}
# Short, isolated fixture paths also cover spaces, Unicode, and wildcard literals.
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('adc-update-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
$payload = Join-Path $fixture ('payload [' + [char]0x6D4B + [char]0x8BD5 + ']')
$target = Join-Path $fixture ('installed ' + [char]0x4E2D + [char]0x6587)
$utf8 = New-Object Text.UTF8Encoding($false)
[void][IO.Directory]::CreateDirectory($payload)
foreach ($file in $files) { Copy-Item -LiteralPath (Join-Path $buildPayload $file) -Destination $payload }

function Write-FixtureFile([string]$Path,[string]$Text) {
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path))
    [IO.File]::WriteAllText($Path,$Text,$utf8)
}
function Get-Hash([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
function Assert-Hash([string]$Path,[string]$Expected,[string]$Message) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf) -or (Get-Hash $Path) -ne $Expected) { throw $Message }
}
function New-Fixture([string]$Root) {
    foreach ($relative in @('Rainmeter\Rainmeter.exe','Rainmeter\Rainmeter.ini','Rainmeter\Skins\RetrOcOlOr\Visualizer\Visualizer.ini','Rainmeter\Skins\RetrOcOlOr\@Resources\Variables.inc','SpectrumControl\my-preset.json','SpectrumControl\legacy-console.exe','SpectrumControl\nexus-path.txt','Nexus\user-layout.ini')) {
        Write-FixtureFile (Join-Path $Root $relative) ('Preserve fixture bytes: ' + $relative)
    }
}
function Get-ShortcutHashes {
    $result = @{}
    $desktop = [Environment]::GetFolderPath('Desktop')
    if ($desktop -and (Test-Path -LiteralPath $desktop -PathType Container)) {
        foreach ($file in @(Get-ChildItem -LiteralPath $desktop -Filter '*.lnk' -File -Force)) { $result[$file.FullName] = Get-Hash $file.FullName }
    }
    return $result
}
function Assert-Rejected([scriptblock]$Action,[string]$Expected) {
    $rejected = $false
    try { & $Action | Out-Null }
    catch {
        if ($_.Exception.Message -notmatch $Expected) { throw }
        $rejected = $true
    }
    if (!$rejected) { throw ('Expected update rejection: ' + $Expected) }
}

$shortcutsBefore = Get-ShortcutHashes
New-Fixture $target
$preserve = @{}
foreach ($file in @(Get-ChildItem -LiteralPath $target -File -Recurse)) { $preserve[$file.FullName] = Get-Hash $file.FullName }
$original = @{}
foreach ($file in $files) {
    $path = Join-Path $target ('SpectrumControl\' + $file)
    Write-FixtureFile $path ('old version: ' + $file)
    $original[$file] = Get-Hash $path
}

$first = & $installer -InstallDir ($target + '\') -PayloadPath $payload -NoShortcut
foreach ($file in $files) {
    Assert-Hash (Join-Path $target ('SpectrumControl\' + $file)) (Get-Hash (Join-Path $payload $file)) ('Update did not deploy ' + $file)
    Assert-Hash (Join-Path $first.Backup $file) $original[$file] ('Backup lost previous bytes of ' + $file)
}
if ($first.Shortcut) { throw '-NoShortcut reported an unexpected desktop entry.' }
$second = & $installer -InstallDir $target -PayloadPath $payload -NoShortcut
if ($first.Backup -eq $second.Backup) { throw 'Repeated updates reused the same backup.' }
foreach ($file in $files) {
    Assert-Hash (Join-Path $second.Backup $file) (Get-Hash (Join-Path $payload $file)) ('Repeated update backup differs: ' + $file)
    Assert-Hash (Join-Path $first.Backup $file) $original[$file] ('Repeated update changed an earlier backup: ' + $file)
}
foreach ($path in $preserve.Keys) { Assert-Hash $path $preserve[$path] 'Update changed a configuration, preset, layout, or legacy file.' }

$beforeRejected = @{}
foreach ($file in $files) { $beforeRejected[$file] = Get-Hash (Join-Path $target ('SpectrumControl\' + $file)) }
Assert-Rejected { & $installer -InstallDir $target -PayloadPath (Join-Path $target 'SpectrumControl') -NoShortcut } 'must be different'
$incompletePayload = Join-Path $fixture 'incomplete'
[void][IO.Directory]::CreateDirectory($incompletePayload)
Copy-Item -LiteralPath (Join-Path $payload 'AdCDesktop.exe') -Destination $incompletePayload
Assert-Rejected { & $installer -InstallDir $target -PayloadPath $incompletePayload -NoShortcut } 'Missing payload'
foreach ($file in $files) { Assert-Hash (Join-Path $target ('SpectrumControl\' + $file)) $beforeRejected[$file] 'Rejected update changed a deployed file.' }

$blockedTarget = Join-Path $fixture 'directory conflict'
New-Fixture $blockedTarget
$blockedExe = Join-Path $blockedTarget 'SpectrumControl\AdCDesktop.exe'
Write-FixtureFile $blockedExe 'preserve old executable'
$blockedHash = Get-Hash $blockedExe
$conflict = Join-Path $blockedTarget 'SpectrumControl\QuickStart.html'
Write-FixtureFile (Join-Path $conflict 'personal.txt') 'never overwrite or remove this directory'
Assert-Rejected { & $installer -InstallDir $blockedTarget -PayloadPath $payload -NoShortcut } 'file is a directory'
Assert-Hash $blockedExe $blockedHash 'Directory conflict was detected after changing a file.'
if (!(Test-Path -LiteralPath (Join-Path $conflict 'personal.txt'))) { throw 'Directory conflict damaged a user directory.' }
if (Test-Path -LiteralPath (Join-Path $conflict 'QuickStart.html')) { throw 'Payload was copied into a user directory.' }

# A read-shared lock allows backups but rejects replacement of the second file.
# The first file has already changed; the transaction must restore it.
foreach ($freshExe in @($false,$true)) {
    $rollbackRoot = Join-Path $fixture ('rollback-' + $freshExe)
    New-Fixture $rollbackRoot
    $rollbackExe = Join-Path $rollbackRoot 'SpectrumControl\AdCDesktop.exe'
    if (!$freshExe) { Write-FixtureFile $rollbackExe 'old executable to restore' }
    $rollbackConfig = Join-Path $rollbackRoot 'SpectrumControl\AdCDesktop.exe.config'
    Write-FixtureFile $rollbackConfig 'old locked config'
    $configHash = Get-Hash $rollbackConfig
    if (!$freshExe) { $exeHash = Get-Hash $rollbackExe }
    $lock = New-Object IO.FileStream($rollbackConfig,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    try { Assert-Rejected { & $installer -InstallDir $rollbackRoot -PayloadPath $payload -NoShortcut } 'rolled back' }
    finally { $lock.Dispose() }
    Assert-Hash $rollbackConfig $configHash 'Rollback changed an untouched locked config.'
    if ($freshExe) {
        if (Test-Path -LiteralPath $rollbackExe) { throw 'Rollback retained an executable absent before the update.' }
    }
    else { Assert-Hash $rollbackExe $exeHash 'Rollback did not restore the previous executable.' }
    if (Test-Path -LiteralPath (Join-Path $rollbackRoot 'SpectrumControl\QuickStart.html')) { throw 'Failed update wrote files after the locked target.' }
}

$shortcutsAfter = Get-ShortcutHashes
if ($shortcutsBefore.Count -ne $shortcutsAfter.Count) { throw '-NoShortcut changed the desktop entry count.' }
foreach ($path in $shortcutsBefore.Keys) {
    if (!$shortcutsAfter.ContainsKey($path) -or $shortcutsAfter[$path] -ne $shortcutsBefore[$path]) { throw '-NoShortcut changed a desktop entry.' }
}
foreach ($file in $files) { Assert-Hash (Join-Path $payload $file) (Get-Hash (Join-Path $buildPayload $file)) 'Installer modified its source payload.' }
Write-Output ('PASS: real build deployed to isolated fixtures; repeat backups, preserved user files, Unicode/spaces/literal brackets, preflight rejection and locked-file rollback. Fixture: ' + $fixture)
