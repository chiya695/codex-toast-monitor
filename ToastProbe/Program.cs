using System.Text;
using System.Text.Json;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace CodexToastProbe;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.Run(new MainForm());
    }
}

internal sealed class MainForm : Form
{
    private const string ChatGptAumid = "OpenAI.Codex_2p2nqsd0c76g0!App";
    private const int LogRetentionDays = 30;
    private readonly UserNotificationListener _listener = UserNotificationListener.Current;
    private readonly HashSet<uint> _knownNotificationIds = [];
    private readonly string _eventLogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexToastProbe", "toast-events.jsonl");
    private readonly string _configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexToastProbe", "config.json");
    private readonly ProbeConfig _config;
    private readonly FeishuDeliveryQueue _deliveryQueue;
    private readonly System.Windows.Forms.Timer _pollTimer = new() { Interval = 1000 };
    private readonly SemaphoreSlim _captureGate = new(1, 1);
    private readonly SemaphoreSlim _logGate = new(1, 1);
    private readonly CheckBox _feishuEnabled = new() { AutoSize = true, Text = "启用飞书推送" };
    private readonly CheckBox _startWithWindows = new() { AutoSize = true, Text = "开机自动启动" };
    private readonly TextBox _webhookUrl = new();
    private readonly TextBox _secret = new() { UseSystemPasswordChar = true };
    private readonly Button _saveConfig = new() { Text = "保存配置", AutoSize = true };
    private readonly Label _status = new();
    private readonly Label _logPath = new();
    private readonly Label _logSummary = new();
    private readonly DataGridView _logGrid = new();
    private readonly TextBox _logDetail = new();
    private readonly Button _refreshLogs = new() { Text = "刷新日志", AutoSize = true };
    private readonly Button _cleanLogs = new() { Text = "清理 30 天前", AutoSize = true };
    private readonly Button _deleteLogs = new() { Text = "删除全部日志", AutoSize = true };
    private readonly NotifyIcon _trayIcon;
    private IReadOnlyList<LogEntry> _logEntries = [];
    private bool _subscribed;
    private bool _allowExit;
    private bool _isInTray;

