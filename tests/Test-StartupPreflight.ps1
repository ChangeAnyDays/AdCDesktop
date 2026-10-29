[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $PSScriptRoot '.artifacts'
$fixtureRoot = Join-Path $artifactRoot ('startup-preflight-' + [Guid]::NewGuid().ToString('N'))
$module = $null

function Assert-Fails {
    param([scriptblock]$Action, [string]$MessagePattern)
    $failure = $null
    try { & $Action } catch { $failure = $_.Exception.Message }
    if (!$failure -or $failure -notmatch $MessagePattern) {
        throw ('Expected failure matching "{0}", received "{1}".' -f $MessagePattern, $failure)
    }
}

try {
    [void][IO.Directory]::CreateDirectory($fixtureRoot)
    foreach ($relative in @('Rainmeter\Rainmeter.exe','Nexus\Nexus.exe','TranslucentTB\TranslucentTB.exe','SpectrumControl\AdCDesktop.exe')) {
        $path = Join-Path $fixtureRoot $relative
        [void][IO.Directory]::CreateDirectory((Split-Path -Parent $path))
        [IO.File]::WriteAllText($path, 'Not an executable: isolated test fixture.')
    }
    $module = Import-Module (Join-Path $repositoryRoot 'scripts\AdCDesktop.Common.psm1') -Force -PassThru
    # These mocks live inside the imported module. No real process query or
    # process launch is used by the readiness checks or component-start tests.
    & $module {
        $script:StartupFakeProcesses = @()
        $script:StartupLaunchCount = 0
        function script:Get-Process {
            param([string[]]$Name, $ErrorAction)
            $script:StartupFakeProcesses | Where-Object { $_.ProcessName -in $Name }
        }
        function script:Start-Process {
            param($FilePath, $WorkingDirectory, $WindowStyle)
            $script:StartupLaunchCount++
        }
    }

    foreach ($relative in @('Rainmeter\Rainmeter.exe','Nexus\Nexus.exe','TranslucentTB\TranslucentTB.exe')) {
        $name = [IO.Path]::GetFileNameWithoutExtension($relative)
        $foreignPath = Join-Path (Join-Path $fixtureRoot 'another-installation') ([IO.Path]::GetFileName($relative))
        & $module { param($name,$path) $script:StartupFakeProcesses = @([pscustomobject]@{ ProcessName=$name; Path=$path; Id=1234 }) } $name $foreignPath
        Assert-Fails { Assert-InstalledComponentReady $fixtureRoot $relative } ('Another ' + $name + ' installation')
        Assert-Fails { Start-InstalledComponent $fixtureRoot $relative } ('Another ' + $name + ' installation')

        $ownPath = Join-Path $fixtureRoot $relative
        & $module { param($name,$path) $script:StartupFakeProcesses = @([pscustomobject]@{ ProcessName=$name; Path=$path; Id=1234 }) } $name $ownPath
        $ready = Assert-InstalledComponentReady $fixtureRoot $relative
        if (!$ready.IsRunning -or $ready.Path -ine $ownPath) { throw 'A same-directory running component was not recognized.' }
        Start-InstalledComponent $fixtureRoot $relative
        if ((& $module { $script:StartupLaunchCount }) -ne 0) { throw 'A running component was launched again.' }
    }

    & $module { $script:StartupFakeProcesses = @() }
    Assert-Fails { Assert-InstalledComponentReady $fixtureRoot 'missing\Rainmeter.exe' } 'Installed component not found'
    Assert-Fails { Assert-InstalledComponentReady $fixtureRoot '' } 'Installed component not found'
    Assert-Fails { Assert-InstalledComponentReady $fixtureRoot '..\outside.exe' } 'escapes the installation directory'
    $ready = Assert-InstalledComponentReady $fixtureRoot 'Rainmeter\Rainmeter.exe'
    if ($ready.IsRunning) { throw 'A stopped component was reported as running.' }
    Start-InstalledComponent $fixtureRoot 'Rainmeter\Rainmeter.exe'
    if ((& $module { $script:StartupLaunchCount }) -ne 1) { throw 'The mock did not observe exactly one launch request for a stopped component.' }
    Remove-Module $module -Force
    $module = $null

    # Exercise the real startup orchestration against a private copy of the
    # module, with native shell and process entry points replaced by fail-fast
    # mocks. A foreign Rainmeter must fail before any backup or desktop action.
    $startupRoot = Join-Path $fixtureRoot 'startup'
    $startupScripts = Join-Path $startupRoot 'scripts'
    [void][IO.Directory]::CreateDirectory($startupScripts)
    [void][IO.Directory]::CreateDirectory((Join-Path $startupRoot 'Rainmeter'))
    [IO.File]::WriteAllText((Join-Path $startupRoot 'Rainmeter\Rainmeter.exe'), 'Not an executable.')
    [IO.File]::WriteAllText((Join-Path $startupRoot 'wallpaper32.exe'), 'Not an executable.')
    [IO.File]::WriteAllText((Join-Path $startupRoot 'project.json'), '{}')
    $testOverrides = @'

function Get-Process {
    param([string[]]$Name, $ErrorAction)
    if ('Rainmeter' -in $Name) {
        [pscustomobject]@{ ProcessName='Rainmeter'; Path=(Join-Path $PSScriptRoot '..\another-installation\Rainmeter.exe'); Id=1234 }
    }
}
function Start-Process { throw 'A process launch happened before startup preflight completed.' }
function Initialize-DesktopShell { throw 'The desktop shell was reached before startup preflight completed.' }
function Get-WallpaperRenderers { throw 'Wallpaper Engine was reached before startup preflight completed.' }
function Write-DesktopJson { throw 'A backup was written before startup preflight completed.' }
Export-ModuleMember -Function *
'@
    $moduleSource = Get-Content -LiteralPath (Join-Path $repositoryRoot 'scripts\AdCDesktop.Common.psm1') -Raw -Encoding UTF8
    [IO.File]::WriteAllText((Join-Path $startupScripts 'AdCDesktop.Common.psm1'), ($moduleSource + $testOverrides), (New-Object Text.UTF8Encoding($true)))
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'Start-Desktop.ps1') -Destination $startupRoot
    $manifest = [ordered]@{
        schemaVersion=1
        components=@{rainmeter='Rainmeter\Rainmeter.exe';panel='SpectrumControl\AdCDesktop.exe'}
        appearance=@{hideDesktopIcons=$true;taskbarAutoHide=$true}
        wallpaper=@{enginePath=(Join-Path $startupRoot 'wallpaper32.exe');projectPath=(Join-Path $startupRoot 'project.json')}
    }
    [IO.File]::WriteAllText((Join-Path $startupRoot 'desktop-style.json'), ($manifest | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($true)))
    Assert-Fails { & (Join-Path $startupRoot 'Start-Desktop.ps1') -InstallDir $startupRoot } 'Another Rainmeter installation'
    if (Test-Path -LiteralPath (Join-Path $startupRoot 'Backups')) { throw 'A failed preflight created a startup backup.' }
    Write-Output 'PASS: startup rejects foreign installations and missing components before changing the desktop; same-directory processes are reused. No real processes were queried or started.'
} finally {
    if ($module) { Remove-Module $module -Force -ErrorAction SilentlyContinue }
    Get-Module | Where-Object { $_.Path -and $_.Path.StartsWith($fixtureRoot + '\', [StringComparison]::OrdinalIgnoreCase) } | Remove-Module -Force
    $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
    $allowedParent = [IO.Path]::GetFullPath($artifactRoot).TrimEnd('\') + '\'
    if (!$resolvedFixture.StartsWith($allowedParent, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing to clean an unexpected fixture directory.' }
    if (Test-Path -LiteralPath $resolvedFixture) { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force }
}
