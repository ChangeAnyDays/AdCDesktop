# Nexus 与 ADC 总控台

Nexus 是独立第三方应用。项目不分发它的程序，也不导入作者账户的 Dock 配置。以下步骤对应清单固定的 26.9 版本，菜单语言会随系统设置变化。

1. 启动安装目录 `Nexus/Nexus.exe`，从 Nexus 控制图标打开“首选项 / Preferences”。
2. 在“位置 / Position”选择**屏幕底部、水平居中**，边缘偏移约 **50 像素**。
3. 在“外观 / Appearance”将图标大小设为 **48**；背景透明度设为 **100%**，图标保持不透明。
4. 在“效果 / Effects”开启反射和鼠标悬停放大，可按自己喜欢的程度调整。
5. 在“内容 / Content”或图标右键菜单里移除不需要的 CPU、天气、内存、时钟等默认入口。移除的是 Dock 入口，桌面原文件保留。
6. 把常用软件快捷方式拖到 Dock；建议加入安装目录的 `SpectrumControl/AdCDesktop.exe` 和 `Restore-Desktop.cmd`。
7. 在“一般 / General”按自己的需要管理开机启动。本项目默认不添加启动项；移动软件目录后，原启动项路径也需要相应更新。

推荐使用自己的资源管理器、浏览器、聊天工具和 Steam 快捷方式，不复制别人的应用路径。若已有一套 Nexus 布局，先用其“高级 / Advanced → Backup”导出备份，再自行决定是否调整。

Nexus Free 的个人使用条件见 [官方页面](https://www.winstep.net/nexus.asp)，商业、企业用途需要另行确认许可。

## 在 ADC 总控台中关闭悬停文字

1. 打开总控台的“Nexus 启动栏”页。未检测到程序时，点击“选择 Nexus.exe”。
2. 第一次使用时先运行 Nexus，并在它的首选项里保存一次设置，以建立配置。
3. 从 Nexus 图标的右键菜单退出 Nexus；总控台不会强杀第三方程序。
4. 点击“重新读取”，然后“关闭所有悬停文字”，再点击“启动 Nexus”。
5. 要撤销，先退出 Nexus，点击“恢复原文字设置”，再启动。

Nexus 保存的是当前 Windows 用户共享的配置，因此修改前要求所有 Nexus 进程退出。总控台只编辑已有的 `DockShowLabels` / `DockShowLabels1` 等正整数编号字段 和 `ShowIconTooltips` 值；首次应用把这些原值备份到安装目录的 `SpectrumControl/Backups/nexus-text-original.json`。其他注册表值和 Dock 图标保持不变。

这是基于 Nexus 26.9 已识别配置字段的适配，并非 Winstep 承诺稳定的公开 API。若键缺失或值格式不兼容，总控台不会猜测创建值，也不会报告修改成功。Windows 11 上已确认 26.9 使用 `Docks` 下的 `DockShowLabels1=True`（REG_SZ），修复后可读出该字段。重启后悬停效果及 `setup` 设置窗口响应尚未完整验收，见[测试报告](TEST-REPORT.md)。

## 手动关闭 / 改成较小的文字

- 立即出现的图标名称：首选项 → **外观 / Appearance** → 取消 **鼠标经过时显示图标标签 / Show icon labels on mouse over**。
- 延迟出现的气泡：首选项 → **行为 / Behavior** → **更多选项 / More Options** → 气泡信息提示选择 **从不 / Never**。
- 保留名称但改变样式：外观 → **标签设置 / Label Settings**，调小字体，调整颜色，关闭标签背景和描边。

“打开 Nexus 详细设置”通过原生入口打开独立窗口，不把第三方界面强行嵌入总控台。若当前版本没有响应入口，点击 Dock 的 Nexus 控制图标，或使用默认快捷键 `Ctrl+F11`。

设置位置来源：[官方 Nexus 用户指南](https://themes.winstep.net/help/Nx/nexus.htm) 的 Appearance / Advanced Behavior Settings；[官方支持说明](https://forums.winstep.net/phpBB2/viewtopic.php?p=41916)。
