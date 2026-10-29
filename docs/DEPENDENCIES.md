# 依赖与可重现安装

版本清单位于 [`config/dependencies.json`](../config/dependencies.json)，核对日期为 **2026-10-29**。这里固定的是参考安装使用的版本，并不声称它们永远是最新版本。

## 组件

| 组件 | 固定版本 | 用途 | 获取方式 |
|---|---|---|---|
| Rainmeter | 4.5.28 | 显示桌面时钟、频谱 | 官方安装器，便携模式 |
| Welfare | 1.1 镜像归档 | 时钟皮肤 | 安装时获取第三方镜像归档 |
| RetrOcOlOr | 按 SHA-256 固定的镜像归档 | 波浪频谱皮肤 | 安装时获取第三方镜像归档 |
| TranslucentTB | 2026.2，x64 | 可选透明任务栏 | 官方 Windows 11 便携包 |
| Winstep Nexus Free | 26.9 | 可选应用启动栏 | 官方安装器，需接受原许可 |
| Wallpaper Engine | 用户自备 | 可选动态壁纸 | 用户自行安装并取得 Workshop 壁纸 |

控制面板及频谱不要求拥有 Wallpaper Engine。Nexus 只对个人使用免费，商业或企业使用需另行授权。第三方许可范围见 [`THIRD_PARTY_NOTICES.md`](../THIRD_PARTY_NOTICES.md)。

## 来源与完整性

Rainmeter、TranslucentTB、Nexus 使用各自的官方发布地址。Welfare 和 RetrOcOlOr 使用参考教程所采用的镜像，其原作者链接、镜像来源及未核实的授权状态均在清单中分别记录；镜像不被标为官方发布。

所有下列 SHA-256 都是从参考安装实际使用的文件计算所得。核对时三个程序包的固定链接均返回 HTTP 200，内容长度与记录一致；校验值仍应在每次安装下载完成后验证。若上游更换文件而校验失败，应停止使用该包并重新审核，不能关闭校验后继续。

| 文件 | SHA-256 |
|---|---|
| `Rainmeter-4.5.28.exe` | `DE6E010F8C28882BC28DCD8F319C899F34DFDDEC51E1E4DD4E7A23F68EBFE14D` |
| `TranslucentTB-2026.2-portable-x64.zip` | `0DBE8E0255C20E131CDE536DCD0AE490D45989A7D360E26D0150DFF1922AC420` |
| `Welfare_1.1.zip` | `8973C0ED30C6EE25276E375FA347BB0F02B9429785D9D8BD8EA4CB40734782D7` |
| `Welfare_1.1.rmskin`，内层 | `ADFF7E5DADE14933551D7F03CA0CBFA9F243CAD573B999AC800E6481C276B52B` |
| `RetrOcOlOr_by_hiphopium.zip` | `8CEA6F9264F351C4DF8A58708C6703323FD3A866DE36FAB13194FBCCF938091E` |
| `RetrOcOlOr.rmskin`，内层 | `2BADF9657D725D32066DB9CE746F9C84D6E8BF2997512916418356212F7A9F3E` |
| `nexus-26.9.zip` | `861D8F3FFFD2AF4BC86306371D3A30117EA558E55A0F60F93546F72BC8BFAAAC` |
| `NexusSetup.exe`，内层 | `43D68322A5A0EAE4414950001229CA24A7C5A2E4B01D7052F330BEF71521694D` |

原文件观察到的 Authenticode 状态：Rainmeter 安装器有效，签名者包含 `SignPath Foundation`；TranslucentTB 程序有效，签名者包含 `Charles Milette`；Nexus 的官方 26.9 安装器没有签名。SHA-256 用于固定下载内容，不等同于作者签名或第三方授权。

## 程序安装约定

Rainmeter 采用官方支持的便携安装，程序、皮肤和设置保留在用户选择的安装目录。参考命令形式如下，`/D=` 必须放在末尾：

```powershell
Rainmeter-4.5.28.exe /S /PORTABLE=1 /RESTART=0 /VERSION=64 /D=<安装目录>\Rainmeter
```

官方说明：普通账户运行静默安装仍可能出现 UAC；安装脚本不得跳过或自动批准系统管理员确认。参见 [Rainmeter 便携与静默安装文档](https://docs.rainmeter.net/manual/installing-rainmeter/)。

TranslucentTB 便携包仅用于 Windows 11，保持官方归档中的程序、依赖与许可文件。参见 [2026.2 官方发行说明](https://github.com/TranslucentTB/TranslucentTB/releases/tag/2026.2)。若目标 Windows 更新后发生初始化或显示兼容问题，可省略这一可选组件；项目不依靠重启 Explorer 或改动系统权限来强行启用效果。

Nexus 的版本化官方链接是 `https://www.winstep.net/versions/v26.9/nexus.zip`。安装可产生 Windows 管理员提示，其许可、开始菜单、卸载记录及数据目录由原安装器处理，并非纯便携软件。Nexus 的组件路径可位于用户选择的目录，但不能声称安装器所有数据都一定保留在该目录。

## 皮肤归档处理

两个镜像 ZIP 各包含一个 `.rmskin`，内层归档也按清单中的 SHA-256 进行核对。只使用 `Skins/Welfare/` 和 `Skins/RetrOcOlOr/` 中对应的皮肤数据；不自动导入归档内布局、替换用户壁纸，也不运行附带的可选调色程序。字体只由 Rainmeter 在皮肤资源目录中读取，不作为全局 Windows 字体安装。

这些皮肤文件仅在目标用户安装时写入其本地运行目录，不加入 Git 追踪或项目 Release 资产。皮肤上游许可没有确认前，不把其源码、字体或图片打包为项目的一部分。

## 可选动态壁纸

用户通过 [Steam Workshop 项目 3113287126](https://steamcommunity.com/sharedfiles/filedetails/?id=3113287126)获取 Dome 壁纸。安装流程可根据显式提供的 Wallpaper Engine 路径或 Steam 游戏库位置找到已有程序及用户下载的项目，不读取或发布 Steam 登录信息。

仓库只保存 Workshop 标识和链接，不附带壁纸资源。壁纸不可用、未安装或未订阅时，频谱控制面板仍可单独使用。

## 更新清单

1. 从上游发布页确认版本及文件来源，保留来源链接。
2. 在隔离下载目录取得归档，核对签名与适用平台，再重新计算 SHA-256。
3. 同时核对内层安装器或 `.rmskin`，不要只更新外层文件名。
4. 更新 `config/dependencies.json`，重跑离线脚本与构建测试。
5. 不把下载缓存、用户配置、个人备份、注册表导出或第三方壁纸加入仓库。
