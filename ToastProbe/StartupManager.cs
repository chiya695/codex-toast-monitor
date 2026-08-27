using Microsoft.Win32;

namespace CodexToastProbe;

internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CodexToastMonitor";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (key is null)
        {
            throw new InvalidOperationException("无法访问当前用户的开机启动设置。");
        }

        if (enabled)
        {
            var executablePath = Path.Combine(AppContext.BaseDirectory, "CodexToastProbe.exe");
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new InvalidOperationException("无法确定程序启动路径。");
            }

            if (!File.Exists(executablePath))
            {
                throw new FileNotFoundException("找不到程序启动文件。", executablePath);
            }

            key.SetValue(ValueName, $"\"{executablePath}\"", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
