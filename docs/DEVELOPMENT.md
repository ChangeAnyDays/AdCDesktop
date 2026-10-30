# 开发与验证

## 构建

主构建入口是 `scripts/Build-ControlPanel.ps1`，调用 Windows .NET Framework 自带的 C# 编译器，不下载编译器、NuGet 包或 SDK。编译语言级别兼容 C# 5。

```powershell
.\scripts\Build-ControlPanel.ps1
.\scripts\Build-ControlPanel.ps1 -OutputDir 'E:\Build output\control-panel'
```

要求存在 .NET Framework 4.8 编译器及 WPF 程序集。脚本会检查 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319`，并回退到 32 位 Framework 目录；缺失时会说明原因。

也可以用安装了 .NET Framework 4.8 目标包的 Visual Studio 打开 `src/ControlPanel/AdCDesktop.csproj`。源码 XAML 是嵌入资源，不需要额外的界面生成步骤。

## 控制台结构

| 文件 | 职责 |
|---|---|
| `SpectrumPanel.xaml` | 界面和样式 |
| `SpectrumPanel.cs` | 交互、预览、个人方案、启动入口 |
| `NexusBackend.cs` | Nexus 路径与进程检查、受限文字设置读写、备份与回退 |
| `StartupBackend.cs` | 当前用户两个独立 Run 启动项、读回校验、失败回退及归属检查 |
| `SpectrumPreview.cs` | 可测试的模拟音源与回落模型 |
| `SpectrumBackend.cs` | 读取配置、校验、原子更新、备份和 Rainmeter 联动 |
| `app.manifest` | 普通用户权限和 DPI 声明 |

程序默认从自身所在 `SpectrumControl` 目录的父目录寻找 Rainmeter，也支持：

```powershell
& .\artifacts\control-panel\AdCDesktop.exe --root 'E:\My desktop tools'
```

若找不到配置，普通界面会提示选择安装目录。不会回退到作者电脑的盘符或账户路径。

## 写入范围

频谱页面只修改指定根目录下：

- `Rainmeter/Skins/RetrOcOlOr/Visualizer/Visualizer.ini` 的频谱尺寸及音频响应字段。
- `Rainmeter/Skins/RetrOcOlOr/@Resources/Variables.inc` 的两端颜色。
- `Rainmeter/Rainmeter.ini` 中该频谱的显示、位置、透明度和鼠标行为。
- `SpectrumControl/Backups` 与 `SpectrumControl/my-preset.json`。

首次应用保存原始备份，每次应用保存上次备份；恢复仅合并目标频谱设置，保留其他皮肤及之后发生的无关修改。原子替换对 Rainmeter 短时文件占用做有界重试，失败则尝试回退。

## 验证

```powershell
.\tests\Test-ControlPanel.ps1
.\tests\Test-Scripts.ps1
.\tests\Test-Install.ps1
.\tests\Test-StartupPreflight.ps1
.\tests\Test-PublishGate.ps1
.\scripts\Test-Publish.ps1
```

`tests/.artifacts` 保存检查结果和截图，不进入 Git。控制台测试的可执行程序和配置样本放在每次新建的短 `%TEMP%/adc-ui-*`、`%TEMP%/adc-backend-*` 目录，避免深层仓库路径触发 .NET Framework 的 MAX_PATH 限制。WPF 样本有独占令牌，清理前核对完整路径、目录名和令牌；只删除本次创建的临时目录。测试覆盖颜色和数值校验、编码和换行保留、事务失败回退、Alpha 原值保留、悬停模式、个人方案、显示开关与恢复范围。

安装测试使用无效的假程序验证离线流程不会执行组件，覆盖中文和空格目录、Dome 私有副本、重复安装保留修改及模式切换保护。启动预检测试用模拟进程检查冲突、同目录复用和缺失组件，避免在检查失败前更改外观。

真实桌面联动与隔离测试分开。CI 不发送 Rainmeter 控制命令、不启动第三方安装器、不更改 Explorer、任务栏或用户壁纸。

### 离屏界面检查

```powershell
& .\artifacts\control-panel\AdCDesktop.exe --screenshot `
  "$PWD\artifacts\panel.png" --root 'E:\FixtureRoot'
```

FixtureRoot 至少应有真实布局的三份配置，测试脚本会生成它们。`--smoke-test <fixtureRoot> <result.json>` 会修改所提供的样本目录，构造的后端禁用运行时集成；只能对测试样本使用。

`Test-ControlPanel.ps1` 会实际创建并显示离屏 WPF 窗口，运行交互检查，并检查两页在 1080×840、940×650 窗口和 100% / 125% / 150% / 200% **内容 ScaleTransform 模拟缩放**下的布局，共 16 个场景。检查将关键操作滚动到视野内，确认完整控件位于窗口和各级滚动视口内；每场景保存初始及滚动后截图，位于 `tests/.artifacts/ui-layout/`。这种模拟验证 WPF 布局和可达性，**不是切换 Windows 系统 DPI 的实测**，也不验证实际 Rainmeter 音频或 Nexus 悬停效果。