    public MainForm()
    {
        _config = ProbeConfig.Load(_configPath);
        _deliveryQueue = new FeishuDeliveryQueue(_config.Feishu, _eventLogPath, AppendRecordAsync, ReportDeliveryStatus);
        Text = "Codex Toast Monitor";
        Width = 900;
        Height = 650;
        MinimumSize = new Size(760, 520);
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        MinimizeBox = true;
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "CodexToastMonitor.ico");
        Icon = new Icon(iconPath);

        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("打开", null, (_, _) => RestoreFromTray());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("退出", null, (_, _) => ExitApplication());
        _trayIcon = new NotifyIcon
        {
            Icon = new Icon(iconPath),
            Text = "Codex Toast Monitor",
            ContextMenuStrip = trayMenu,
            Visible = false
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();

        _webhookUrl.PlaceholderText = "https://open.feishu.cn/open-apis/bot/v2/hook/...";
        _webhookUrl.Dock = DockStyle.Fill;
        _secret.Dock = DockStyle.Fill;
        _feishuEnabled.Checked = _config.Feishu.Enabled;
        _startWithWindows.Checked = _config.StartWithWindows || StartupManager.IsEnabled();
        _webhookUrl.Text = _config.Feishu.WebhookUrl;
        _secret.Text = _config.Feishu.Secret;
        _saveConfig.Click += (_, _) => SaveConfig();
        _startWithWindows.CheckedChanged += (_, _) =>
        {
            if (IsHandleCreated)
            {
                SaveConfig();
            }
        };

        _logGrid.Dock = DockStyle.Fill;
        _logGrid.ReadOnly = true;
        _logGrid.AllowUserToAddRows = false;
        _logGrid.AllowUserToDeleteRows = false;
        _logGrid.AllowUserToResizeRows = false;
        _logGrid.MultiSelect = false;
        _logGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _logGrid.RowHeadersVisible = false;
        _logGrid.AutoGenerateColumns = false;
        _logGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "time", HeaderText = "时间", Width = 165 });
        _logGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "kind", HeaderText = "类型", Width = 150 });
        _logGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "id", HeaderText = "通知 ID", Width = 90 });
        _logGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "summary", HeaderText = "摘要", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _logGrid.SelectionChanged += (_, _) => ShowSelectedLog();
        _logDetail.Dock = DockStyle.Fill;
        _logDetail.Multiline = true;
        _logDetail.ReadOnly = true;
        _logDetail.ScrollBars = ScrollBars.Both;
        _logDetail.Font = new Font(FontFamily.GenericMonospace, 9F);

        _refreshLogs.Click += (_, _) => ReloadLogs();
        _cleanLogs.Click += (_, _) => CleanOldLogs();
        _deleteLogs.Click += (_, _) => DeleteAllLogs();
        Application.ThreadException += OnThreadException;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildMonitorPage());
        tabs.TabPages.Add(BuildLogsPage());
        Controls.Add(tabs);

        Shown += async (_, _) => await StartAsync();
        FormClosing += OnFormClosing;
        Resize += OnResize;
        _pollTimer.Tick += async (_, _) => await CaptureAddedAsync();
    }

    private TabPage BuildMonitorPage()
    {
        var page = new TabPage("运行状态") { Padding = new Padding(12) };
        var settings = new GroupBox { Text = "程序设置", Dock = DockStyle.Top, Height = 205 };
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 3, RowCount = 5 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.Controls.Add(_feishuEnabled, 0, 0);
        table.SetColumnSpan(_feishuEnabled, 3);
        table.Controls.Add(CreateRightLabel("Webhook 地址"), 0, 1);
        table.Controls.Add(_webhookUrl, 1, 1);
        table.Controls.Add(_saveConfig, 2, 1);
        table.Controls.Add(CreateRightLabel("签名 Secret"), 0, 2);
        table.Controls.Add(_secret, 1, 2);
        table.Controls.Add(_startWithWindows, 0, 3);
        table.SetColumnSpan(_startWithWindows, 3);
        var note = new Label { Text = "仅 HTTPS 地址会启用网络发送；Secret 只保存在本机配置文件中。保存后立即应用。程序只记录 ChatGPT/Codex 通知。", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        table.Controls.Add(note, 0, 4);
        table.SetColumnSpan(note, 3);
        settings.Controls.Add(table);

        var state = new GroupBox { Text = "监听状态", Dock = DockStyle.Fill, Padding = new Padding(10) };
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.TopLeft;
        _status.Padding = new Padding(4);
        _status.Text = "正在启动...";
        state.Controls.Add(_status);
        _logPath.Dock = DockStyle.Bottom;
        _logPath.Height = 34;
        _logPath.AutoEllipsis = true;
        _logPath.Text = $"日志文件：{_eventLogPath}";

        page.Controls.Add(state);
        page.Controls.Add(_logPath);
        page.Controls.Add(settings);
        return page;
    }

    private TabPage BuildLogsPage()
    {
        var page = new TabPage("运行日志") { Padding = new Padding(10) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
        toolbar.Controls.Add(_refreshLogs);
        toolbar.Controls.Add(_cleanLogs);
        toolbar.Controls.Add(_deleteLogs);
        _logSummary.Text = "日志默认保留 30 天；删除操作只处理本程序日志，不影响飞书待发送队列。";
        _logSummary.AutoSize = true;
        _logSummary.Margin = new Padding(18, 8, 0, 0);
        toolbar.Controls.Add(_logSummary);
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(_logGrid, 0, 1);
        layout.Controls.Add(_logDetail, 0, 2);
        page.Controls.Add(layout);
        return page;
    }

    private static Label CreateRightLabel(string text) => new() { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, AutoSize = false, Margin = new Padding(0, 3, 8, 3) };

    private void SaveConfig()
    {
        _config.Feishu.Enabled = _feishuEnabled.Checked;
        _config.Feishu.WebhookUrl = _webhookUrl.Text.Trim();
        _config.Feishu.Secret = _secret.Text;
        _config.StartWithWindows = _startWithWindows.Checked;
        try
        {
            _config.Save(_configPath);
            StartupManager.SetEnabled(_startWithWindows.Checked);
            _deliveryQueue.Start();
            _status.Text = $"配置已保存并应用；{GetFeishuStatus()}";
        }
        catch (Exception ex)
        {
            _status.Text = $"配置保存失败：{ex.Message}";
            _ = AppendRecordAsync(new { observedAtUtc = DateTimeOffset.UtcNow, kind = "config-save-error", error = ex.ToString() });
        }
    }

    private async Task StartAsync()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_eventLogPath)!);
            await SynchronizeStartupSettingAsync();
            _deliveryQueue.Start();
            await ApplyLogRetentionAsync();
            var access = _listener.GetAccessStatus();
            if (access != UserNotificationListenerAccessStatus.Allowed)
            {
                access = await _listener.RequestAccessAsync();
            }
            if (access != UserNotificationListenerAccessStatus.Allowed)
            {
                _status.Text = $"当前通知监听权限：{access}\r\n请在 Windows 设置中允许后重新启动。";
                await AppendRecordAsync(new { observedAtUtc = DateTimeOffset.UtcNow, kind = "permission-error", error = access.ToString() });
                return;
            }

            await EstablishBaselineAsync();
            try
            {
                _listener.NotificationChanged += OnNotificationChanged;
                _subscribed = true;
            }
            catch (Exception ex)
            {
                await AppendRecordAsync(new { observedAtUtc = DateTimeOffset.UtcNow, kind = "subscription-error", error = ex.ToString() });
            }

            _pollTimer.Start();
            _status.Text = _subscribed ? $"实时事件与 1 秒轮询均已启动；{GetFeishuStatus()}" : $"实时事件不可用，正在使用 1 秒本地轮询；{GetFeishuStatus()}";
            ReloadLogs();
        }
        catch (Exception ex)
        {
            _status.Text = $"启动失败：{ex.GetType().Name} {ex.Message}";
            await AppendRecordAsync(new { observedAtUtc = DateTimeOffset.UtcNow, kind = "probe-error", error = ex.ToString() });
            ReloadLogs();
        }
    }

    private async Task SynchronizeStartupSettingAsync()
    {
        try
        {
            _config.StartWithWindows = _startWithWindows.Checked;
            _config.Save(_configPath);
            StartupManager.SetEnabled(_config.StartWithWindows);
        }
        catch (Exception ex)
        {
            await AppendRecordAsync(new
            {
                observedAtUtc = DateTimeOffset.UtcNow,
                kind = "startup-sync-error",
                error = ex.ToString()
            });
        }
    }

    private string GetFeishuStatus() => !_config.Feishu.Enabled ? "飞书推送未启用" : _deliveryQueue.IsEnabled ? "飞书推送已启用" : "飞书配置无效，推送未启动";

    private void ReportDeliveryStatus(string message, bool isError)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(() =>
        {
            _status.Text = message;
            _status.ForeColor = isError ? Color.Firebrick : SystemColors.ControlText;
            ReloadLogs();
        });
    }

    private async Task EstablishBaselineAsync()
    {
        var notifications = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
        foreach (var notification in notifications) _knownNotificationIds.Add(notification.Id);
        await AppendRecordAsync(new { observedAtUtc = DateTimeOffset.UtcNow, kind = "baseline", count = notifications.Count, matchedCount = notifications.Count(n => n.AppInfo.AppUserModelId == ChatGptAumid) });
    }

    private async Task ApplyLogRetentionAsync()
    {
        _logGate.Wait();
        int removed;
        try
        {
            removed = LogStore.RemoveOlderThan(_eventLogPath, DateTimeOffset.UtcNow.AddDays(-LogRetentionDays));
        }
        finally
        {
            _logGate.Release();
        }

        if (removed > 0)
        {
            await AppendRecordAsync(new { observedAtUtc = DateTimeOffset.UtcNow, kind = "log-retention", removed, retentionDays = LogRetentionDays });
        }
    }

    private void OnNotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        if (args.ChangeKind != UserNotificationChangedKind.Added || IsDisposed || !IsHandleCreated) return;
        _ = BeginInvoke(async () => await CaptureAddedAsync());
    }

    private async Task CaptureAddedAsync()
    {
        if (!await _captureGate.WaitAsync(0)) return;
        try
        {
            var notifications = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            var capturedAny = false;
            foreach (var notification in notifications)
            {
                if (notification.AppInfo.AppUserModelId != ChatGptAumid || _knownNotificationIds.Contains(notification.Id)) continue;
                var toastEvent = new ToastEvent(notification.Id, notification.CreationTime, notification.AppInfo.AppUserModelId, notification.AppInfo.DisplayInfo.DisplayName, ExtractTexts(notification), DateTimeOffset.UtcNow);
                await AppendRecordAsync(new { observedAtUtc = toastEvent.ObservedAtUtc, kind = "added", id = toastEvent.Id, creationTime = toastEvent.CreationTime, appUserModelId = toastEvent.AppUserModelId, displayName = toastEvent.DisplayName, texts = toastEvent.Texts });
                _knownNotificationIds.Add(notification.Id);
                await _deliveryQueue.EnqueueAsync(toastEvent);
                _status.Text = $"已捕获通知 ID {notification.Id}，{GetFeishuStatus()}";
                _status.ForeColor = SystemColors.ControlText;
                capturedAny = true;
            }
            if (capturedAny) ReloadLogs();
        }
        catch (Exception ex)
        {
            _status.Text = $"捕获通知失败：{ex.Message}";
            await AppendRecordAsync(new { observedAtUtc = DateTimeOffset.UtcNow, kind = "capture-error", error = ex.ToString() });
            ReloadLogs();
        }
        finally { _captureGate.Release(); }
    }

    private static string[] ExtractTexts(UserNotification notification)
    {
        var binding = notification.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
        return binding is null ? [] : binding.GetTextElements().Select(element => element.Text).ToArray();
    }

    private async Task AppendRecordAsync(object record)
    {
        await _logGate.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_eventLogPath)!);
            await File.AppendAllTextAsync(_eventLogPath, LogStore.SerializeRecord(record) + Environment.NewLine, Encoding.UTF8);
        }
        finally { _logGate.Release(); }
    }

    private void ReloadLogs()
    {
        _logGate.Wait();
        try { _logEntries = LogStore.Read(_eventLogPath); }
        finally { _logGate.Release(); }
        _logGrid.Rows.Clear();
        foreach (var entry in _logEntries.Reverse())
        {
            var row = _logGrid.Rows.Add(entry.ObservedAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "未知", entry.Kind, entry.Id, entry.Summary);
            _logGrid.Rows[row].Tag = entry;
        }
        _logSummary.Text = $"共 {_logEntries.Count} 条记录；默认保留最近 {LogRetentionDays} 天。删除操作只处理本程序日志。";
        ShowSelectedLog();
    }

    private void ShowSelectedLog()
    {
        var entry = _logGrid.SelectedRows.Count == 0 ? null : _logGrid.SelectedRows[0].Tag as LogEntry;
        _logDetail.Text = entry?.Raw ?? "选择一条日志查看完整 JSON 内容。";
    }

    private void CleanOldLogs()
    {
        if (MessageBox.Show(this, $"删除 {LogRetentionDays} 天以前的本程序日志？飞书待发送队列不会被删除。", "确认清理", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _logGate.Wait();
        try { _status.Text = $"已清理 {LogStore.RemoveOlderThan(_eventLogPath, DateTimeOffset.UtcNow.AddDays(-LogRetentionDays))} 条旧日志；{GetFeishuStatus()}"; }
        catch (Exception ex) { _status.Text = $"日志清理失败：{ex.Message}"; _ = AppendRecordAsync(new { observedAtUtc = DateTimeOffset.UtcNow, kind = "log-clean-error", error = ex.ToString() }); }
        finally { _logGate.Release(); }
        ReloadLogs();
    }

    private void DeleteAllLogs()
    {
        if (MessageBox.Show(this, "立即删除本程序的全部运行日志？此操作不可撤销，飞书待发送队列不会被删除。", "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _logGate.Wait();
        try { LogStore.Delete(_eventLogPath); _status.Text = $"运行日志已删除；{GetFeishuStatus()}"; }
        catch (Exception ex) { _status.Text = $"日志删除失败：{ex.Message}"; _ = AppendRecordAsync(new { observedAtUtc = DateTimeOffset.UtcNow, kind = "log-delete-error", error = ex.ToString() }); }
        finally { _logGate.Release(); }
        ReloadLogs();
    }

    private void OnThreadException(object? sender, ThreadExceptionEventArgs args)
    {
        _status.Text = $"程序错误：{args.Exception.Message}";
        _ = AppendRecordAsync(new { observedAtUtc = DateTimeOffset.UtcNow, kind = "ui-error", error = args.Exception.ToString() });
        MessageBox.Show(this, args.Exception.Message, "Codex Toast Monitor 错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs args)
    {
        if (_allowExit)
        {
            return;
        }

        var choice = MessageBox.Show(
            this,
            "请选择关闭方式：\r\n\r\n是：彻底退出程序\r\n否：最小化到系统托盘\r\n取消：保持窗口打开",
            "关闭 Codex Toast Monitor",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button3);

        if (choice == DialogResult.Yes)
        {
            _allowExit = true;
            Stop();
            return;
        }

        args.Cancel = true;
        if (choice == DialogResult.No)
        {
            // Let the canceled close message finish before changing the window state.
            BeginInvoke((MethodInvoker)(() =>
            {
                if (!IsDisposed)
                {
                    WindowState = FormWindowState.Minimized;
                }
            }));
        }
    }

    private void OnResize(object? sender, EventArgs args)
    {
        if (WindowState == FormWindowState.Minimized && !_isInTray && !_allowExit)
        {
            MinimizeToTray();
        }
    }

    private void MinimizeToTray()
    {
        _isInTray = true;
        ShowInTaskbar = false;
        Hide();
        _trayIcon.Visible = true;
    }

    private void RestoreFromTray()
    {
        if (IsDisposed) return;
        _trayIcon.Visible = false;
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        _isInTray = false;
    }

    private void ExitApplication()
    {
        _allowExit = true;
        _trayIcon.Visible = false;
        Close();
    }

    private void Stop()
    {
        if (_subscribed) { _listener.NotificationChanged -= OnNotificationChanged; _subscribed = false; }
        _pollTimer.Stop();
        Application.ThreadException -= OnThreadException;
        _deliveryQueue.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
    }
}
