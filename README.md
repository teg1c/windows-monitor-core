# 窗巡 Window Sentinel

窗巡是一款面向 Windows 桌面场景的窗口监听、文字识别和任务栏闪烁提醒工具。它可以持续监听窗口标题、桌面或指定窗口中的文字内容，以及任务栏软件闪烁状态；当规则命中关键词或目标状态后，通过系统通知或网络回调发送提醒。

适用场景包括客服消息提醒、订单异常监听、业务系统告警、运维状态观察、网页或桌面软件关键字监控等。

## 主要功能

- 窗口标题监听：监听所有可见窗口标题，按关键词命中规则。
- 文字识别监听：支持整个桌面、指定窗口、整图识别或框选区域识别。
- 任务栏闪烁监听：从运行中的软件中选择目标，检测任务栏闪烁并提醒。
- 规则管理：每条规则独立配置类型、目标、关键词、冷却时间、连续发送次数。
- 通知配置：每条规则可自定义系统通知内容和网络回调地址、请求头、请求体。
- 授权机制：客户端手动输入授权码，本地校验机器码和过期时间，再做远程校验。
- 在线更新：支持从 GitHub Releases 检测和下载最新版本。
- 远程授权服务：`service-api` 目录提供 Go 语言实现的授权校验服务示例。

## 技术栈

- 客户端：C#、WinForms、AntdUI
- OCR：Windows OCR
- 数据存储：SQLite
- 远程授权服务：Go
- 发布更新：GitHub Releases

## 仓库说明

源码仓库：

```text
git@github.com:teg1c/windows-monitor-core.git
```

发布仓库：

```text
git@github.com:teg1c/windows-monitor-release.git
```

发布仓库只放软件包和版本文件，不放源码。

## 目录结构

```text
src/
  WindowsMonitor.App/             客户端界面和主程序
  WindowsMonitor.Core/            核心模型、规则匹配和接口
  WindowsMonitor.Infrastructure/  OCR、窗口枚举、授权、更新、SQLite 等实现
  WindowsMonitor.Tests/           单元测试
  WindowsMonitor.Updater/         更新辅助程序
service-api/                      Go 远程授权校验服务
docs/                             产品、技术、授权协议和发布说明
tools/                            授权码生成等辅助脚本
dist/                             构建输出目录，已忽略
artifacts/                        临时产物目录，已忽略
```

## 本地开发

环境要求：

- Windows 10 或更新版本
- .NET 8 SDK 或更新版本
- Go 1.22 或更新版本，用于远程授权服务

还原和构建：

```powershell
dotnet restore WindowsMonitor.slnx
dotnet build WindowsMonitor.slnx -c Debug
```

运行测试：

```powershell
dotnet test WindowsMonitor.slnx -c Debug
```

运行 Go 授权服务：

```powershell
cd service-api
go run .
```

默认服务地址是：

```text
http://127.0.0.1:8081/license
```

## 使用说明

### 1. 授权

打开软件后进入“授权管理”页面，复制机器码，然后使用授权码生成脚本生成授权码。

测试授权码示例：

```powershell
.\tools\new-license-code.ps1 -MachineCode "你的机器码" -LicenseType yearly
```

把生成的 `WML1.` 开头授权码粘贴到客户端，点击“激活”。

授权校验逻辑：

- 客户端先本地解密授权码，校验机器码和过期时间。
- 本地校验通过后，再请求远程授权服务。
- 远程服务可返回吊销、无效、过期等状态。
- 如果远程服务不可访问，客户端跳过远程校验，不影响本地有效授权使用。
- 授权过期或无效时，监听功能会锁定。

远程授权服务配置由打包时写入：

```powershell
.\build.ps1 -Version 0.1.0 -LicenseValidationUrl "http://127.0.0.1:8081/license"
```

更多授权协议细节见 [docs/license-protocol.md](docs/license-protocol.md)。

### 2. 添加规则

进入“规则”页面，点击“新增规则”。

规则类型：

- 窗口标题：监听所有窗口标题，可按进程名和标题过滤。
- 文字识别：选择整个桌面或指定窗口，可整图识别，也可预览后框选区域。
- 任务栏闪烁：从运行中的软件下拉列表选择目标软件。

每条规则都可以配置：

