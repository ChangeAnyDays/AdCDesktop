AdCat 桌面 1.3.2 — Windows 11 更新包

安装：完整解压 ZIP，双击 Install.cmd，选择已有桌面美化的根目录
（里面应有 Rainmeter 文件夹），完成后从桌面“AdCat 桌面”打开。
请先关闭 AdCat 桌面，并把更新包解压到与现有 SpectrumControl 不同的目录。
更新仅替换 SpectrumControl 内的 AdCDesktop.exe、AdCDesktop.exe.config、
QuickStart.html；覆盖前备份并核对 SHA-256，备份位于
SpectrumControl/Backups/ControlCenter-*。写入失败时自动尝试回滚，
如文件被其他程序锁定导致回滚失败，会明确提示备份位置以供手动恢复。
本更新保留皮肤配置、个人方案、Nexus 布局及其他旧版控制台文件。
如需撤销更新：先关闭控制台，再把对应备份中的三个文件复制回 SpectrumControl。
备份未包含的文件代表更新前不存在，需要在撤销时自行移除该新增文件。
桌面已有同名快捷方式会备份为 previous-shortcut.lnk，可复制回桌面恢复。

无人值守更新示例（路径可含中文和空格）：
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-ControlCenter.ps1 -InstallDir "D:\我的桌面工具" -NoShortcut
此命令不创建桌面快捷方式，也不启动程序；加 -Launch 可在更新完成后打开控制台。

首次安装整套频谱：使用 GitHub 源码包的 Install.cmd。
本更新包只包含原创控制台，不包含 Rainmeter、Nexus 或第三方皮肤。

使用：选择配色和参数，看模拟预览，然后点击“应用到桌面”。
预览使用模拟音源，不采集系统音频。桌面实际频谱由 Rainmeter 提供。
顶部两个自启动开关独立、立即保存，下次登录生效。
关闭 Nexus 悬停文字：先从 Nexus 菜单退出，再在 Nexus 分页应用，重新启动。
完整中文教程：control-panel/QuickStart.html，或程序中的“上手教程”。

测试记录见 GitHub docs/TEST-REPORT.md；未验证项目会明确列出。
源码：https://github.com/ChangeAnyDays/AdCDesktop
