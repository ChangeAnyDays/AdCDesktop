[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('adc-skin-' + [Guid]::NewGuid().ToString('N').Substring(0,12))
$utf8 = New-Object Text.UTF8Encoding($false)
Add-Type -AssemblyName System.IO.Compression.FileSystem | Out-Null

function New-ZipEntry([IO.Compression.ZipArchive]$Archive, [string]$Name, [string]$Text) {
    $entry = $Archive.CreateEntry($Name)
    $stream = $entry.Open()
    try {
        $writer = New-Object IO.StreamWriter($stream, $utf8)
        try { $writer.Write($Text) } finally { $writer.Dispose() }
    } finally { $stream.Dispose() }
}

try {
    [void][IO.Directory]::CreateDirectory($testRoot)
    $inner = Join-Path $testRoot 'RetrOcOlOr.rmskin'
    $innerZip = [IO.Compression.ZipFile]::Open($inner, 'Create')
    try {
        New-ZipEntry $innerZip 'Skins/RetrOcOlOr/Visualizer/Visualizer.ini' "[Rainmeter]`r`nUpdate=15`r`n[Variables]`r`nBarWidth=700`r`nBarHeight=60`r`n"
        New-ZipEntry $innerZip 'Skins/RetrOcOlOr/@Resources/Variables.inc' "[Variables]`r`nColor2=18,237,247`r`nColor3=245,11,185`r`n"
        New-ZipEntry $innerZip 'Skins/RetrOcOlOr/NotAllowed.exe' 'fixture only - must not be installed'
        New-ZipEntry $innerZip 'Skins/OtherSkin/Other.ini' "[Rainmeter]`r`nUpdate=1000`r`n"
    } finally { $innerZip.Dispose() }

    $outer = Join-Path $testRoot 'RetrOcOlOr_by_hiphopium.zip'
    $outerZip = [IO.Compression.ZipFile]::Open($outer, 'Create')
    try {
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($outerZip, $inner, 'RetrOcOlOr.rmskin')
        New-ZipEntry $outerZip 'README.txt' 'fixture outer archive'
    } finally { $outerZip.Dispose() }

    $cache = Join-Path $testRoot 'cache'
    $skinRoot = Join-Path $testRoot 'Rainmeter\Skins'
    $dependency = [pscustomobject]@{
        innerArchive = 'RetrOcOlOr.rmskin'
        innerArchiveSha256 = (Get-FileHash -LiteralPath $inner -Algorithm SHA256).Hash
    }

    Import-Module (Join-Path $repositoryRoot 'scripts\AdCDesktop.Common.psm1') -Force
    Install-SkinData -ArchivePath $outer -Dependency $dependency -SkinName 'RetrOcOlOr' -SkinRoot $skinRoot -CacheDir $cache

    $visualizer = Join-Path $skinRoot 'RetrOcOlOr\Visualizer\Visualizer.ini'
    $variables = Join-Path $skinRoot 'RetrOcOlOr\@Resources\Variables.inc'
    if (!(Test-Path -LiteralPath $visualizer -PathType Leaf)) { throw 'Skin installation did not extract Visualizer.ini.' }
    if (!(Test-Path -LiteralPath $variables -PathType Leaf)) { throw 'Skin installation did not extract Variables.inc.' }
    if (Test-Path -LiteralPath (Join-Path $skinRoot 'RetrOcOlOr\NotAllowed.exe')) { throw 'Skin installation copied a non-whitelisted file.' }
    if (Test-Path -LiteralPath (Join-Path $skinRoot 'OtherSkin')) { throw 'Skin installation copied an unrelated skin folder.' }
    if (!(Test-Path -LiteralPath (Join-Path $cache 'RetrOcOlOr_by_hiphopium.rmskin') -PathType Leaf)) { throw 'Skin installation did not cache the inner archive.' }

    # The same path must also work when the dependency only declares the .rmskin wildcard.
    $wildcardRoot = Join-Path $testRoot 'wildcard\Skins'
    Install-SkinData -ArchivePath $outer -Dependency ([pscustomobject]@{ innerArchiveSha256 = $dependency.innerArchiveSha256 }) -SkinName 'RetrOcOlOr' -SkinRoot $wildcardRoot -CacheDir $cache
    if (!(Test-Path -LiteralPath (Join-Path $wildcardRoot 'RetrOcOlOr\Visualizer\Visualizer.ini') -PathType Leaf)) { throw 'Wildcard inner-archive selection did not extract the skin.' }

    Write-Output 'PASS: skin archives install from a single .rmskin entry under StrictMode without a scalar Count failure; only whitelisted skin files are extracted.'
} finally {
    $resolved = [IO.Path]::GetFullPath($testRoot).TrimEnd('\')
    $temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
    if ((Split-Path -Parent $resolved) -ine $temporaryParent -or (Split-Path -Leaf $resolved) -notmatch '^adc-skin-[a-f0-9]{12}$') {
        throw 'Refusing to clean an unexpected skin-install fixture path.'
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
