# Codex Toast Monitor

## 项目简介

Codex Toast Monitor 是一个运行在 Windows 上的通知监控工具。它读取 ChatGPT/Codex 桌面应用发出的 Windows Toast 通知，在任务完成后提取通知内容，并按照用户配置转发到飞书机器人。

程序不读取屏幕，不注入 ChatGPT/Codex 进程，也不修改 Codex 的配置文件。通知读取通过 Windows `UserNotificationListener` 完成，只处理 ChatGPT 应用的通知。

## 主要功能

- 捕获 ChatGPT/Codex 桌面应用新增的 Toast 通知。
- 使用 1 秒本地轮询作为稳定的实时捕获方式。
- 将通知内容分成“提问”和“回答”两部分发送到飞书。
- 支持飞书 HTTPS Webhook 和签名 Secret。
- 使用本地待发送队列，网络失败后自动重试。
- 在程序界面中修改飞书配置，并即时启用或停用推送。
- 查看通知、发送结果和运行错误日志。
- 自动清理 30 天以前的日志，也可以手动清理或立即删除全部日志。
- 提供应用图标、运行状态页和日志详情页。

## 工作方式

程序启动后会先读取当前通知列表建立基线，避免把启动前的历史通知重复发送。随后每秒读取一次通知列表，并使用通知 ID 去重。

程序也会尝试订阅 Windows 的 `NotificationChanged` 实时事件。在当前桌面应用环境中，该接口可能返回 `0x80070490`。遇到这种情况时，程序会记录错误并继续使用轮询，不影响主要功能。

需要注意，Windows 通知监听不是 ChatGPT/Codex 的任务状态接口。程序只能处理已经出现在 Windows 通知中的事件，无法得知没有产生 Toast 的运行进度，也无法保证通知被系统提前清理时仍能读取到。

## 隐私与数据安全

仓库只包含源代码、图标、清单和空配置示例，不包含以下内容：

- 飞书 Webhook 地址；
- 飞书签名 Secret；
- ChatGPT/Codex 通知历史；
- Windows 用户名或本机绝对路径；
- 本地签名证书；
- 编译输出、安装包和调试文件。

程序运行数据保存在当前用户的本地应用数据目录中：

```text
%LOCALAPPDATA%\CodexToastProbe\
```

其中：

- `config.json` 保存程序界面中填写的飞书配置；
- `toast-events.jsonl` 保存匹配到的 ChatGPT 通知和程序运行日志；
- `feishu-outbox` 保存暂时发送失败、等待重试的通知。

程序不会记录其他应用的通知，也不会复制完整的 Windows 通知中心数据库。

## 构建要求

- Windows 10 版本 19041 或更高版本；
- .NET 9 SDK；
- 可通过 NuGet 还原 Windows SDK 引用包。

在仓库根目录执行：

```powershell
dotnet restore .\ToastProbe\ToastProbe.csproj
dotnet build .\ToastProbe\ToastProbe.csproj
dotnet publish .\ToastProbe\ToastProbe.csproj -c Release --self-contained false -o .\ToastProbe\publish
```

要读取 Windows 通知，程序必须具有包身份，并在清单中声明 `userNotificationListener` 能力。进行本地开发时，需要按照 Windows 通知监听文档注册经过检查的 MSIX 包；开发者模式只用于本地测试。仓库不会提交生成的安装包目录和证书文件。

## 飞书配置

启动程序后，在“运行状态”页面填写：

1. 勾选“启用飞书推送”；
2. 填写飞书自定义机器人的 HTTPS Webhook 地址；
3. 如果机器人启用了签名校验，填写签名 Secret；
4. 点击“保存配置”。

推送开关默认关闭。Webhook 不是 HTTPS 地址或地址为空时，程序不会启动网络发送。

飞书消息格式如下：

```text
[ChatGPT通知]

提问：
这里是触发通知的提问内容

回答：
这里是 ChatGPT 返回的内容

通知时间：...
通知 ID：...
```

## 日志管理

“运行日志”页面提供以下功能：

- 查看日志时间、类型、通知 ID 和摘要；
- 选中记录后查看完整 JSON 内容；
- 手动刷新日志；
- 删除 30 天以前的日志；
- 删除全部本程序日志。

程序启动时会自动执行 30 天保留策略。删除日志不会影响 Windows 通知，也不会删除飞书待发送队列。

## 当前版本

当前初版已完成并验证以下流程：

- 连续多个 ChatGPT 任务的通知捕获；
- 程序重启后的历史通知去重；
- 飞书签名校验和实际消息投递；
- 网络失败后的本地队列和重试；
- 日志查看、自动保留和手动删除；
- 自适应设置界面和应用图标。

当前版本标签为 `v0.2.0`。

## 后续计划

后续可以在不修改 ChatGPT/Codex 安装文件的前提下，继续完善：

- 开机启动和后台运行选项；
- 更完整的 MSIX 安装和卸载流程；
- Windows 后台通知触发器的独立实验；
- 在本地流程长期稳定后，再评估 CodeIsland 的适配分支。
