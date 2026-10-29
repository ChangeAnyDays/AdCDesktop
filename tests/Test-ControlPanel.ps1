[CmdletBinding()]
param([switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$outputRoot = Join-Path $PSScriptRoot '.artifacts'
[void][IO.Directory]::CreateDirectory($outputRoot)
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $framework 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler is unavailable.' }
if (!$SkipBuild) { & (Join-Path $repositoryRoot 'scripts\Build-ControlPanel.ps1') }
$application = Join-Path $repositoryRoot 'artifacts\control-panel\AdCDesktop.exe'
if (!(Test-Path -LiteralPath $application)) { throw "Build output is missing: $application" }

$backendTest = Join-Path $outputRoot 'BackendTests.exe'
$backendArguments = @('/nologo','/target:exe','/codepage:65001',('/out:' + $backendTest),'/reference:System.Core.dll','/reference:System.Windows.Forms.dll')
$backendArguments += Join-Path $repositoryRoot 'src\ControlPanel\SpectrumBackend.cs'
$backendArguments += Join-Path $PSScriptRoot 'BackendTests.cs'
& $compiler $backendArguments
if ($LASTEXITCODE -ne 0) { throw 'Backend test compilation failed.' }
& $backendTest
if ($LASTEXITCODE -ne 0) { throw 'Backend fixture tests failed.' }

$fixtureRoot = Join-Path $outputRoot ('ui-fixture-' + [Guid]::NewGuid().ToString('N'))
$skinPath = Join-Path $fixtureRoot 'Rainmeter\Skins\RetrOcOlOr\Visualizer\Visualizer.ini'
$colorsPath = Join-Path $fixtureRoot 'Rainmeter\Skins\RetrOcOlOr\@Resources\Variables.inc'
$settingsPath = Join-Path $fixtureRoot 'Rainmeter\Rainmeter.ini'
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($skinPath))
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($colorsPath))
$utf8 = New-Object Text.UTF8Encoding($true)
[IO.File]::WriteAllText($skinPath,"[Rainmeter]`r`nUpdate=15`r`n[Variables]`r`nBarWidth=700`r`nBarHeight=60`r`n[MeasureAudio]`r`nSensitivity=45`r`nFFTDecay=0`r`n",$utf8)
[IO.File]::WriteAllText($colorsPath,"[Variables]`r`nColor1=255,255,255`r`nColor2=18,237,247`r`nColor3=245,11,185`r`n",$utf8)
[IO.File]::WriteAllText($settingsPath,"[Rainmeter]`r`nSkinPath=Skins\`r`nDisableDragging=1`r`n[Welfare\Clock]`r`nActive=1`r`nWindowY=76%`r`n[RetrOcOlOr\Visualizer]`r`nActive=1`r`nWindowX=610`r`nWindowY=879`r`nAlphaValue=178`r`nOnHover=2`r`nClickThrough=1`r`n",$utf8)

$uiTest = Join-Path $outputRoot 'FixtureUiChecks.exe'
$uiArguments = @('/nologo','/target:exe','/codepage:65001',('/out:' + $uiTest),'/reference:System.Core.dll','/reference:System.Xaml.dll')
foreach ($reference in @('PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll')) {
    $uiArguments += '/reference:' + (Join-Path $framework ('WPF\' + $reference))
}
$uiArguments += Join-Path $PSScriptRoot 'FixtureUiChecks.cs'
& $compiler $uiArguments
if ($LASTEXITCODE -ne 0) { throw 'UI test compilation failed.' }
$resultPath = Join-Path $outputRoot 'ui-regression-result.json'
& $uiTest $application $fixtureRoot $resultPath
if ($LASTEXITCODE -ne 0) { throw 'UI fixture regression failed.' }
$result = Get-Content -LiteralPath $resultPath -Raw -Encoding UTF8 | ConvertFrom-Json
if (!$result.success -or $result.checks -lt 12) { throw 'UI regression did not complete all expected checks.' }
$fixtureFiles = @($skinPath,$colorsPath,$settingsPath)
$beforePreview = @($fixtureFiles | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash })
$previewPath = Join-Path $outputRoot 'control-panel-fixture.png'
$previewArguments = @('--screenshot', ('"{0}"' -f $previewPath), '--root', ('"{0}"' -f $fixtureRoot))
$preview = Start-Process -FilePath $application -ArgumentList $previewArguments -PassThru -WindowStyle Hidden
if (!$preview.WaitForExit(20000)) { throw 'Isolated WPF preview timed out.' }
if ($preview.ExitCode -ne 0 -or !(Test-Path -LiteralPath $previewPath)) { throw 'Isolated WPF preview failed.' }
for ($i=0; $i -lt $fixtureFiles.Count; $i++) {
    if ((Get-FileHash -LiteralPath $fixtureFiles[$i] -Algorithm SHA256).Hash -ne $beforePreview[$i]) { throw 'Preview export unexpectedly changed a fixture configuration.' }
}
Write-Output 'PASS: UI fixtures preserved exact alpha/hover state, positioning, visibility and personal presets.'
