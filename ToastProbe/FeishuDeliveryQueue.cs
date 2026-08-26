using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexToastProbe;

internal sealed class FeishuDeliveryQueue : IDisposable
{
    private const int MaxTextLength = 3500;
    private readonly FeishuConfig _config;
    private readonly string _outboxPath;
    private readonly Func<object, Task> _appendEventLogAsync;
    private readonly Action<string, bool>? _reportStatus;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _outboxGate = new(1, 1);
    private Task? _worker;

    public FeishuDeliveryQueue(FeishuConfig config, string eventLogPath, Func<object, Task> appendEventLogAsync, Action<string, bool>? reportStatus = null)
    {
        _config = config;
        _appendEventLogAsync = appendEventLogAsync;
        _reportStatus = reportStatus;
        _outboxPath = Path.Combine(Path.GetDirectoryName(eventLogPath)!, "feishu-outbox");
    }

    public bool IsEnabled => _config.Enabled && Uri.TryCreate(_config.WebhookUrl, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps;

    public void Start()
    {
        if (!IsEnabled || (_worker is not null && !_worker.IsCompleted))
        {
            return;
        }

        Directory.CreateDirectory(_outboxPath);
        _worker = Task.Run(ProcessLoopAsync);
    }

    public async Task<bool> EnqueueAsync(ToastEvent toastEvent)
    {
        if (!IsEnabled)
        {
            return true;
        }

        try
        {
            Directory.CreateDirectory(_outboxPath);
            var target = Path.Combine(_outboxPath, $"{toastEvent.Id}.json");
            await _outboxGate.WaitAsync(_stop.Token);
            try
            {
                if (!File.Exists(target))
                {
                    var temporary = target + ".tmp";
                    await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(toastEvent), Encoding.UTF8, _stop.Token);
                    File.Move(temporary, target, true);
                }
            }
            finally
            {
                _outboxGate.Release();
            }

            return true;
        }
        catch (Exception ex)
        {
            await _appendEventLogAsync(new { observedAtUtc = DateTimeOffset.UtcNow, kind = "feishu-queue-error", error = ex.ToString(), id = toastEvent.Id });
            _reportStatus?.Invoke($"飞书待发送队列写入失败：{ex.Message}", true);
            return false;
        }
    }

    private async Task ProcessLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            if (!IsEnabled)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), _stop.Token);
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                {
                    break;
                }

                continue;
            }

            var sentAny = false;
            try
            {
                foreach (var path in Directory.EnumerateFiles(_outboxPath, "*.json"))
                {
                    if (_stop.IsCancellationRequested)
                    {
                        break;
                    }

                    ToastEvent? toastEvent;
                    try
                    {
                        toastEvent = JsonSerializer.Deserialize<ToastEvent>(await File.ReadAllTextAsync(path, _stop.Token));
                    }
                    catch (Exception ex)
                    {
                        await _appendEventLogAsync(new { observedAtUtc = DateTimeOffset.UtcNow, kind = "feishu-queue-read-error", error = ex.ToString(), path });
                        continue;
                    }

                    if (toastEvent is null)
                    {
                        continue;
                    }

                    if (await SendWithRetryAsync(toastEvent, _stop.Token))
                    {
                        File.Delete(path);
                        sentAny = true;
                    }
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                await _appendEventLogAsync(new { observedAtUtc = DateTimeOffset.UtcNow, kind = "feishu-worker-error", error = ex.ToString() });
            }

            try
            {
                await Task.Delay(sentAny ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(5), _stop.Token);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task<bool> SendWithRetryAsync(ToastEvent toastEvent, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await SendOnceAsync(toastEvent, cancellationToken);
                await _appendEventLogAsync(new { observedAtUtc = DateTimeOffset.UtcNow, kind = "feishu-sent", id = toastEvent.Id });
                _reportStatus?.Invoke($"飞书推送成功，通知 ID {toastEvent.Id}", false);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex) when (attempt < 3)
            {
                await _appendEventLogAsync(new { observedAtUtc = DateTimeOffset.UtcNow, kind = "feishu-retry", id = toastEvent.Id, attempt, error = ex.Message });
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
            }
            catch (Exception ex)
            {
                await _appendEventLogAsync(new { observedAtUtc = DateTimeOffset.UtcNow, kind = "feishu-send-error", id = toastEvent.Id, error = ex.ToString() });
                _reportStatus?.Invoke($"飞书推送失败：{ex.Message}。消息已保留并将自动重试。", true);
                return false;
            }
        }

        return false;
    }

    private async Task SendOnceAsync(ToastEvent toastEvent, CancellationToken cancellationToken)
    {
        var text = BuildMessage(toastEvent);
        var payload = new Dictionary<string, object?>
        {
            ["msg_type"] = "text",
            ["content"] = new { text }
        };

        if (!string.IsNullOrWhiteSpace(_config.Secret))
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            payload["timestamp"] = timestamp;
            payload["sign"] = BuildSignature(timestamp);
        }

        using var response = await _httpClient.PostAsJsonAsync(new Uri(_config.WebhookUrl, UriKind.Absolute), payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        if (document.RootElement.TryGetProperty("code", out var code) && code.GetInt32() != 0)
        {
            throw new InvalidOperationException($"Feishu returned code {code.GetInt32()}: {document.RootElement.GetProperty("msg").GetString()}");
        }
    }

    private string BuildSignature(string timestamp)
    {
        var stringToSign = $"{timestamp}\n{_config.Secret}";
        // Feishu defines stringToSign as the HMAC key and signs an empty message.
        var digest = HMACSHA256.HashData(Encoding.UTF8.GetBytes(stringToSign), Array.Empty<byte>());
        return Convert.ToBase64String(digest);
    }

    private static string BuildMessage(ToastEvent toastEvent)
    {
        var texts = toastEvent.Texts
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text.Trim())
            .ToArray();
        var question = texts.ElementAtOrDefault(0) ?? "（通知未提供提问内容）";
        var answer = texts.Length switch
        {
            0 => "（通知未提供回答内容）",
            1 => "（通知只包含一段文本，可能是状态提示）",
            _ => string.Join("\n", texts.Skip(1))
        };
        var message = $"[ChatGPT通知]\n\n提问：\n{question}\n\n回答：\n{answer}\n\n通知时间：{toastEvent.CreationTime:yyyy-MM-dd HH:mm:ss zzz}\n通知 ID：{toastEvent.Id}";
        return message.Length <= MaxTextLength ? message : message[..MaxTextLength] + "\n[内容已截断]";
    }

    public void Stop()
    {
        _stop.Cancel();
        try
        {
            _worker?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }
    }

    public void Dispose()
    {
        Stop();
        _httpClient.Dispose();
        _outboxGate.Dispose();
        _stop.Dispose();
    }
}
