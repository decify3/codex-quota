# Codex Quota / Codex 额度球

Windows 桌面额度查看与规划工具：查看剩余额度，把当前周余额按开发日安排，并为重要工作预留额度。

基于 MIT 许可的 Codex Guardian 二次开发，非 OpenAI 官方应用。首个公开测试版本为 **0.1.0-beta.2**。

## 使用

1. 解压完整 Windows x64 目录包，保持所有文件在一起。
2. 先安装并登录官方 Codex，再双击 `CodexGuardian.App.exe`。程序自带运行依赖，无需安装 .NET SDK。
3. 点击小球展开，打开“额度规划”。选择每天的工作强度、总预留，必要时展开某天的“最低预留”。保存后返回小球查看今日建议。
4. 隐藏后通过系统托盘恢复；从托盘退出程序。

今日建议是按**当前剩余额度**动态重新分配的百分点，不是今天累计已用量、固定日限额或官方额度预订。例如 10 个百分点表示整个周额度的 10%，不是当前余额的 10%。短周期限额仍然独立生效。

## 首版范围

- 可识别额度窗口的余额、重置时间和数据状态。
- 日常周预算、每日相对强度、重要日最低分配和总预留。
- 小球展开页的今日动态建议，以及有依据的计划风险提示。
- 本地计划持久化、账号作用域和重置窗口隔离。
- 中文/英文界面、托盘、手动刷新和可关闭的主动提醒。

重置事件预算暂不在首版界面开放；旧实验文件保留，首版小球和通知统一使用日常周计划。桌面宠物、自动更新、安装器和多工具汇总不属于本版。

## 数据与限制

- 通过本机 Codex app-server 请求账号元数据和额度，必要时回退到本地会话文件中的额度事件。短时启动的 Codex 子进程可能使用既有登录状态联网。
- 本工具不保存账号邮箱或登录令牌；账号分区使用元数据哈希。预算和历史默认保存在 `%USERPROFILE%\.codex-quota\data`，可用 `CODEX_QUOTA_HOME` 指定目录。
- 无法确认工作区身份时，仍可查看额度，但限制正式计划保存。只有确认只使用个人账号时，才在设置中启用“仅个人账号”；它不能区分同邮箱下未具名的团队工作区。
- 数据陈旧、窗口过期或读取失败时，不把旧数值当成实时余额，也不自行补满额度。历史不足时不提供确定的耗尽预测。
- 主动通知默认关闭；开启后也受勿扰和使用场景限制，同一风险在一次运行中去重，重启会重置去重。
- Windows x64 便携测试包，未签名；本机验收不代表覆盖全部 Windows 版本、混合 DPI 或团队账号。原生通知不可用时有桌面备用提示。

卸载：先关闭开机启动设置（如曾启用），从托盘退出，再删除解压目录。需要清除个人历史时另外删除自己的数据目录；更新程序无需删除数据。

## 下载与反馈

[下载 Windows x64 测试版](https://github.com/decify3/codex-quota/releases/tag/v0.1.0-beta.2) · [问题反馈](https://github.com/decify3/codex-quota/issues)

下载 ZIP 后可使用 PowerShell `Get-FileHash 文件路径 -Algorithm SHA256` 对照随附 `.sha256` 文件。

## 从源码构建

在 Windows 安装 .NET SDK 8.0.422，在仓库根目录执行：

```powershell
dotnet restore native/CodexGuardian.sln
dotnet build native/src/CodexGuardian.App/CodexGuardian.App.csproj --no-restore -m:1
dotnet test native/tests/CodexGuardian.Tests/CodexGuardian.Tests.csproj
dotnet run --project native/src/CodexGuardian.App/CodexGuardian.App.csproj
# 不读取账号的演示
dotnet run --project native/src/CodexGuardian.App/CodexGuardian.App.csproj -- --demo
# 自带运行依赖的目录包
powershell -File scripts/package-native.ps1 -Version 0.1.0-local
```

`native/` 是 .NET 8 WPF 主线；发布仓库不包含个人开发目录中的研究副本和旧 Electron 原型。版本参数会同步到应用内显示。无账号环境仍可运行核心测试及演示。

## 验证范围与来源

147 项核心回归通过；本机 Windows 11 x64 完成实时接口读取、独立包离线冒烟和演示预算的保存/跨进程恢复。构建目标为 Windows 10 build 19041 及以上，但尚未在 Windows 10 实机、ARM64 或混合 DPI 多屏环境验收。安装包仅提供 x64。

来源与修改范围见 [UPSTREAM](native/UPSTREAM.md)，上游 MIT 原文见 [LICENSE](LICENSE)，实际依赖见 [第三方声明](native/THIRD-PARTY-NOTICES.md)。主要贡献是独立额度球、逐日预算、账号隔离、风险提示及 Windows 商店版兼容改进，不将上游代码宣称为原创。