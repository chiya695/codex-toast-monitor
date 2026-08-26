# Codex Toast Monitor

Codex Toast Monitor is a small Windows utility that watches the notification history for the ChatGPT/Codex desktop app and can forward new completion notifications to a Feishu bot.

## 中文说明

Codex Toast Monitor 是一个运行在 Windows 上的小工具。它读取 ChatGPT/Codex 桌面应用产生的 Windows Toast 通知，并可将新完成通知转发到飞书机器人。

程序不读取屏幕、不注入 ChatGPT 进程，也不修改 Codex 配置。它通过 Windows `UserNotificationListener` 获取通知，只保留 ChatGPT 应用的匹配项，并在本地记录 JSONL 日志。

当前版本已经完成通知捕获、1 秒轮询、飞书 HTTPS Webhook、签名校验、本地待发送队列、失败重试、运行日志、30 天自动保留、手动清理和应用界面配置。`NotificationChanged` 在当前桌面环境可能返回 `0x80070490`，程序会记录该错误并继续使用轮询，不影响主流程。

仓库只包含源代码和空配置示例，不包含 Webhook、Secret、通知历史、用户姓名、证书或构建输出。运行时文件保存在当前用户的本地应用数据目录中，且只记录 ChatGPT/Codex 通知和本程序错误。

It does not inspect the screen, attach to the ChatGPT process, or change Codex configuration. The monitor reads Windows Toast notifications through `UserNotificationListener`, filters for the ChatGPT app, and keeps a local JSONL record of what it saw.

## Current status

- ChatGPT/Codex Toast capture works on Windows 11.
- A one-second local poll is used as the reliable path. The foreground `NotificationChanged` event is also attempted, but may return `0x80070490` on desktop installations; that failure is logged and does not stop polling.
- Feishu custom bot delivery supports HTTPS Webhooks, signed requests, a local outbox, and retries.
- The monitor window includes Feishu settings, runtime status, a log table, full-record inspection, 30-day retention cleanup, and immediate log deletion.

## Privacy and data handling

The repository contains source code and an empty configuration example only. It does not contain a Webhook URL, Feishu Secret, notification history, Windows user name, certificate, or build output.

At runtime, data is kept under the current user's local application data directory:

- `config.json` stores the Feishu settings entered in the program.
- `toast-events.jsonl` stores matching ChatGPT notifications and monitor errors.
- `feishu-outbox` holds notifications waiting for delivery.

The program records ChatGPT/Codex notifications only. It does not copy the complete Windows Notification Center database or notifications from unrelated apps.

## Build

Requirements:

- Windows 10 19041 or newer
- .NET 9 SDK
- A Windows SDK reference package restored by NuGet

Build from the repository root:

```powershell
dotnet restore .\ToastProbe\ToastProbe.csproj
dotnet build .\ToastProbe\ToastProbe.csproj
dotnet publish .\ToastProbe\ToastProbe.csproj -c Release --self-contained false -o .\ToastProbe\publish
```

The app requires package identity and the `userNotificationListener` capability to read notifications. During local development, register the reviewed MSIX package and enable Windows Developer Mode as described by the Windows notification listener documentation. Do not commit the generated package directory or signing certificate.

## Feishu setup

Open Codex Toast Monitor and enter the Feishu custom bot HTTPS Webhook and optional signing Secret. The switch is off by default. Saving an invalid or non-HTTPS address does not start network delivery.

Messages use separate sections for readability:

```text
[ChatGPT通知]

提问：
...

回答：
...
```

## Limitations

The monitor reports notifications that Windows exposes through the listener. It is not a full task-state API and cannot infer progress that never produces a Toast. Notification history retention and Windows notification settings can still affect what is available to read.

## English notes

The utility is intentionally conservative: it observes Windows notifications, filters the ChatGPT app, and sends only when Feishu delivery is explicitly enabled. Local logs are automatically trimmed to 30 days, while the UI also provides explicit cleanup and deletion controls.

The `NotificationChanged` event is not dependable in the current packaged WinForms environment, so the one-second poll remains the production path for this version. A future background-task implementation can be evaluated separately without touching the Codex installation.
