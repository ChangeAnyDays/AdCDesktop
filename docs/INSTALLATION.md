# 安装、启动与恢复

## 系统与目录

推荐 Windows 11 x64、Windows PowerShell 5.1、.NET Framework 4.8。构建使用系统已有编译器，不安装 SDK。

安装目录应是本项目专用文件夹。可以包含中文和空格，不能直接使用盘符根目录，也不能覆盖源码仓库根目录。默认有 D 盘时使用 `D:\AdCDesktop`，否则使用 `%LOCALAPPDATA%\AdCDesktop`。

## 安装模式

```powershell
# 默认：频谱控制台、Rainmeter、时钟/频谱皮肤和 Nexus
.\Install.ps1 -InstallDir 'E:\桌面工具'

# 只装频谱，不装 Nexus
.\Install.ps1 -InstallDir 'E:\桌面工具' -IncludeNexus:$false

# 完整桌面：再加透明任务栏
.\Install.ps1 -Mode Full -InstallDir 'E:\桌面工具' -IncludeNexus -IncludeTaskbar
```

安装会下载并校验清单中的归档，安装所需数据、构建原创控制台，创建本机清单与入口。直接运行 `Install.ps1` 默认只准备文件，不启动组件；双击 `Install.cmd` 使用 Full 模式并追加 `-Launch`，安装完成后隐藏图标、自动隐藏任务栏，并启动 Rainmeter、频谱和 Nexus。不会添加开机启动。Nexus 官方安装器可能请求 Windows 管理员授权，需要用户在系统提示中允许。

Release 中的 `ADC-Desktop-Setup-1.3.0.exe` 是单文件启动器：内部展开安装文件后调用同一个 `Install.cmd`，因此不需要手动解压；ZIP 仍保留为备用。

安装后提供“启动AdC桌面美化”和“恢复原桌面”两个入口。前者重新启动 Nexus/频谱并保持完整桌面效果；后者恢复安装前的图标、任务栏和壁纸。不再提供“标准桌面效果”快捷入口。

同目录重复安装会保留已有模式与频谱配置。若希望从 Full 改用仅控制台模式，先恢复原桌面，再为 ControlPanel 模式选择另一个专用目录。

双击 CMD 入口时，仅为它启动的 PowerShell 进程设置执行策略，不更改系统或用户的全局执行策略。受组织策略管理的电脑仍遵循组织设置。

| 参数 | 含义 |
|---|---|
| `InstallDir` | 目标目录 |
| `Mode` | `ControlPanel`（默认）或 `Full` |
| `IncludeNexus` | 默认开启；传 `-IncludeNexus:$false` 可只装频谱 |
| `IncludeTaskbar` | Full 模式下安装 Windows 11 透明任务栏组件 |
| `WallpaperEnginePath` | 已安装 Wallpaper Engine 的目录或主程序 |
| `WallpaperProject` | 用户已拥有的本地 `project.json` |
| `CacheDir` | 下载缓存，默认源码目录的 `.cache/dependencies` |
| `NoShortcuts` | 不创建桌面快捷方式 |
| `Launch` | 安装完成后启动已配置的组件；`Install.cmd` 已默认传入 |
| `WhatIf` | 只输出安装计划 |
| `Offline` | 不下载、不执行第三方安装器，仅使用准备好的组件 |
| `PreparedComponentsPath` | 离线组件目录 |
| `PreparedControlPanelPath` | 可选，已经构建好的控制台输出目录 |

## Nexus 首次布局

Nexus 使用全局的用户配置，安装器不会把另一台电脑的注册表导入到你的账户中。如果已发现现有 Nexus 配置，会保留它并停止第二份自动安装。新装时安装器把第一个 Dock 放到屏幕底部，避免落在顶部边缘；之后的图标和应用仍按 [Nexus 布局指南](NEXUS.md) 自行调整。Nexus 本身的管理员确认属于 Windows 安装过程，需要用户在系统提示中决定。

