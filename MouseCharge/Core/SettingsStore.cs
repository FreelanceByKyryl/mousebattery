using System.IO;

namespace MouseCharge.Core;

internal sealed class SettingsStore
{
    private readonly string _path;

    public SettingsStore() : this(LocalJson.AppDirectory)
    {
    }

    internal SettingsStore(string directory) => _path = Path.Combine(directory, "settings.json");

    public string? LoadIssue { get; private set; }

    public AppSettings Load()
    {
        var settings = LocalJson.Load<AppSettings>(_path, out var issue) ?? new AppSettings();
        LoadIssue = issue;
        settings.Normalize();
        return settings;
    }

    public void Save(AppSettings settings)
    {
        var normalized = settings.Copy();
        normalized.Normalize();
        LocalJson.Save(_path, normalized);
        LoadIssue = null;
    }
}
