[CmdletBinding()]
param(
    [string]$InstallDir,
    [string]$PayloadPath,
    [switch]$NoShortcut,
    [switch]$Launch
)

$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Windows 11 and .NET Framework 4.8 are required.' }
$payloadFiles = @('AdCDesktop.exe','AdCDesktop.exe.config','QuickStart.html')
if (!$PayloadPath) {
    $PayloadPath = Join-Path $PSScriptRoot 'control-panel'
    if (!(Test-Path -LiteralPath $PayloadPath)) {
        $PayloadPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\control-panel'
    }
}
$PayloadPath = [IO.Path]::GetFullPath($PayloadPath)
foreach ($file in $payloadFiles) {
    if (!(Test-Path -LiteralPath (Join-Path $PayloadPath $file) -PathType Leaf)) { throw "Missing payload: $file" }
}
if (!$InstallDir) {
    Add-Type -AssemblyName System.Windows.Forms
    $picker = New-Object System.Windows.Forms.FolderBrowserDialog
    try {
        $picker.Description = 'Select your existing AdCDesktop directory containing Rainmeter.'
        $picker.ShowNewFolderButton = $false
        if ($picker.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { return }
        $InstallDir = $picker.SelectedPath
    }
    finally { $picker.Dispose() }
}
$InstallDir = [IO.Path]::GetFullPath($InstallDir)
$rootArgument = $InstallDir
# A trailing backslash immediately before a quote is special to Windows argv.
if ($rootArgument.EndsWith('\')) { $rootArgument += '.' }
foreach ($file in @('Rainmeter\Rainmeter.exe','Rainmeter\Rainmeter.ini','Rainmeter\Skins\RetrOcOlOr\Visualizer\Visualizer.ini','Rainmeter\Skins\RetrOcOlOr\@Resources\Variables.inc')) {
    if (!(Test-Path -LiteralPath (Join-Path $InstallDir $file) -PathType Leaf)) { throw "Select the existing installation root. Missing: $file" }
}
$destination = Join-Path $InstallDir 'SpectrumControl'
$application = Join-Path $destination 'AdCDesktop.exe'
if ([string]::Equals($PayloadPath.TrimEnd('\'),$destination.TrimEnd('\'),[StringComparison]::OrdinalIgnoreCase)) {
    throw 'Payload and installation directories must be different. Extract the update ZIP to a separate directory.'
}
if ((Test-Path -LiteralPath $destination) -and !(Test-Path -LiteralPath $destination -PathType Container)) {
    throw 'SpectrumControl must be a directory.'
}
foreach ($file in $payloadFiles) {
    $target = Join-Path $destination $file
    if ((Test-Path -LiteralPath $target) -and !(Test-Path -LiteralPath $target -PathType Leaf)) {
        throw "An installation file is a directory; no files were updated: $file"
    }
}
foreach ($process in @(Get-Process AdCDesktop -ErrorAction SilentlyContinue)) {
    try {
        if ([string]::Equals($process.Path,$application,[StringComparison]::OrdinalIgnoreCase)) {
            throw 'Close ADC Control Center before installing the update.'
        }
    }
    finally { $process.Dispose() }
}
[void][IO.Directory]::CreateDirectory($destination)
$backup = Join-Path $destination ('Backups\ControlCenter-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
[void][IO.Directory]::CreateDirectory($backup)
$entries = @()
$changed = @()
$shortcutPath = $null
$shortcutExisted = $false
$shortcutChanged = $false
$shell = $null
$shortcut = $null
try {
    # Finish and verify all backups before replacing the first application file.
    foreach ($file in $payloadFiles) {
        $target = Join-Path $destination $file
        $existed = Test-Path -LiteralPath $target -PathType Leaf
        $oldHash = $null
        if ($existed) {
            $oldHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
            $backupFile = Join-Path $backup $file
            Copy-Item -LiteralPath $target -Destination $backupFile
            if ((Get-FileHash -LiteralPath $backupFile -Algorithm SHA256).Hash -ne $oldHash) { throw "Backup verification failed: $file" }
        }
        $entries += [pscustomobject]@{Path=$target;Name=$file;Existed=$existed;Hash=$oldHash}
    }
    if (!$NoShortcut) {
        $desktop = [Environment]::GetFolderPath('Desktop')
        if ([string]::IsNullOrWhiteSpace($desktop) -or !(Test-Path -LiteralPath $desktop -PathType Container)) { throw 'The current-user desktop directory is unavailable. Use -NoShortcut to install without a desktop entry.' }
        $shortcutPath = Join-Path $desktop ('ADC ' + [char]0x603B + [char]0x63A7 + [char]0x53F0 + '.lnk')
        if ((Test-Path -LiteralPath $shortcutPath) -and !(Test-Path -LiteralPath $shortcutPath -PathType Leaf)) { throw 'The desktop shortcut path is a directory.' }
        $shortcutExisted = Test-Path -LiteralPath $shortcutPath -PathType Leaf
        if ($shortcutExisted) {
            Copy-Item -LiteralPath $shortcutPath -Destination (Join-Path $backup 'previous-shortcut.lnk')
            if ((Get-FileHash -LiteralPath $shortcutPath -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath (Join-Path $backup 'previous-shortcut.lnk') -Algorithm SHA256).Hash) { throw 'Shortcut backup verification failed.' }
        }
        $shell = New-Object -ComObject WScript.Shell
    }
    foreach ($entry in $entries) {
        $file = $entry.Name
        $target = $entry.Path
        $changed += $entry
        Copy-Item -LiteralPath (Join-Path $PayloadPath $file) -Destination $target -Force
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath (Join-Path $PayloadPath $file) -Algorithm SHA256).Hash) { throw "Installed file verification failed: $file" }
    }
    if (!$NoShortcut) {
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = $application
        $shortcut.Arguments = '--root "' + $rootArgument + '"'
        $shortcut.WorkingDirectory = $destination
        $shortcut.IconLocation = $application + ',0'
        $shortcutChanged = $true
        $shortcut.Save()
    }
}
catch {
    $installError = $_.Exception.Message
    $rollbackErrors = @()
    if ($shortcutChanged) {
        try {
            if ($shortcutExisted) { Copy-Item -LiteralPath (Join-Path $backup 'previous-shortcut.lnk') -Destination $shortcutPath -Force }
            elseif (Test-Path -LiteralPath $shortcutPath -PathType Leaf) { Remove-Item -LiteralPath $shortcutPath -Force }
        }
        catch { $rollbackErrors += 'desktop shortcut: ' + $_.Exception.Message }
    }
    foreach ($item in $changed) {
        try {
            if ($item.Existed) {
                # A locked target can reject a copy without changing its bytes.
                if (!(Test-Path -LiteralPath $item.Path -PathType Leaf) -or (Get-FileHash -LiteralPath $item.Path -Algorithm SHA256).Hash -ne $item.Hash) {
                    Copy-Item -LiteralPath (Join-Path $backup $item.Name) -Destination $item.Path -Force
                    if ((Get-FileHash -LiteralPath $item.Path -Algorithm SHA256).Hash -ne $item.Hash) { throw 'Restored bytes differ from the backup.' }
                }
            }
            elseif (Test-Path -LiteralPath $item.Path -PathType Leaf) { Remove-Item -LiteralPath $item.Path -Force }
        }
        catch { $rollbackErrors += $item.Name + ': ' + $_.Exception.Message }
    }
    if ($rollbackErrors.Count -gt 0) { throw ($installError + "`nRollback needs manual recovery from " + $backup + ":`n" + ($rollbackErrors -join "`n")) }
    throw ($installError + "`nApplication files were rolled back. Backup: " + $backup)
}
finally {
    if ($null -ne $shortcut) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) }
    if ($null -ne $shell) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) }
}
if ($Launch) { Start-Process -FilePath $application -ArgumentList ('--root "' + $rootArgument + '"') -WindowStyle Normal }
[pscustomobject]@{Installed=$application;Version=[Diagnostics.FileVersionInfo]::GetVersionInfo($application).FileVersion;Backup=$backup;Shortcut=$shortcutPath}
