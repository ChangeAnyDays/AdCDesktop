# 第三方软件与素材说明

本仓库的 MIT 许可只适用于本项目原创的控制面板、安装脚本、原创图标及文档。第三方软件、皮肤、字体、图像、音乐和壁纸继续适用各自权利人的条款。

仓库与源码发布包不包含第三方安装器、运行库、皮肤源码、字体包或 Steam Workshop 壁纸资源。安装脚本按固定 URL 获取依赖，并核对 `config/dependencies.json` 中的 SHA-256。公开下载地址和校验值用于重现安装，不代表本项目取得了第三方素材的再分发或商用授权。

## Rainmeter

- 项目：[Rainmeter](https://www.rainmeter.net/)，[源码](https://github.com/rainmeter/rainmeter)。
- 官方网站标明使用 GNU GPL v2 发布；具体条款以 Rainmeter 上游许可为准。
- 本项目安装便携版，由原安装器保留其许可与相关说明，不把 Rainmeter 二进制纳入本仓库的 MIT 许可。
- 控制台通过 Rainmeter 的公开窗口消息和命令接口操作已有皮肤；本仓库未复制 Rainmeter 程序源码。

## TranslucentTB

- 项目：[TranslucentTB](https://github.com/TranslucentTB/TranslucentTB)。
- 上游许可：[GNU GPL v3](https://github.com/TranslucentTB/TranslucentTB/blob/release/LICENSE.md)。
- 本项目使用官方 2026.2 x64 便携包。该便携发行版面向 Windows 11；运行时保留官方包中的许可和依赖文件。
- 本仓库不再分发 TranslucentTB 程序，也不将其许可替换为 MIT。

## Winstep Nexus Free

- 产品：[Winstep Nexus](https://www.winstep.net/nexus.asp)。
- 属于 Winstep 的专有软件，**仅个人使用免费**。官方页面和 26.9 安装包的 ReadMe 均注明，企业或商业环境需要另行取得相应许可。
- Nexus 是可选组件。项目的 MIT 许可不赋予 Nexus 的企业使用权、商业使用权、再许可权或素材再分发权。
- 本仓库仅提供官方版本化下载链接和校验值；原安装器的许可协议与 Windows 管理员确认仍由用户处理。

## Welfare 时钟皮肤

- 时钟文件署名：Morteza Nasirivand。
- [原作者页面](https://www.deviantart.com/nasirivand/art/Welfare-1-3-818243965)。
- 参考安装使用了教程中的[致美化镜像页面](https://zhutix.com/skins/welfare/)所提供的 **Welfare 1.1** 包。原作者页面标题是 1.3，本项目没有将两者视为同一版本。
- 镜像包的 `RMSKIN.ini` 将作者写作镜像站名，时钟源码写作 Morteza Nasirivand；本项目保留这项来源差异，不将镜像站视为时钟原创作者。
- 审核时原作者页面返回 HTTP 403，归档元数据中没有确认到皮肤的再分发许可。因此本仓库不包含该皮肤源码、字体或图像，使用者应查看上游条款；如要把皮肤一并分发或用于商业目的，应先取得相应授权。

## RetrOcOlOr 频谱皮肤

- 套件归档署名：HipHopium；[参考原作者页面](https://www.deviantart.com/apexxx-sensei/art/RetrOcOlOr-792148096)。
- `Visualizer.ini` 中组件作者署名为 **Connect-R**；这项署名也应保留。
- 参考安装来源：[致美化镜像页面](https://zhutix.com/skins/retrocolor/)。镜像包没有可确认的数字版本号，本项目按归档 SHA-256 固定其字节内容。
- 原作者页面在审核时返回 HTTP 403，镜像归档未确认到皮肤再分发许可。因此本仓库不复制频谱的 INI、Lua、字体或图片源码，只在用户安装时获取原归档并提取所需皮肤数据。
- 安装流程跳过附带的 `RainRGB4.exe`、整套布局和壁纸。原创控制面板使用 Windows 自带颜色选择器，不依赖该调色程序。

## Wallpaper Engine 与 Dome 壁纸

- [Wallpaper Engine](https://store.steampowered.com/app/431960/Wallpaper_Engine/)由用户自行拥有并安装，本项目不附带程序、不提供购买账户或激活方式。
- 参考壁纸：[Dome 4k {Artwork by WLOP}](https://steamcommunity.com/sharedfiles/filedetails/?id=3113287126)。
- 原画：WLOP，[原画来源](https://www.artstation.com/artwork/n0q2Wr)；动画发布者：Drioyard。Workshop 项目另注明音乐作者 Mustafa Avşaroğlu，曲目与完整署名以该项目页面为准。
- 本仓库不包含 `scene.pkg`、壁纸预览 GIF、图片、音乐或完整桌面截图。用户通过 Steam Workshop 自行取得壁纸，安装脚本只引用已下载到自己电脑上的文件。
- Workshop 的公开订阅入口不等于把原画、动画或音乐重新发布到 GitHub 的授权。

## 本项目公开的图片

允许纳入仓库的是本项目原创波形图标，以及只展示原创控制面板和示意波形的面板截图。包含第三方壁纸、应用商标、用户个人桌面或账户信息的参考截图不属于本项目的发布素材。

## 更新依赖时

更新版本需同时核对来源、字节内容、校验值与许可变化。授权不明确的素材继续使用外链方式，不应因为已有下载缓存或被其他网站提供下载就直接加入仓库。
