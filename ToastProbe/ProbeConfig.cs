using System.Text.Json;

namespace CodexToastProbe;

internal sealed class ProbeConfig
{
    public FeishuConfig Feishu { get; set; } = new();

    public static ProbeConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            return new ProbeConfig();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ProbeConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new ProbeConfig();
        }
        catch
        {
            return new ProbeConfig();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}

internal sealed class FeishuConfig
{
    public bool Enabled { get; set; }
    public string WebhookUrl { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
}
