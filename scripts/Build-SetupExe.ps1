[CmdletBinding()]
param(
    [string]$SetupZip,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($SetupZip)) {
    $SetupZip = Get-ChildItem (Join-Path $repoRoot 'artifacts\packages') -Filter 'ADC-Desktop-Setup-*.zip' |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (!(Test-Path -LiteralPath $SetupZip -PathType Leaf)) { throw "Setup zip not found: $SetupZip" }
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot ('artifacts\packages\' + [IO.Path]::GetFileNameWithoutExtension($SetupZip) + '.exe')
}
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path -LiteralPath (Join-Path $frameworkRoot 'csc.exe'))) {
    $frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $frameworkRoot 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler is unavailable.' }
[void](New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) -Force)

$arguments = @(
    '/nologo','/target:winexe','/optimize+','/platform:anycpu','/codepage:65001',
    ('/out:' + $OutputPath),
    ('/win32icon:' + (Join-Path $repoRoot 'assets\app.ico')),
    ('/resource:' + $SetupZip + ',ADC.Setup.zip'),
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Windows.Forms.dll',
    ('/reference:' + (Join-Path $frameworkRoot 'System.IO.Compression.dll')),
    ('/reference:' + (Join-Path $frameworkRoot 'System.IO.Compression.FileSystem.dll')),
    (Join-Path $PSScriptRoot 'SetupLauncher.cs')
)
& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw 'Setup launcher compilation failed.' }
Write-Output $OutputPath
