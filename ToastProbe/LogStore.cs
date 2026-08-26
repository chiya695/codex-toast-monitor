using System.Text.Json;

namespace CodexToastProbe;

internal sealed record LogEntry(
    DateTimeOffset? ObservedAtUtc,
    string Kind,
    string Id,
    string Summary,
    string Raw);

internal static class LogStore
{
    public static IReadOnlyList<LogEntry> Read(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var entries = new List<LogEntry>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                DateTimeOffset? observedAt = null;
                if (root.TryGetProperty("observedAtUtc", out var timestamp) &&
                    timestamp.ValueKind == JsonValueKind.String &&
                    timestamp.TryGetDateTimeOffset(out var parsedTimestamp))
                {
                    observedAt = parsedTimestamp;
                }

                var kind = root.TryGetProperty("kind", out var kindValue)
                    ? kindValue.GetString() ?? "unknown"
                    : "unknown";
                var id = root.TryGetProperty("id", out var idValue)
                    ? idValue.ToString()
                    : string.Empty;
                entries.Add(new LogEntry(observedAt, kind, id, BuildSummary(root), line));
            }
            catch
            {
                entries.Add(new LogEntry(null, "invalid", string.Empty, "无法解析的日志记录", line));
            }
        }

        return entries;
    }

    public static int RemoveOlderThan(string path, DateTimeOffset cutoff)
    {
        if (!File.Exists(path))
        {
            return 0;
        }

        var kept = new List<string>();
        var removed = 0;
        foreach (var line in File.ReadLines(path))
        {
            if (TryGetObservedAt(line, out var observedAt) && observedAt < cutoff)
            {
                removed++;
            }
            else
            {
                kept.Add(line);
            }
        }

        if (removed == 0)
        {
            return 0;
        }

        ReplaceFile(path, kept);
        return removed;
    }

    public static void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static bool TryGetObservedAt(string line, out DateTimeOffset observedAt)
    {
        observedAt = default;
        try
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.TryGetProperty("observedAtUtc", out var value) &&
                value.TryGetDateTimeOffset(out observedAt);
        }
        catch
        {
            return false;
        }
    }

    private static void ReplaceFile(string path, IReadOnlyList<string> lines)
    {
        var temporaryPath = path + ".tmp";
        File.WriteAllLines(temporaryPath, lines);
        try
        {
            File.Replace(temporaryPath, path, null);
        }
        catch (PlatformNotSupportedException)
        {
            File.Move(temporaryPath, path, true);
        }
    }

    private static string BuildSummary(JsonElement root)
    {
        if (root.TryGetProperty("texts", out var texts) && texts.ValueKind == JsonValueKind.Array)
        {
            return Truncate(string.Join(" | ", texts.EnumerateArray().Select(item => item.GetString()).Where(text => !string.IsNullOrWhiteSpace(text))));
        }

        if (root.TryGetProperty("error", out var error))
        {
            return Truncate(error.GetString() ?? error.ToString());
        }

        return root.TryGetProperty("message", out var message)
            ? Truncate(message.GetString() ?? message.ToString())
            : root.ToString();
    }

    private static string Truncate(string value)
    {
        const int maxLength = 240;
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}
