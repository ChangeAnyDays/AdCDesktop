# 发布到 GitHub

## 上传前

```powershell
.\scripts\Test-Publish.ps1
.\tests\Test-Scripts.ps1
.\tests\Test-SkinData.ps1
.\tests\Test-Install.ps1
.\tests\Test-StartupPreflight.ps1
.\tests\Test-PublishGate.ps1
.\scripts\Build-ControlPanel.ps1
.\tests\Test-ControlPanel.ps1 -SkipBuild
```

公开内容应只有项目源码、脚本、测试、原创图标、纯控制台截图和文档。以下内容保留在本机或安装缓存中：

- 第三方软件和安装器。
- Rainmeter 第三方皮肤、字体、附带图片及原归档。
- Wallpaper Engine、Workshop 的 `.pkg`、视频、预览 GIF 和包含壁纸的全桌面截图。
- 个人快捷方式、注册表导出、备份、账户标识、日志及真实配置。

## 使用 GitHub Desktop

1. 在 GitHub 创建一个空仓库，或在 GitHub Desktop 选择添加本地项目。
2. 提交本项目文件，检查差异中没有 `artifacts`、`tests/.artifacts` 或下载内容。
3. 点击 **Publish repository**，选择自己的账户和可见范围。

## 使用 Git 命令

若目录尚未初始化 Git，先运行 `git init -b main`，然后：

```powershell
git add .
git diff --cached --check
git commit -m "Initial AdCDesktop source release"
git remote add origin https://github.com/YOUR_ACCOUNT/AdCDesktop.git
git push -u origin main
```

`YOUR_ACCOUNT` 需要替换为自己的 GitHub 账户。GitHub 访问验证通过 Git 或 GitHub Desktop 完成，不要把令牌、密码写到脚本、URL 或配置中。

## 构建产物

GitHub Actions 的 Windows 工作流会上传 `artifacts/control-panel` 作为构建产物。它包含原创控制台及运行配置，不含 Rainmeter、Nexus、TranslucentTB、第三方皮肤或壁纸。

需要 GitHub Release 时，可下载该构建产物，将其作为原创控制台附件；完整桌面仍通过本项目的安装脚本获取外部依赖。发布说明应注明支持的 Windows 版本和用户需自备 Wallpaper Engine 的条件。
