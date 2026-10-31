# Windows 11 验证记录 — 1.2.2 测试版

验证日期：2026-10-31（Asia/Shanghai）。Windows 11 家庭中文版 x64，Build 26200；.NET Framework 4.8，Windows PowerShell 5.1；C# 5 编译器。本文区分隔离自动测试、真实桌面操作和仍未完成的验收项。

## 本版修复

### 首次安装失败（用户截图）

用户从源码目录运行 `Install.cmd` 时，`Install.ps1` 报告：

```text
在此对象上找不到属性“Count”。请确认该属性存在。
FullyQualifiedErrorId : PropertyNotFoundStrict,Install.ps1
```

根因不在 `Install.ps1` 本身，而在它导入的 `scripts/AdCDesktop.Common.psm1`：`Install-SkinData` 把 `if` 块里的单元素 `@(...)` 结果赋给 `$entry`，PowerShell 会把单元素数组解包成标量；模块启用了 `Set-StrictMode -Version 2`，随后读取 `$entry.Count` 就抛出 `PropertyNotFoundException`。第一次安装需要解包 Welfare / RetrOcOlOr 皮肤时必然触发，离线夹具因为预置了皮肤而绕过了该分支。

修复：把结果重新包成数组并读取 `$entries.Count` / `$entries[0]`，不再依赖 PowerShell 的数组解包行为。新增 `tests/Test-SkinData.ps1`，覆盖单个 `.rmskin`、白名单后缀过滤、内层归档缓存和 `innerArchive` 通配回退；修复前该测试稳定复现 `PropertyNotFoundStrict`，修复后通过。

### 总控台

- 底部“恢复原设置 / 重新读取 / 应用到桌面”改为固定底栏，默认窗口高度在屏幕允许时提升到 960；1080p 下打开即可看到主操作，不再需要先滚动到底部。
- 频谱状态区分“未运行 / 未加载 / 已隐藏 / 运行中”；宽度滑块上限与后端 4096 校验一致。
- 从个人方案或桌面读取设置后清空临时基准，后续应用不会继续沿用旧方案的隐藏值。
- Nexus 路径文件失效时回退到本地/常见安装路径，避免启动项使用旧路径。
- Nexus 已运行时，点击“打开 Nexus 详细设置”只提示使用 Dock 原生菜单，不再另开一个隐藏实例；Nexus 未运行时，原生 `setup` 入口实际打开了“首选项”窗口并保留 Dock。

## 已执行的自动验证

以下九项在 Windows PowerShell 5.1 上运行，退出码全部为 0。关键结果、UI 截图和 `results.json` 保存在本机 `artifacts/verification/local-1.2.2/`，不提交个人运行状态。

| 检查 | 结果 |
|---|---|
| 发布文件扫描 | 50 个预期源码文件，无检测到的个人路径、凭据或第三方运行时资源 |
| 发布门禁回归 | 9 组通过 |
| PowerShell 语法与安装 WhatIf | 15 个脚本通过 |
| 皮肤归档解包（新增） | 单元素 `.rmskin` 在 StrictMode 下不再触发 Count 失败；仅解包白名单皮肤文件 |
| 启动预检 | 冲突、复用、缺失组件等隔离检查通过 |
| 离线安装回归 | 假组件、Unicode/空格路径、配置保留及模式保护通过 |
| Windows 控制台编译 | .NET Framework 4.8 / C# 5 通过，文件版本 1.2.2.0 |
| 控制台测试 | 72 后端、45 总控台、27 自启动、313 WPF 检查通过 |
| 更新安装器 | 真实 EXE 部署、重复更新备份、特殊路径、锁文件失败回退通过 |

另做了一次完整的首次安装夹具：用假的 Rainmeter 程序和控制台、缓存中的 Welfare / RetrOcOlOr 归档运行完整 `Install.ps1`，不再出现 Count 错误，皮肤和清单均生成成功；日志见 `artifacts/verification/local-1.2.2/fresh-install.log`。它验证安装编排和解包，不代表真实下载或第三方安装器已在本轮重跑。

WPF 测试实际创建并显示离屏窗口，覆盖两页 × 两尺寸（1080×840、940×650）× 四种内容缩放（100/125/150/200%），共 16 场景、32 张截图；另新增默认窗口尺寸下底栏主按钮无需滚动即可见的断言。**内容 ScaleTransform 不等于切换系统 DPI**。

## 已执行的真实桌面操作

- 将 1.2.2 控制台通过 `Install-ControlCenter.ps1` 更新到 `D:\DesktopStyle\SpectrumControl`，从桌面快捷方式实际打开；默认 960 高度下底部主操作可见。
- 实际启动 Rainmeter 和 Nexus；频谱状态显示“频谱运行中”，Dock 可见。
- 通过总控台开关真实开启、再关闭 `HKCU\...\Run\AdCDesktop.Spectrum`，每步读回；最后恢复为关闭，未留下 ADC 启动项。
- 在 Nexus 已运行时点击详细设置，确认只显示原生菜单提示；在 Nexus 未运行时点击，实际打开“首选项”窗口，未导入或覆盖布局。
- 未修改皮肤、壁纸、Nexus 图标顺序或个人频谱方案。

## 尚未完成

- 真实 Rainmeter 系统音频响应、参数应用后效果与恢复。
- Nexus 关闭标签/延迟气泡后的多个图标逐个悬停、恢复原值和布局保留。
- Windows 系统 DPI 切换、多显示器、实际小屏窗口操作。
- 重新登录或重启后两个自启动独立生效、关闭后不启动、原生自启冲突和重复实例。
- Dock 多次消失的可复现原因和持续稳定性；本版没有重装 Nexus 或覆盖布局。

频谱预览仍是明确标注的模拟音源，不采集系统音频。安装更新包只包含原创控制台与中文教程，第三方程序及皮肤继续由原安装流程准备。

## GitHub CI

本次修复提交 `fd9d333653360d73cd17b4cb4f088d848faf1bc3` 的 Windows CI 已成功：[Windows 构建与验证记录](https://github.com/ChangeAnyDays/AdCDesktop/actions/runs/41180503636)。上一版程序提交 `18df2891d4dee7ca237789598234dba333342225` 的历史记录为：[上一版 CI](https://github.com/ChangeAnyDays/AdCDesktop/actions/runs/41016881621)。自动测试不能替代以上未完成的实机验收。