测试按原字节将构建的 EXE/config 复制到独占本机临时目录再加载，避免源码解压目录的区域信息或旧 ACL 影响程序集加载。使用 PowerShell 7 的 `Start-Process` 启动 Windows PowerShell 5.1 时，应传递 Windows PowerShell 的模块路径，避免继承 PowerShell 7 模块目录而错误加载 Utility 模块；直接在 Windows PowerShell 5.1 中执行上述命令即可。

GitHub Actions 同时上传 WPF 结果 JSON 和这些纯控制台截图；测试失败时也保留可用证据。工作流中的 Actions 固定提交已与官方 `v7` 标签核对。GitHub 托管 Windows runner 的自动检查和本机 Windows 11 验证应分别记录，不能据此声称完成了重启登录测试。

### 值的保留

`AlphaValue=178` 显示为约 70%，但没有调整透明度时仍保留 178，不能往返变为 179。Rainmeter 的 `OnHover` 分别为 0=不变、1=隐藏、2=淡入、3=淡出；鼠标穿透开启时不会触发悬停效果。

## 更新外部依赖

先检查官方发布和适用许可，独立下载并计算新 SHA-256，再更新 `config/dependencies.json` 和 `docs/DEPENDENCIES.md`。不要用未经验证的新文件替换旧版本校验值，也不要把下载包提交进仓库。

## 总控台适配与验收

Nexus 页面另外保存 `SpectrumControl/nexus-path.txt`（仅本机路径，不应提交）和 `Backups/nexus-text-original.json`。配置适配器读取当前用户注册表的 32 位视图 `Software\WinSTEP2000\NeXuS`，只修改已有 `ShowIconTooltips` 和 `Docks` 下的 `DockShowLabels` / `DockShowLabels1` 等正整数编号字段。不创建未知配置，不修改自动启动、不改变图标布局。运行中的 Nexus 可能在退出时覆盖注册表，因此写入前检查并要求退出。

适配器按已识别字段和类型限制写入；它不是已获保证的公开 API。应用先备份，写后重新读取校验；部分失败尝试回退，回退失败明确报告。恢复按当前仍存在的键合并，保留其他设置。支持 True/False 和已识别的整数值，原先启用的气泡子模式在未改变开关时保留。

`ControlCenterTests.cs` 使用内存配置模拟器，验证关闭/恢复、原值保留、失败回退、未知格式拒绝和频谱响应。`FixtureUiChecks.cs` 验证页面切换保留未应用的频谱修改、暂停切换以及测试模式不能启动/修改 Nexus。截图模式也禁用运行时集成。

发布前在 Windows 上运行本页已有的全部检查，并手动验收：

1. 100% / 150% DPI、小屏滚动、两页布局；切换页面或最小化时停止预览计时器。
2. 使用原生 Nexus 26.9 创建配置；关闭两个文字开关后重启，逐个悬停图标，确认名称和延迟气泡都消失。
3. 恢复原值后确认原有标签、气泡模式恢复，图标顺序和路径不变。
4. 验证 `setup` 打开详细设置；不支持的版本能使用页面中提供的原生操作说明。
5. 仅频谱安装、Nexus 未安装、路径缺失、另一位置的 Nexus 正在运行，以及配置格式未知时均应清楚提示。

此前 Linux 环境只完成 C# 5 / .NET Framework 4.8 引用程序集交叉编译与独立逻辑测试。接手后已在 Windows 11 构建并执行隔离后端、安装和 WPF 渲染测试；真实组件联动、系统 DPI 变化和重启登录需按各自的实测证据记录，不能从隔离测试结果推断。

## 1.2 自启动与动效

- 只写当前用户 `Software\Microsoft\Windows\CurrentVersion\Run` 中的 `AdCDesktop.Spectrum` / `AdCDesktop.Nexus`，值为带双引号的组件绝对路径；限制命令长度为 260。不开总控台、不修改 HKLM、计划任务或其他软件的启动项。Rainmeter 使用安装目录内的便携配置，因此会恢复其中全部启用的皮肤。
- 读取不写入。开启前验证程序存在，关闭仍可清除匹配当前目录的失效启动项。名称被其他路径占用时不覆盖。写后读回校验，失败回退并向 UI 报告；不把失败显示为成功。
- 已识别的原生 `Nexus` / `Rainmeter` Run 项只做提示，不擅自删除；Windows 对启动应用的禁用也不覆盖。入口“系统启动项”打开 `ms-settings:startupapps`。开关代表登记状态，不能承诺系统实际执行。
- `StartupTests.cs` 使用内存存储覆盖独立开关、禁用、中文空格路径、缺失文件、其他安装保护、原生自启提示、失败回退、测试模式零注册表读写等情况。实际重登录启动需在 Windows 验证。
- 按钮 110ms、开关 160ms、切页 180ms；尊重 `SystemParameters.ClientAreaAnimation`。预览缓存网格、曲线控件和渐变，尺寸或颜色变化时才重建。最小化和隐藏频谱页继续暂停计时器。
- Windows 验收补充：验证 Tab/空格开关、反复切页不丢修改、100%/150% DPI、小屏滚动、系统关闭动画；分别启用两个组件、重新登录、分别关闭并再登录，确认互不影响且不会重复运行。验证原生启动项及任务管理器禁用时的提示。

参考：[Microsoft Run / RunOnce](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys)、[Rainmeter 便携配置](https://docs.rainmeter.net/manual/settings/)。
