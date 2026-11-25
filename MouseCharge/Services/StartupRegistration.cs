using Microsoft.Win32;

namespace MouseCharge.Services;

internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MouseCharge";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string { Length: > 0 };
    }

    public static void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            string executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("MouseCharge could not find its executable.");
            using var key = Registry.CurrentUser.CreateSubKey(RunKey)
                ?? throw new InvalidOperationException("Windows did not open the startup registry key.");
            key.SetValue(ValueName, $"\"{executable}\"", RegistryValueKind.String);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
