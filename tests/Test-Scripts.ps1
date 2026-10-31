[CmdletBinding()]
param([switch]$SkipPlan)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$scriptFiles = @(Get-ChildItem -LiteralPath $repositoryRoot -Recurse -File -Force | Where-Object {
    $_.Extension -in @('.ps1','.psm1') -and $_.FullName.Substring($repositoryRoot.Length+1) -notmatch '(?:^|[\\/])(?:\.git|\.artifacts|artifacts|bin|obj)[\\/]'
})
if ($scriptFiles.Count -eq 0) { throw 'No PowerShell source files were found.' }
$errors = New-Object 'System.Collections.Generic.List[string]'
foreach ($scriptFile in $scriptFiles) {
    $tokens = $null; $parseErrors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($scriptFile.FullName,[ref]$tokens,[ref]$parseErrors)
    foreach ($parseError in $parseErrors) { $errors.Add($scriptFile.Name + ': ' + $parseError.Message) }
    $bytes = [IO.File]::ReadAllBytes($scriptFile.FullName)
    $text = [Text.Encoding]::UTF8.GetString($bytes)
    if ($text -match '[\u3400-\u9fff]' -and !($bytes.Length -ge 3 -and $bytes[0] -eq 0xef -and $bytes[1] -eq 0xbb -and $bytes[2] -eq 0xbf)) {
        $errors.Add($scriptFile.Name + ': Chinese PowerShell source needs UTF-8 BOM for Windows PowerShell 5.1.')
    }
}
if ($errors.Count -gt 0) { throw ($errors -join [Environment]::NewLine) }

if (!$SkipPlan) {
    $installScript = Join-Path $repositoryRoot 'Install.ps1'
    if (!(Test-Path -LiteralPath $installScript)) { throw 'Install.ps1 is missing.' }
    $planRoot = Join-Path $PSScriptRoot ('.artifacts\whatif-' + [Guid]::NewGuid().ToString('N'))
    & $installScript -InstallDir $planRoot -Mode ControlPanel -WhatIf
    if (Test-Path -LiteralPath $planRoot) { throw 'WhatIf unexpectedly created the installation directory.' }
}

# Regression: Windows PowerShell 5.1 leaves $PSScriptRoot empty inside param() defaults.
# Both entrypoints must resolve their own folder when -InstallDir is omitted.
$startupFixture = Join-Path $PSScriptRoot ('.artifacts\startup-' + [Guid]::NewGuid().ToString('N') + ' [space]')
try {
    [void][IO.Directory]::CreateDirectory((Join-Path $startupFixture 'scripts'))
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'Start-Desktop.ps1') -Destination $startupFixture
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'Restore-Desktop.ps1') -Destination $startupFixture
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'scripts\AdCDesktop.Common.psm1') -Destination (Join-Path $startupFixture 'scripts')
    [IO.File]::WriteAllText((Join-Path $startupFixture 'desktop-style.json'),'{"schemaVersion":1,"components":{},"appearance":{},"wallpaper":{}}',(New-Object Text.UTF8Encoding($false)))
    $startError = $null
    try { & (Join-Path $startupFixture 'Start-Desktop.ps1') -WhatIf *> $null }
    catch { $startError = $_.Exception.Message }
    if ($startError) { throw ('Start-Desktop.ps1 did not fall back to its own folder: ' + $startError) }
    $restoreError = $null
    try { & (Join-Path $startupFixture 'Restore-Desktop.ps1') -WhatIf *> $null }
    catch { $restoreError = $_.Exception.Message }
    if ($restoreError) { throw ('Restore-Desktop.ps1 did not fall back to its own folder: ' + $restoreError) }
}
finally {
    if (Test-Path -LiteralPath $startupFixture) { Remove-Item -LiteralPath $startupFixture -Recurse -Force }
}
Write-Output 'PASS: Start/Restore resolve their own folder when -InstallDir is omitted.'

if ($SkipPlan) { Write-Output ("PASS: {0} PowerShell files parsed (syntax-only mode)." -f $scriptFiles.Count) }
else { Write-Output ("PASS: {0} PowerShell files parsed; installation plan did not change the machine." -f $scriptFiles.Count) }
