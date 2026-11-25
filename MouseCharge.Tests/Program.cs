using MouseCharge.Core;

var tests = new (string Name, Action Run)[]
{
    ("Default settings and local JSON round trip", SettingsRoundTrip),
    ("Old, invalid and corrupted settings", InvalidSettings),
    ("Last known reading persists separately for each mouse", BatteryRoundTrip),
    ("Low battery notification is latched below the threshold", BatteryWarnings),
    ("Notification can be disabled and re-enabled", DisabledNotifications),
    ("Older readings and storage errors preserve the current reading", BatteryRecovery)
};

var failures = 0;
foreach (var (name, run) in tests)
{
    try
    {
        run();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception error)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {error.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures} passed, {failures} failed, {tests.Length} total.");
return failures == 0 ? 0 : 1;

static void SettingsRoundTrip()
{
    using var data = new TestDirectory();
    var store = new SettingsStore(data.Path);
    var settings = store.Load();
    Equal(60, settings.RefreshSeconds);
    Equal(15, settings.LowBatteryThreshold);
    Equal(true, settings.NotificationsEnabled);
    Equal(true, settings.WidgetVisible);
    Equal(null, store.LoadIssue);

    var edited = settings.Copy();
    edited.DeviceId = "mouse-id";
    edited.RefreshSeconds = 120;
    edited.LowBatteryThreshold = 22;
    edited.WidgetOpacity = 0.82;
    edited.WidgetScale = 1.25;
    edited.Theme = "Light";
    edited.TransparentBackground = true;
    edited.AlwaysOnTop = true;
    edited.PositionLocked = true;
    edited.ClickThrough = true;
    edited.StartWithWindows = true;
    edited.WidgetVisible = false;
    edited.DesktopX = -1050;
    edited.DesktopY = 420;
    store.Save(edited);

    var loaded = new SettingsStore(data.Path).Load();
    Equal(null, settings.DeviceId);
    Equal(false, settings.ClickThrough);
    Equal("mouse-id", loaded.DeviceId);
    Equal(120, loaded.RefreshSeconds);
    Equal(22, loaded.LowBatteryThreshold);
    Equal(0.82, loaded.WidgetOpacity);
    Equal(1.25, loaded.WidgetScale);
    Equal("Light", loaded.Theme);
    Equal(true, loaded.TransparentBackground);
    Equal(true, loaded.AlwaysOnTop);
    Equal(true, loaded.PositionLocked);
    Equal(true, loaded.ClickThrough);
    Equal(true, loaded.StartWithWindows);
    Equal(false, loaded.WidgetVisible);
    Equal<double?>(-1050, loaded.DesktopX);
    Equal<double?>(420, loaded.DesktopY);

    edited.NotificationsEnabled = false;
    store.Save(edited);
    Equal(false, store.Load().NotificationsEnabled);
    Equal(1, Directory.GetFiles(data.Path).Length);
}

static void InvalidSettings()
{
    using var data = new TestDirectory();
    var path = Path.Combine(data.Path, "settings.json");
    var store = new SettingsStore(data.Path);
    File.WriteAllText(path, """
        {"DeviceId":"  ","RefreshSeconds":42,"LowBatteryThreshold":120,
         "widgetOpacity":0,"theme":"light","widgetScale":4,"desktopX":-26}
        """);
    var loaded = store.Load();
    Equal(null, loaded.DeviceId);
    Equal(60, loaded.RefreshSeconds);
    Equal(100, loaded.LowBatteryThreshold);
    Equal(0.25, loaded.WidgetOpacity);
    Equal("Light", loaded.Theme);
    Equal(2.0, loaded.WidgetScale);
    Equal<double?>(null, loaded.DesktopX);
    Equal<double?>(null, loaded.DesktopY);
    Equal(true, loaded.NotificationsEnabled);

    File.WriteAllText(path, "{");
    loaded = store.Load();
    Equal(60, loaded.RefreshSeconds);
    Equal(true, store.LoadIssue is not null);
    store.Save(loaded);
    Equal(null, store.LoadIssue);
    Equal(60, store.Load().RefreshSeconds);
}

static void BatteryRoundTrip()
{
    using var data = new TestDirectory();
    var history = new BatteryHistory(data.Path);
    var first = Reading(58, 0);
    var second = Reading(30, 1) with { IsApproximate = true, IsCharging = false, Provider = "Windows" };
    Equal<LastKnownBattery?>(null, history.GetLastKnown("missing"));
    Equal(false, history.RecordReading("FIRST", first, 15, true));
    Equal(false, history.RecordReading("SECOND", second, 15, true));

    history = new BatteryHistory(data.Path);
    Equal(first, history.GetLastKnown("first"));
    Equal(second, history.GetLastKnown("second"));
    Equal(null, history.PersistenceIssue);
}

static void BatteryWarnings()
{
    using var data = new TestDirectory();
    var history = new BatteryHistory(data.Path);
    Equal(false, history.RecordReading("a", Reading(15, 0), 15, true));
    Equal(true, history.RecordReading("a", Reading(14, 1), 15, true));

    history = new BatteryHistory(data.Path);
    Equal(false, history.RecordReading("a", Reading(13, 2), 15, true));
    Equal(true, history.RecordReading("b", Reading(5, 3), 15, true));
    Equal(false, history.RecordReading("a", Reading(15, 4), 15, true));
    Equal(true, history.RecordReading("a", Reading(14, 5), 15, true));
}

static void DisabledNotifications()
{
    using var data = new TestDirectory();
    var history = new BatteryHistory(data.Path);
    Equal(false, history.RecordReading("a", Reading(12, 0), 15, false));
    Equal(true, history.RecordReading("a", Reading(11, 1), 15, true));
    Equal(false, history.RecordReading("a", Reading(10, 2), 15, false));
    Equal(false, history.RecordReading("a", Reading(9, 3), 15, true));
}

static void BatteryRecovery()
{
    using var data = new TestDirectory();
    var path = Path.Combine(data.Path, "battery-history.json");
    File.WriteAllText(path, "{");
    var history = new BatteryHistory(data.Path);
    Equal(true, history.PersistenceIssue is not null);

    var latest = Reading(70, 2);
    Equal(false, history.RecordReading("a", latest, 15, true));
    Equal(false, history.RecordReading("a", Reading(10, 1), 15, true));
    Equal(latest, history.GetLastKnown("a"));
    Equal(null, history.PersistenceIssue);

    File.Delete(path);
    Directory.CreateDirectory(path);
    latest = Reading(65, 3);
    Equal(false, history.RecordReading("a", latest, 15, true));
    Equal(latest, history.GetLastKnown("a"));
    Equal(true, history.PersistenceIssue is not null);
}

static LastKnownBattery Reading(int percentage, int seconds) =>
    new(percentage, false, null, DateTimeOffset.UnixEpoch.AddSeconds(seconds), "Battery Service");

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}

sealed class TestDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"MouseCharge.Tests.{Guid.NewGuid():N}");

    public TestDirectory() => Directory.CreateDirectory(Path);

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
