[CmdletBinding()]
param([string]$RepositoryRoot)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { $RepositoryRoot = Split-Path -Parent $PSScriptRoot }
$repositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\','/')
$tracked = @()
$git = Get-Command git -ErrorAction SilentlyContinue
if ($null -ne $git -and (Test-Path -LiteralPath (Join-Path $repositoryRoot '.git'))) {
    $tracked = @(& $git.Source -C $repositoryRoot ls-files --cached --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate the intended Git files.' }
}
else {
    # Source ZIPs have no Git index. Restrict fallback enumeration to source directories.
    $sourceDirectories = @('.github','assets','config','docs','scripts','src','tests')
    $tracked = @(Get-ChildItem -LiteralPath $repositoryRoot -File -Force | ForEach-Object Name)
    foreach ($directory in $sourceDirectories) {
        $directoryPath = Join-Path $repositoryRoot $directory
        if (Test-Path -LiteralPath $directoryPath) {
            $tracked += @(Get-ChildItem -LiteralPath $directoryPath -File -Force -Recurse | Where-Object {
                $_.FullName.Substring($repositoryRoot.Length+1) -notmatch '(?:^|[\\/])(?:\.artifacts|artifacts|bin|obj)[\\/]'
            } | ForEach-Object { $_.FullName.Substring($repositoryRoot.Length+1).Replace('\','/') })
        }
    }
}
$tracked = @($tracked | Sort-Object -Unique)
if ($tracked.Count -eq 0) { throw 'No publishable source files were found.' }
$issues = New-Object 'System.Collections.Generic.List[string]'
$forbiddenExtensions = @('.exe','.dll','.msi','.msix','.msixbundle','.appx','.zip','.7z','.rar','.pkg','.mp4','.mov','.mp3','.gif','.reg','.lnk','.log','.dmp','.pfx','.p12','.key')
$textExtensions = @('.html','.md','.txt','.cs','.xaml','.ps1','.psm1','.psd1','.json','.yml','.yaml','.xml','.manifest','.cmd','.bat','.sln','.csproj')
$rules = @(
    @{Name='absolute local user profile';Pattern='(?i)[A-Z]:[\\/]+Users[\\/]+(?!(?:Public|Default)(?:[\\/]|$))[^\\/\r\n"''<>$]+'},
    @{Name='personal Steam account ID';Pattern='(?<![A-Za-z0-9])765611\d{11}(?![A-Za-z0-9])'},
    @{Name='GitHub access credential';Pattern='(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{40,})'},
    @{Name='private key';Pattern='-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'}
)
foreach ($relativePath in $tracked) {
    $normalized = $relativePath.Replace('\','/')
    if ($normalized -match '^(?:\.git/|artifacts/|runtime/|downloads/|tests/\.artifacts/)' -or $normalized -match '(?i)(?:^|/)(?:\.env(?:\..*)?|\.git-credentials|id_rsa|credentials\.json)$') {
        $issues.Add($normalized + ': runtime or credential file must not be published.'); continue
    }
    $path = Join-Path $repositoryRoot $relativePath
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { continue }
    $resolved = [IO.Path]::GetFullPath($path)
    if (!$resolved.StartsWith($repositoryRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { $issues.Add($normalized + ': path escapes repository.'); continue }
    $item = Get-Item -LiteralPath $path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { $issues.Add($normalized + ': linked file requires manual review.'); continue }
    if ($forbiddenExtensions -contains $item.Extension.ToLowerInvariant()) { $issues.Add($normalized + ': packaged executable, third-party asset or personal runtime file.'); continue }
    if ($item.Length -gt 5MB) { $issues.Add($normalized + ': source file exceeds the 5 MB review limit.'); continue }
    if ($item.Extension -in @('.png','.ico')) {
        if ($normalized -notmatch '^(?:assets/(?:app\.ico|app-icon\.png|logo\.png)|assets/nexus-presets/(?:minimal|balanced|showcase|compact|rainglass)\.png|docs/images/control-panel\.png)$') { $issues.Add($normalized + ': binary image is not in the reviewed original-artwork list.') }
        continue
    }
    if ($textExtensions -notcontains $item.Extension.ToLowerInvariant() -and $item.Name -notin @('LICENSE','NOTICE','.gitignore','.gitattributes','.editorconfig')) { $issues.Add($normalized + ': unknown file type requires review.'); continue }
    $text = Get-Content -LiteralPath $path -Raw -Encoding UTF8
    foreach ($rule in $rules) {
        $matches = [regex]::Matches($text,$rule.Pattern)
        foreach ($match in $matches) {
            $line = 1 + [regex]::Matches($text.Substring(0,$match.Index),"`n").Count
            $issues.Add(('{0}:{1}: {2}' -f $normalized,$line,$rule.Name))
        }
    }
}
if ($issues.Count -gt 0) { throw ("Publish review failed:`r`n" + ($issues -join "`r`n")) }
Write-Output ("PASS: {0} intended source files contain no detected personal paths, credentials or bundled third-party runtime assets." -f $tracked.Count)