- 关键词
- 冷却秒数
- 连续发送上限
- 是否启用
- 系统通知内容
- 网络回调地址、请求头、请求体

规则列表支持：

- 双击编辑
- 右键编辑、删除、复制
- 操作列启用或停用

### 3. 文字识别

文字识别规则支持两种目标：

- 整个桌面
- 指定窗口

选择目标后，可以点击“预览/框选区域”打开预览窗口，然后选择识别区域。如果不框选区域，则识别整个桌面或整个窗口。

### 4. 通知

当前支持两类通知渠道：

- 系统通知：弹出 Windows 系统通知。
- 网络回调：向指定 URL 发送 HTTP POST 请求。

通知模板可使用变量：

```text
{RuleName}
{HitType}
{Keyword}
{WindowTitle}
{ProcessName}
{Source}
{Snippet}
{OccurredAt}
```

网络回调请求头必须是 JSON 对象，例如：

```json
{
  "Authorization": "Bearer token"
}
```

网络回调请求体可以是 JSON，也可以是普通文本模板。

### 5. 在线更新

进入“软件更新”页面，默认检查：

```text
teg1c/windows-monitor-release
```

点击“检测最新版本”会读取 GitHub Releases 最新版本；点击“下载最新版本”会下载最新 zip 包，并在存在 sha256 文件时校验完整性。

## 构建软件

普通构建：

```powershell
.\build.ps1 -Version 0.1.0
```

带远程授权服务地址：

```powershell
.\build.ps1 -Version 0.1.0 -LicenseValidationUrl "https://example.com/license"
```

自包含构建：

```powershell
.\build.ps1 -Version 0.1.0 -SelfContained
```

构建产物会输出到 `dist`：

```text
dist/WindowsMonitor/
dist/WindowsMonitor-v0.1.0-win-x64.zip
dist/WindowsMonitor-v0.1.0-win-x64.zip.sha256
dist/WindowsMonitor-v0.1.0-manifest.json
dist/latest.json
```

## 发布新版本

发布脚本会先构建软件，再把软件包复制到发布仓库。发布仓库只包含软件产物，不包含源码。

准备发布产物：

```powershell
.\publish-release.ps1 -Version 0.1.0
```

构建并推送到发布仓库：

```powershell
.\publish-release.ps1 -Version 0.1.0 -Push
```

带远程授权服务地址：

```powershell
.\publish-release.ps1 -Version 0.1.0 -LicenseValidationUrl "https://example.com/license" -Push
```

如果已经安装并登录 GitHub CLI，可以同时创建 GitHub Release：

```powershell
.\publish-release.ps1 -Version 0.1.0 -Push -CreateGitHubRelease
```

没有 GitHub CLI 时，可以手动到 `teg1c/windows-monitor-release` 创建 Release，标签使用同版本号，例如：

```text
v0.1.0
```

然后上传这些文件：

```text
WindowsMonitor-v0.1.0-win-x64.zip
WindowsMonitor-v0.1.0-win-x64.zip.sha256
WindowsMonitor-v0.1.0-manifest.json
```

详细发布流程见 [docs/release-process.md](docs/release-process.md)。

## 远程授权服务

远程授权服务代码在 [service-api](service-api)。

启动示例：

```powershell
cd service-api
$env:LICENSE_ADDR=":8081"
$env:LICENSE_CRYPTO_KEY_BASE64="MDEyMzQ1Njc4OUFCQ0RFRjAxMjM0NTY3ODlBQkNERUY="
go run .
```

吊销授权示例：

```powershell
$env:LICENSE_REVOKED_IDS="LIC-202606110001,LIC-202606110002"
go run .
```

客户端请求 `/license`，服务端返回加密响应。每次响应都会重新加密，客户端会校验响应随机数，避免伪造固定返回。

更多服务说明见 [service-api/README.md](service-api/README.md)。

## 常用命令

```powershell
# 构建
dotnet build WindowsMonitor.slnx -c Debug

# 测试
dotnet test WindowsMonitor.slnx -c Debug

# Go 授权服务测试
cd service-api
go test ./...

# 发布软件包
cd ..
.\publish-release.ps1 -Version 0.1.0 -Push
```

## 联系方式

- 作者：tegic
- 联系方式：35350826
- GitHub：https://github.com/teg1c
