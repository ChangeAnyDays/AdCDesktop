[CmdletBinding()]
param(
    [string]$InstallDir,
    [switch]$Quiet,
    [switch]$Confirmed
)
$ErrorActionPreference = 'Stop'

function Show-UninstallMessage([string]$Message, [string]$Title, [int]$Icon) {
    if ($Quiet) { Write-Host $Message; return }
    Add-Type -AssemblyName System.Windows.Forms
    [void][System.Windows.Forms.MessageBox]::Show($Message, $Title, [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]$Icon)
}

function Test-UnderRoot([string]$Path, [string]$Root) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    try { $full = [IO.Path]::GetFullPath($Path) } catch { return $false }
    return $full.StartsWith($Root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)
}

if ([string]::IsNullOrWhiteSpace($InstallDir)) { $InstallDir = $PSScriptRoot }
$InstallDir = [IO.Path]::GetFullPath($InstallDir).TrimEnd('\')
$manifest = Join-Path $InstallDir 'desktop-style.json'
$panel = Join-Path $InstallDir 'SpectrumControl\AdCDesktop.exe'
if (!(Test-Path -LiteralPath $manifest -PathType Leaf) -and !(Test-Path -LiteralPath $panel -PathType Leaf)) {
    throw "这不是 AdCat 安装目录，未找到 desktop-style.json 或 SpectrumControl\AdCDesktop.exe：$InstallDir"
}
if ($InstallDir -match '^[A-Za-z]:\\?$') {
    throw "拒绝卸载这个目录：$InstallDir"
}

if (!$Quiet -and !$Confirmed) {
    Add-Type -AssemblyName System.Windows.Forms
    $answer = [System.Windows.Forms.MessageBox]::Show(
        "将卸载 AdCat 桌面，并一起删除：`n`n• Rainmeter / 频谱 / 时钟皮肤`n• Nexus 及其当前配置`n• 控制台、开始菜单和桌面快捷方式`n• 自启动项和安装目录`n`n会先恢复桌面图标、任务栏和壁纸。`n`n确定继续吗？",
        "卸载 AdCat 桌面",
        [System.Windows.Forms.MessageBoxButtons]::YesNo,
        [System.Windows.Forms.MessageBoxIcon]::Warning)
    if ($answer -ne [System.Windows.Forms.DialogResult]::Yes) { return }
}

$failed = New-Object System.Collections.Generic.List[string]
$restore = Join-Path $InstallDir 'Restore-Desktop.ps1'
if (Test-Path -LiteralPath $restore -PathType Leaf) {
    try { & $restore -InstallDir $InstallDir } catch { $failed.Add('恢复桌面：' + $_.Exception.Message) }
}

# Stop only processes that belong to this installation.
foreach ($process in @(Get-Process -ErrorAction SilentlyContinue)) {
    try {
        if ($process.Path -and (Test-UnderRoot $process.Path $InstallDir)) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
    } catch { }
}
Start-Sleep -Milliseconds 500

# Use Nexus's official uninstaller when present; fall back to removing the folder.
$nexusExe = Join-Path $InstallDir 'Nexus\Nexus.exe'
if (Test-Path -LiteralPath $nexusExe -PathType Leaf) {
    $uninstaller = Get-ChildItem -LiteralPath (Join-Path $InstallDir 'Nexus') -Filter 'unins*.exe' -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($uninstaller) {
        try {
            $p = Start-Process -FilePath $uninstaller.FullName -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -PassThru -Wait -ErrorAction Stop
            if ($p.ExitCode -ne 0) { $failed.Add('Nexus 官方卸载器返回代码 ' + $p.ExitCode) }
        } catch { $failed.Add('Nexus 官方卸载器：' + $_.Exception.Message) }
    }
}

# Remove shortcuts whose target points into this installation.
$shortcutShell = New-Object -ComObject WScript.Shell
$shortcutRoots = @(
    [Environment]::GetFolderPath('Desktop'),
    [Environment]::GetFolderPath('Programs'),
    [Environment]::GetFolderPath('CommonPrograms')
) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Container) } | Select-Object -Unique
foreach ($base in $shortcutRoots) {
    foreach ($link in @(Get-ChildItem -LiteralPath $base -Filter '*.lnk' -File -Recurse -ErrorAction SilentlyContinue)) {
        try {
            $target = $shortcutShell.CreateShortcut($link.FullName).TargetPath
            if (Test-UnderRoot $target $InstallDir) { Remove-Item -LiteralPath $link.FullName -Force -ErrorAction Stop }
        } catch { }
    }
}
$startMenuFolder = Join-Path ([Environment]::GetFolderPath('Programs')) 'AdCat 桌面'
if (Test-Path -LiteralPath $startMenuFolder -PathType Container) {
    try { Remove-Item -LiteralPath $startMenuFolder -Recurse -Force } catch { $failed.Add('开始菜单：' + $_.Exception.Message) }
}

# Remove only startup values that point into this installation.
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (Test-Path -LiteralPath $runKey) {
    $key = Get-Item -LiteralPath $runKey
    foreach ($name in @('Nexus','AdCDesktop.Spectrum','AdCDesktop.Nexus')) {
        try {
            $value = [string]$key.GetValue($name, '')
            if ($value -and $value.IndexOf($InstallDir, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                Remove-ItemProperty -LiteralPath $runKey -Name $name -Force -ErrorAction Stop
            }
        } catch { }
    }
}
$approvedKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'
if (Test-Path -LiteralPath $approvedKey) {
    foreach ($name in @('Nexus','AdCDesktop.Spectrum','AdCDesktop.Nexus')) {
        try { Remove-ItemProperty -LiteralPath $approvedKey -Name $name -Force -ErrorAction SilentlyContinue } catch { }
    }
}

# Remove the per-user Add/Remove entry.
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AdCatDesktop'
if (Test-Path -LiteralPath $uninstallKey) {
    try { Remove-Item -LiteralPath $uninstallKey -Recurse -Force } catch { $failed.Add('卸载登记：' + $_.Exception.Message) }
}

# Nexus is owned by this installation when its executable lives under the root.
if (Test-Path -LiteralPath $nexusExe -PathType Leaf) {
    try { Remove-Item -LiteralPath 'HKCU:\Software\WinSTEP2000\NeXuS' -Recurse -Force -ErrorAction SilentlyContinue } catch { }
}
# Rainmeter portable keeps its settings inside the installation folder. Do not
# remove the shared HKCU\Software\Rainmeter key, which may belong to another copy.

# Remove the installation directory after this PowerShell process exits.
$removeCommand = '/d /c ping 127.0.0.1 -n 3 >nul & rmdir /s /q "' + $InstallDir + '"'
try { Start-Process -FilePath $env:ComSpec -ArgumentList $removeCommand -WindowStyle Hidden | Out-Null }
catch {
    try { Remove-Item -LiteralPath $InstallDir -Recurse -Force -ErrorAction Stop }
    catch { $failed.Add('删除安装目录：' + $_.Exception.Message) }
}

$message = if ($failed.Count -eq 0) {
    "AdCat 桌面、Nexus、Rainmeter/频谱和快捷方式已卸载。`n`n安装目录会在几秒内删除。"
} else {
    "卸载基本完成，但以下项目需要手动处理：`n`n" + ($failed -join "`n")
}
Show-UninstallMessage $message '卸载 AdCat 桌面' 64
if ($failed.Count -gt 0) { exit 2 }
