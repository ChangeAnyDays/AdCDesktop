Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'

function Get-ObjectValue {
    param($Object, [string]$Name, $Default = $null)
    if ($null -eq $Object) { return $Default }
    if ($Object -is [Collections.IDictionary]) { if ($Object.Contains($Name)) { return $Object[$Name] }; return $Default }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $Default }
    return $property.Value
}

function Get-FullInstallPath {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { throw 'An installation directory is required.' }
    $full = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path).TrimEnd('\')
    if ($full -eq [IO.Path]::GetPathRoot($full).TrimEnd('\')) { throw 'Choose a dedicated folder, not a drive root.' }
    return $full
}

function Resolve-InstallComponent {
    param([string]$Root, [string]$RelativePath)
    if ([string]::IsNullOrWhiteSpace($RelativePath)) { return $null }
    $base = Get-FullInstallPath $Root
    $candidate = if ([IO.Path]::IsPathRooted($RelativePath)) { [IO.Path]::GetFullPath($RelativePath) } else { [IO.Path]::GetFullPath((Join-Path $base $RelativePath)) }
    if (!$candidate.StartsWith($base + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'A component path escapes the installation directory.' }
    return $candidate
}

function Read-DesktopManifest {
    param([string]$Root)
    $file = Join-Path (Get-FullInstallPath $Root) 'desktop-style.json'
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw "Installation manifest not found: $file. Run Install.ps1 first." }
    $manifest = Get-Content -LiteralPath $file -Raw -Encoding UTF8 | ConvertFrom-Json
    if ((Get-ObjectValue $manifest 'schemaVersion' 0) -ne 1) { throw 'Unsupported installation manifest version.' }
    return $manifest
}

function Write-Utf8File {
    param([string]$Path, [string]$Content)
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Path)))
    [IO.File]::WriteAllText($Path, $Content, (New-Object Text.UTF8Encoding($true)))
}

function Write-DesktopJson {
    param([string]$Path, $Value)
    Write-Utf8File $Path ($Value | ConvertTo-Json -Depth 16)
}

function Set-IniValue {
    param([string]$Path, [string]$Section, [string]$Key, [string]$Value)
    if ($Value -match '[\r\n]') { throw 'INI values cannot contain newlines.' }
    $text = if (Test-Path -LiteralPath $Path) { Get-Content -LiteralPath $Path -Raw -Encoding UTF8 } else { '' }
    $escapedSection = [regex]::Escape($Section)
    $sectionMatch = [regex]::Match($text, '(?ims)^\[' + $escapedSection + '\][^\r\n]*(?:\r\n|\n|\r|$).*?(?=^\[|\z)')
    if (!$sectionMatch.Success) {
        $text = $text.TrimEnd("`r", "`n") + "`r`n`r`n[$Section]`r`n$Key=$Value`r`n"
    } else {
        $body = $sectionMatch.Value
        $keyPattern = '(?im)^' + [regex]::Escape($Key) + '\s*=[^\r\n]*'
        if ([regex]::IsMatch($body, $keyPattern)) { $body = [regex]::Replace($body, $keyPattern, [Text.RegularExpressions.MatchEvaluator]{ param($m) "$Key=$Value" }) }
        else { $body = $body.TrimEnd("`r", "`n") + "`r`n$Key=$Value`r`n" }
        $text = $text.Substring(0, $sectionMatch.Index) + $body + $text.Substring($sectionMatch.Index + $sectionMatch.Length)
    }
    Write-Utf8File $Path $text
}

function Get-Dependency {
    param($Manifest, [string]$Id)
    $all = Get-ObjectValue $Manifest 'dependencies'
    if ($all -is [Array]) { $entry = @($all | Where-Object { (Get-ObjectValue $_ 'id') -eq $Id } | Select-Object -First 1) }
    else { $entry = Get-ObjectValue $all $Id }
    if ($null -eq $entry -or @($entry).Count -eq 0) { throw "Dependency is not declared: $Id" }
    return @($entry)[0]
}

function Get-VerifiedArchive {
    param($Dependency, [string]$CacheDir, [switch]$Offline)
    $name = [string](Get-ObjectValue $Dependency 'fileName')
    $expected = [string](Get-ObjectValue $Dependency 'sha256')
    $url = [string](Get-ObjectValue $Dependency 'url')
    if ([IO.Path]::GetFileName($name) -ne $name -or [string]::IsNullOrWhiteSpace($name)) { throw 'Invalid dependency file name.' }
    if ($expected -notmatch '^[a-fA-F0-9]{64}$') { throw "A pinned SHA256 is required for $name." }
    $path = Join-Path $CacheDir $name
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) {
        if ($Offline) { throw "Offline cache is missing $name." }
        if ($url -notmatch '^https://') { throw 'Downloads must use HTTPS.' }
        [void][IO.Directory]::CreateDirectory($CacheDir)
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        Write-Host "Downloading $name"
        $temporary = $path + '.download'
        try {
            Invoke-WebRequest -Uri $url -OutFile $temporary -UseBasicParsing
            if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ine $expected) { throw "SHA256 mismatch for $name; no installer was started." }
            Move-Item -LiteralPath $temporary -Destination $path
        } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
    }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine $expected) { throw "Cached dependency failed SHA256 verification: $name" }
    return $path
}

