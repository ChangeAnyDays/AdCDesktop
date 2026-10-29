[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$checker = Join-Path $repositoryRoot 'scripts\Test-Publish.ps1'
$testRoot = Join-Path $PSScriptRoot ('.artifacts\publish-' + [Guid]::NewGuid().ToString('N'))
$utf8 = New-Object Text.UTF8Encoding($true)
$checks = 0

function New-PublishFixture([string]$Name, [string]$Text) {
    $path = Join-Path $testRoot $Name
    [void][IO.Directory]::CreateDirectory($path)
    [IO.File]::WriteAllText((Join-Path $path 'README.md'),$Text,$utf8)
    return $path
}
function Assert-Rejected([string]$Path, [string]$Name) {
    $rejected = $false
    try { & $checker -RepositoryRoot $Path | Out-Null }
    catch { $rejected = $true }
    if (!$rejected) { throw "Publish gate accepted $Name" }
}

$clean = New-PublishFixture 'clean' 'A source-only desktop control panel.'
& $checker -RepositoryRoot $clean | Out-Null
$checks++
$publicPath = 'C:' + '\Users\Public\Documents'
$public = New-PublishFixture 'public-profile' $publicPath
& $checker -RepositoryRoot $public | Out-Null
$checks++
$privatePath = 'C:' + '\Users\' + 'fixture-account' + '\Desktop\notes.txt'
Assert-Rejected (New-PublishFixture 'private-profile' $privatePath) 'an absolute account profile'
$checks++
$accountId = '765611' + ('1' * 11)
Assert-Rejected (New-PublishFixture 'account-id' $accountId) 'a personal Steam account ID'
$checks++
$credential = 'ghp_' + ('A' * 32)
Assert-Rejected (New-PublishFixture 'credential' $credential) 'an access credential'
$checks++
$privateKey = '-----BEGIN ' + 'PRIVATE KEY-----'
Assert-Rejected (New-PublishFixture 'private-key' $privateKey) 'a private key header'
$checks++
$binary = New-PublishFixture 'binary' 'No third-party executables belong in source.'
[IO.File]::WriteAllText((Join-Path $binary 'Installer.exe'),'fixture only',$utf8)
Assert-Rejected $binary 'a packaged executable'
$checks++
$wallpaper = New-PublishFixture 'wallpaper' 'Wallpaper packages remain external.'
[IO.File]::WriteAllText((Join-Path $wallpaper 'scene.pkg'),'fixture only',$utf8)
Assert-Rejected $wallpaper 'a wallpaper package'
$checks++
$nested = New-PublishFixture 'nested-source' 'Clean root document; nested source must also be reviewed.'
$nestedSource = Join-Path $nested 'src'
[void][IO.Directory]::CreateDirectory($nestedSource)
[IO.File]::WriteAllText((Join-Path $nestedSource 'Nested.cs'), ('// ' + $privatePath), $utf8)
Assert-Rejected $nested 'a private path in nested source below an artifacts ancestor'
$checks++
Write-Output ("PASS: {0} publish-gate fixtures." -f $checks)
