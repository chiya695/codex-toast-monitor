# Codex Toast Probe

See the repository root [README.md](../README.md) for the project overview, build instructions, privacy notes, and Feishu setup.

The monitor uses a one-second local poll as its reliable fallback. It records captured notifications and runtime errors in `%LOCALAPPDATA%\CodexToastProbe\toast-events.jsonl`. The program window provides a log table, full-record inspection, cleanup of records older than 30 days, and immediate deletion of this log. These actions never delete Windows Notification Center data or the Feishu outbox.

The app must have package identity and the `userNotificationListener` capability. Do not register the package until the build has been reviewed. The first run should be used only for controlled notification tests.

Optional Feishu delivery is disabled by default. Configure it from the program window, or place an equivalent file at `%LOCALAPPDATA%\CodexToastProbe\config.json` with an HTTPS Feishu bot webhook before setting `Enabled` to `true`. When enabled, events are first written to the local `feishu-outbox` directory and are retried until delivery succeeds. No Webhook URL means no network requests.

The notification event subscription may return `0x80070490` on this Windows desktop setup. That failure is logged and does not stop the one-second polling fallback.