function Expand-VerifiedZip {
    param([string]$ArchivePath, [string]$Destination)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $base = [IO.Path]::GetFullPath($Destination).TrimEnd('\') + '\'
    [void][IO.Directory]::CreateDirectory($base)
    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        foreach ($entry in $archive.Entries) {
            $name = $entry.FullName.Replace('/', '\')
            $target = [IO.Path]::GetFullPath((Join-Path $base $name))
            if (!$target.StartsWith($base, [StringComparison]::OrdinalIgnoreCase)) { throw 'Archive entry escapes its destination.' }
            if ($name.EndsWith('\')) { [void][IO.Directory]::CreateDirectory($target); continue }
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
        }
    } finally { $archive.Dispose() }
}

function Install-SkinData {
    param([string]$ArchivePath, $Dependency, [string]$SkinName, [string]$SkinRoot, [string]$CacheDir)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $outer = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    $innerPath = Join-Path $CacheDir ([IO.Path]::GetFileNameWithoutExtension($ArchivePath) + '.rmskin')
    try {
        $innerName = [string](Get-ObjectValue $Dependency 'innerArchive')
        $entry = if ($innerName) { @($outer.Entries | Where-Object { $_.FullName -eq $innerName } | Select-Object -First 1) } else { @($outer.Entries | Where-Object { $_.FullName -like '*.rmskin' } | Select-Object -First 1) }
        if ($entry.Count -ne 1) { throw "No skin archive found in $ArchivePath" }
        [void][IO.Directory]::CreateDirectory($CacheDir)
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry[0], $innerPath, $true)
    } finally { $outer.Dispose() }
    $innerHash = [string](Get-ObjectValue $Dependency 'innerArchiveSha256')
    if ($innerHash -notmatch '^[a-fA-F0-9]{64}$' -or (Get-FileHash -LiteralPath $innerPath -Algorithm SHA256).Hash -ine $innerHash) { throw "Inner skin archive failed SHA256 verification: $SkinName" }
    $allowed = @('.ini','.inc','.lua','.png','.jpg','.jpeg','.gif','.bmp','.svg','.ttf','.otf','.txt','.md')
    $archive = [IO.Compression.ZipFile]::OpenRead($innerPath)
    $base = [IO.Path]::GetFullPath($SkinRoot).TrimEnd('\') + '\'
    $prefix = 'Skins/' + $SkinName + '/'
    try {
        foreach ($entry in $archive.Entries) {
            if (!$entry.FullName.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or $entry.FullName.EndsWith('/')) { continue }
            if ([IO.Path]::GetExtension($entry.FullName).ToLowerInvariant() -notin $allowed) { continue }
            $relative = $entry.FullName.Substring(6).Replace('/', '\')
            $target = [IO.Path]::GetFullPath((Join-Path $base $relative))
            if (!$target.StartsWith($base, [StringComparison]::OrdinalIgnoreCase)) { throw 'A skin entry escapes the destination.' }
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
        }
    } finally { $archive.Dispose() }
}

function Find-WallpaperEngine {
    param([string]$ExplicitPath)
    if ($ExplicitPath) {
        $candidate = if (Test-Path -LiteralPath $ExplicitPath -PathType Container) { Join-Path $ExplicitPath 'wallpaper32.exe' } else { $ExplicitPath }
        if (!(Test-Path -LiteralPath $candidate -PathType Leaf) -or [IO.Path]::GetFileName($candidate) -notin @('wallpaper32.exe','wallpaper64.exe')) { throw 'WallpaperEnginePath must point to an installed wallpaper32.exe/wallpaper64.exe or its directory.' }
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($candidate)
    }
    $steamPath = $null
    try { $steamPath = (Get-ItemProperty -LiteralPath 'HKCU:\Software\Valve\Steam' -Name SteamPath -ErrorAction Stop).SteamPath } catch { }
    $libraries = New-Object 'System.Collections.Generic.List[string]'
    if ($steamPath) {
        $libraries.Add($steamPath)
        $vdf = Join-Path $steamPath 'steamapps\libraryfolders.vdf'
        if (Test-Path -LiteralPath $vdf) {
            foreach ($match in [regex]::Matches((Get-Content -LiteralPath $vdf -Raw -Encoding UTF8), '"path"\s+"(?<p>[^"]+)"')) { $libraries.Add($match.Groups['p'].Value.Replace('\\','\')) }
        }
    }
    foreach ($library in $libraries) {
        $candidate = Join-Path $library 'steamapps\common\wallpaper_engine\wallpaper32.exe'
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return [IO.Path]::GetFullPath($candidate) }
    }
    return $null
}

function Find-DomeProject {
    param([string]$EnginePath)
    if (!$EnginePath) { return $null }
    $common = Split-Path -Parent (Split-Path -Parent $EnginePath)
    $steamapps = Split-Path -Parent $common
    $candidate = Join-Path $steamapps 'workshop\content\431960\3113287126\project.json'
    if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    return $null
}

function Initialize-DesktopShell {
    if (!('AdCDesktop.NativeShell' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'DesktopShell.cs') }
}

function Get-CurrentWallpaperProject {
    param([string]$EnginePath)
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $EnginePath
    $start.Arguments = '-control getWallpaper -monitor 0'
    $start.WorkingDirectory = [IO.Path]::GetDirectoryName($EnginePath)
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true; $start.RedirectStandardOutput = $true
    $process = $null
    try {
        $process = [Diagnostics.Process]::Start($start)
        if ($process -and $process.WaitForExit(5000)) {
            $output = $process.StandardOutput.ReadToEnd().Trim()
            foreach ($line in @($output -split '[\r\n]+' | Select-Object -Last 5)) {
                $candidate = $line.Trim().Trim('"')
                if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) { return [IO.Path]::GetFullPath($candidate) }
            }
        }
    } catch { }
    finally { if ($process) { $process.Dispose() } }
    # Older versions do not expose getWallpaper. Read only the current Windows
    # user's profile instead of guessing between other users' stored profiles.
    $configPath = Join-Path ([IO.Path]::GetDirectoryName($EnginePath)) 'config.json'
    try {
        $config = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $profile = Get-ObjectValue $config ([Environment]::UserName)
        if (!$profile) { $profile = $config }
        $general = Get-ObjectValue $profile 'general'
        $selected = Get-ObjectValue (Get-ObjectValue $general 'wallpaperconfig') 'selectedwallpapers'
        $candidate = [string](Get-ObjectValue (Get-ObjectValue $selected 'Monitor0') 'file')
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) { return [IO.Path]::GetFullPath($candidate) }
    } catch { }
    return $null
}

function Get-WallpaperRenderers {
    param([string]$EnginePath)
    if (!$EnginePath) { return }
    $folder = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($EnginePath))
    $allowed = @((Join-Path $folder 'wallpaper32.exe'), (Join-Path $folder 'wallpaper64.exe'))
    Get-Process -Name wallpaper32,wallpaper64 -ErrorAction SilentlyContinue | Where-Object { try { $_.Path -in $allowed } catch { $false } }
}

function Assert-InstalledComponentReady {
    param([string]$Root, [string]$RelativePath)
    $path = Resolve-InstallComponent $Root $RelativePath
    if (!$path -or !(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Installed component not found: $RelativePath" }
    $name = [IO.Path]::GetFileNameWithoutExtension($path)
    $candidates = @(Get-Process -Name $name -ErrorAction SilentlyContinue)
    $exists = @($candidates | Where-Object { try { $_.Path -ieq $path } catch { $false } })
    if (!$exists.Count -and $candidates.Count -and $name -in @('Rainmeter','Nexus','TranslucentTB')) {
        throw "Another $name installation is already running. Close it yourself before starting this installation; it was not stopped or reconfigured."
    }
    return [pscustomobject]@{ Path=$path; IsRunning=($exists.Count -gt 0) }
}

function Start-InstalledComponent {
    param([string]$Root, [string]$RelativePath)
    # Repeat the read-only check immediately before launching, since processes
    # can change after Start-Desktop completes its initial preflight.
    $ready = Assert-InstalledComponentReady $Root $RelativePath
    if (!$ready.IsRunning) { Start-Process -FilePath $ready.Path -WorkingDirectory ([IO.Path]::GetDirectoryName($ready.Path)) -WindowStyle Hidden }
}

function Invoke-WallpaperControl {
    param([string]$EnginePath, [string]$Command, [string]$ProjectPath)
    if ([IO.Path]::GetFileName($EnginePath) -notin @('wallpaper32.exe','wallpaper64.exe') -or !(Test-Path -LiteralPath $EnginePath -PathType Leaf)) { throw 'Wallpaper Engine is not available.' }
    if ($Command -notin @('openWallpaper','closeWallpaper')) { throw 'Unsupported wallpaper command.' }
    $arguments = '-control ' + $Command + ' -monitor 0'
    if ($Command -eq 'openWallpaper') {
        if (!(Test-Path -LiteralPath $ProjectPath -PathType Leaf) -or $ProjectPath.Contains('"')) { throw 'Wallpaper project does not exist.' }
        $arguments += ' -file "' + [IO.Path]::GetFullPath($ProjectPath) + '"'
    }
    $process = Start-Process -FilePath $EnginePath -ArgumentList $arguments -WorkingDirectory ([IO.Path]::GetDirectoryName($EnginePath)) -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(15000)) { throw 'Wallpaper Engine did not finish processing the command. Its process was left running.' }
    if ($process.ExitCode -ne 0) { throw 'Wallpaper Engine could not process the requested command.' }
}

Export-ModuleMember -Function *
