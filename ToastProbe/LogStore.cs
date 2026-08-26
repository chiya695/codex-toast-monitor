using System.Text;
using System.Text.Encodings.Web;
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
    private static readonly JsonSerializerOptions DisplayJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };
    private static readonly JsonSerializerOptions StorageJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    public static string SerializeRecord(object record) => JsonSerializer.Serialize(record, StorageJsonOptions);

    public static IReadOnlyList<LogEntry> Read(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var entries = new List<LogEntry>();
        foreach (var record in ReadRecords(path))
        {
            try
            {
                var root = record.Root;
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
                entries.Add(new LogEntry(observedAt, kind, id, BuildSummary(root), JsonSerializer.Serialize(root, DisplayJsonOptions)));
            }
            catch
            {
                entries.Add(new LogEntry(null, "invalid", string.Empty, "无法解析的日志记录", record.Raw));
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
        foreach (var record in ReadRecords(path))
        {
            if (record.Root.TryGetProperty("observedAtUtc", out var value) && value.TryGetDateTimeOffset(out var observedAt) && observedAt < cutoff)
            {
                removed++;
            }
            else
            {
                kept.Add(record.Storage);
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

    private static IReadOnlyList<ParsedRecord> ReadRecords(string path)
    {
        var records = new List<ParsedRecord>();
        var bytes = Encoding.UTF8.GetBytes(File.ReadAllText(path));
        var readerState = new JsonReaderState(new JsonReaderOptions { AllowMultipleValues = true });
        var reader = new Utf8JsonReader(bytes, isFinalBlock: true, readerState);
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    continue;
                }

                using var document = JsonDocument.ParseValue(ref reader);
                var root = document.RootElement.Clone();
                records.Add(new ParsedRecord(root, JsonSerializer.Serialize(root, StorageJsonOptions)));
            }
        }
        catch
        {
            records.Add(new ParsedRecord(JsonDocument.Parse("{\"kind\":\"invalid\",\"message\":\"无法解析的日志内容\"}").RootElement.Clone(), Encoding.UTF8.GetString(bytes)));
        }

        return records;
    }

    private sealed record ParsedRecord(JsonElement Root, string Storage)
    {
        public string Raw => Storage;
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
