[CmdletBinding()]
param([string]$OutputDir)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDir)) { $OutputDir = Join-Path $repoRoot 'artifacts\control-panel' }
$OutputDir = [IO.Path]::GetFullPath($OutputDir)
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path -LiteralPath (Join-Path $frameworkRoot 'csc.exe'))) {
    $frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $frameworkRoot 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework 4.8 C# compiler is required. See docs/DEVELOPMENT.md.' }
[void](New-Item -ItemType Directory -Path $OutputDir -Force)
$wpf = Join-Path $frameworkRoot 'WPF'
$source = Join-Path $repoRoot 'src\ControlPanel'
$exe = Join-Path $OutputDir 'AdCDesktop.exe'
$arguments = @('/nologo','/target:winexe','/optimize+','/platform:anycpu','/codepage:65001',('/out:' + $exe),('/win32manifest:' + (Join-Path $source 'app.manifest')),('/resource:' + (Join-Path $source 'SpectrumPanel.xaml') + ',SpectrumControl.Panel.xaml'),('/resource:' + (Join-Path $repoRoot 'assets\logo.png') + ',SpectrumControl.Logo.png'))
$icon = Join-Path $repoRoot 'assets\app.ico'
if (Test-Path -LiteralPath $icon) { $arguments += '/win32icon:' + $icon }
foreach ($name in @('System.dll','System.Core.dll','System.Xml.dll','System.Xaml.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Runtime.Serialization.dll')) {
    $arguments += '/reference:' + (Join-Path $frameworkRoot $name)
}
foreach ($name in @('PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll')) {
    $arguments += '/reference:' + (Join-Path $wpf $name)
}
$arguments += (Join-Path $source 'SpectrumPanel.cs')
$arguments += (Join-Path $source 'SpectrumBackend.cs')
$arguments += (Join-Path $source 'NexusBackend.cs')
$arguments += (Join-Path $source 'SpectrumPreview.cs')
$arguments += (Join-Path $source 'StartupBackend.cs')
& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw 'Control panel compilation failed.' }
$runtimeConfig = '<?xml version="1.0" encoding="utf-8"?><configuration><startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8" /></startup></configuration>'
[IO.File]::WriteAllText(($exe + '.config'), $runtimeConfig, (New-Object Text.UTF8Encoding($false)))
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\QuickStart.html') -Destination (Join-Path $OutputDir 'QuickStart.html') -Force
Write-Output $exe
