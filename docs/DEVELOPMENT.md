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
| `SpectrumBackend.cs` | 读取配置、校验、原子更新、备份和 Rainmeter 联动 |
| `app.manifest` | 普通用户权限和 DPI 声明 |

程序默认从自身所在 `SpectrumControl` 目录的父目录寻找 Rainmeter，也支持：

```powershell
& .\artifacts\control-panel\AdCDesktop.exe --root 'E:\My desktop tools'
```

若找不到配置，普通界面会提示选择安装目录。不会回退到作者电脑的盘符或账户路径。

## 写入范围

控制台只修改指定根目录下：

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

`tests/.artifacts` 是所有测试写入位置，不进入 Git。测试覆盖颜色和数值校验、编码和换行保留、事务失败回退、Alpha 原值保留、悬停模式、个人方案、显示开关与恢复范围。

安装测试使用无效的假程序验证离线流程不会执行组件，覆盖中文和空格目录、Dome 私有副本、重复安装保留修改及模式切换保护。启动预检测试用模拟进程检查冲突、同目录复用和缺失组件，避免在检查失败前更改外观。

真实桌面联动与隔离测试分开。CI 不发送 Rainmeter 控制命令、不启动第三方安装器、不更改 Explorer、任务栏或用户壁纸。

### 离屏界面检查

```powershell
& .\artifacts\control-panel\AdCDesktop.exe --screenshot `
  "$PWD\artifacts\panel.png" --root 'E:\FixtureRoot'
```

FixtureRoot 至少应有真实布局的三份配置，测试脚本会生成它们。`--smoke-test <fixtureRoot> <result.json>` 会修改所提供的样本目录，构造的后端禁用运行时集成；只能对测试样本使用。

### 值的保留

`AlphaValue=178` 显示为约 70%，但没有调整透明度时仍保留 178，不能往返变为 179。Rainmeter 的 `OnHover` 分别为 0=不变、1=隐藏、2=淡入、3=淡出；鼠标穿透开启时不会触发悬停效果。

## 更新外部依赖

先检查官方发布和适用许可，独立下载并计算新 SHA-256，再更新 `config/dependencies.json` 和 `docs/DEPENDENCIES.md`。不要用未经验证的新文件替换旧版本校验值，也不要把下载包提交进仓库。