## 同款动态壁纸

Wallpaper Engine 是独立付费软件，不由本项目提供。先在 Steam 安装并订阅 [Dome 4k](https://steamcommunity.com/sharedfiles/filedetails/?id=3113287126)，等待下载完成。

脚本尝试从 Steam 及其库目录检测安装。也可以显式指定实际目录和壁纸 `project.json`。识别为同款 Dome 后，只在安装目录生成本机私有副本，关闭其重复的时钟、媒体、频谱和背景音乐；不会修改原 Workshop 文件。

该副本、下载数据和本机清单均属于运行数据，不要提交 Git 或放入源码压缩包。

## 离线准备

```text
PreparedAdCDesktop/
  Rainmeter/
    Rainmeter.exe
    Skins/
      Welfare/Clock/Clock.ini
      RetrOcOlOr/Visualizer/Visualizer.ini
      RetrOcOlOr/@Resources/Variables.inc
  Nexus/Nexus.exe                  # 选择该组件时需要
  TranslucentTB/TranslucentTB.exe   # 选择该组件时需要
```

上图只列必要入口。真正使用时要保留程序自身的其他依赖、皮肤 Lua、字体等完整文件；不能只复制这几个文件名。

```powershell
.\Install.ps1 -InstallDir 'E:\桌面工具' -Offline `
  -PreparedComponentsPath 'F:\PreparedAdCDesktop' `
  -PreparedControlPanelPath 'F:\ControlPanelBuild'
```

`PreparedControlPanelPath` 应包含 `AdCDesktop.exe` 及其运行配置。不指定时可在离线状态下使用系统编译器构建。离线模式不创建桌面快捷方式、不执行下载的安装器，也不启动假组件；适合在自行准备完整文件后部署或做隔离测试。

## 日常使用

- **只调整频谱**：打开“AdC桌面美化”，选择预设或拖动滑块，点击“应用到桌面”。
- **启动桌面组合**：双击安装目录的 `Start-Desktop.cmd`。
- **打开面板并启动组合**：运行 `Start-Desktop.ps1 -ControlPanel`。
- **恢复桌面**：双击 `Restore-Desktop.cmd`。只管理清单里、位于本安装目录内的进程。

Full 模式启动时会先备份本机图标显示、任务栏自动隐藏和当前壁纸，再应用隐藏图标及任务栏自动隐藏。ControlPanel 模式只启动清单中的 Rainmeter 和 Nexus，不调整这些系统外观。

若 Wallpaper Engine 原来正在运行，脚本需要能可靠读取原壁纸才能替换；无法识别时保留原壁纸并提示，其他组件仍可使用。恢复动态壁纸使用官方控制指令，不强制结束其进程。

当前壁纸自动化仅管理 Wallpaper Engine 的第一个显示器（monitor 0）。使用的获取、打开和关闭指令见 [官方命令行说明](https://help.wallpaperengine.io/en/functionality/cli.html)；其他显示器请在 Wallpaper Engine 中自行配置。

若之后手动退出 Wallpaper Engine 或删除了原壁纸文件，恢复脚本会提示无法重新打开该动态壁纸；图标和任务栏仍可恢复。此时可在 Wallpaper Engine 中手动重新选择壁纸。

## 常见问题

- **频谱是细线**：这是静音时的正常表现，播放系统声音后会起伏。
- **频谱没有显示**：打开控制台，确认组件正在运行并勾选“显示频谱”，再点击应用。
- **更换耳机后不响应**：在 Rainmeter 管理器刷新该频谱，让它重新连接默认音频设备。
- **下载校验失败**：保留错误信息，检查固定来源与版本；不要删除校验逻辑绕过失败。
- **脚本被 Windows 阻止**：检查下载文件的属性和组织执行策略；在允许的环境中解除已审核项目文件的下载锁定，不必修改全局策略。
- **透明任务栏不兼容系统更新**：退出该可选组件，其他功能仍独立可用。
