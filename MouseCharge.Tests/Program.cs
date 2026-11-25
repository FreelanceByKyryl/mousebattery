using System.Drawing;
using MouseCharge.Core;
using MouseCharge.Devices;

var tests = new (string Name, Action Run)[]
{
    ("Default settings and local JSON round trip", SettingsRoundTrip),
    ("Existing floating widget settings default to a visible taskbar dock", UpgradeWidgetSettings),
    ("Bottom taskbar dock straddles the bar and clears the notification area", BottomTaskbarDock),
    ("Top taskbar dock uses physical pixels on a scaled negative monitor", ScaledTaskbarDock),
    ("Taskbar dock handles a vertical bar and hides when there is no safe fit", TaskbarDockFallback),
    ("Old, invalid and corrupted settings", InvalidSettings),
    ("Last known reading persists separately for each mouse", BatteryRoundTrip),
    ("Low battery notification is latched below the threshold", BatteryWarnings),
    ("Notification can be disabled and re-enabled", DisabledNotifications),
    ("Older readings and storage errors preserve the current reading", BatteryRecovery),
    ("Xtrfy M4 queries use the documented read-only frames", XtrfyQueries),
    ("Xtrfy M4 battery reading validates checksum, status and values", XtrfyBatteryReplies),
    ("Xtrfy M4 receiver status distinguishes connected and asleep", XtrfyConnectionReplies)
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
    Equal(true, settings.DockToTaskbar);
    Equal(true, settings.TaskbarWidgetVisible);
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
    edited.DockToTaskbar = false;
    edited.TaskbarWidgetVisible = false;
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
    Equal(false, loaded.DockToTaskbar);
    Equal(false, loaded.TaskbarWidgetVisible);
    Equal(false, loaded.WidgetVisible);
    Equal<double?>(-1050, loaded.DesktopX);
    Equal<double?>(420, loaded.DesktopY);

    edited.NotificationsEnabled = false;
    store.Save(edited);
    Equal(false, store.Load().NotificationsEnabled);
    Equal(1, Directory.GetFiles(data.Path).Length);
}

static void UpgradeWidgetSettings()
{
    using var data = new TestDirectory();
    File.WriteAllText(Path.Combine(data.Path, "settings.json"), """
        {"widgetVisible":false,"desktopX":-1050,"desktopY":420}
        """);
    var store = new SettingsStore(data.Path);
    var settings = store.Load();
    Equal(true, settings.DockToTaskbar);
    Equal(true, settings.TaskbarWidgetVisible);
    Equal(false, settings.WidgetVisible);
    Equal<double?>(-1050, settings.DesktopX);
    Equal<double?>(420, settings.DesktopY);

    settings.TaskbarWidgetVisible = false;
    settings.WidgetVisible = true;
    store.Save(settings);

    var reloaded = new SettingsStore(data.Path).Load();
    Equal(true, reloaded.DockToTaskbar);
    Equal(false, reloaded.TaskbarWidgetVisible);
    Equal(true, reloaded.WidgetVisible);
}

static void BottomTaskbarDock()
{
    var monitor = new Rectangle(0, 0, 1920, 1080);
    var bar = new Rectangle(0, 1032, 1920, 48);
    var tray = new Rectangle(1612, 1032, 308, 48);

    var point = TaskbarPlacement.Find(monitor, bar, tray, 245, 103, 96);
    Equal<(int X, int Y)?>((1359, 977), point);
    var card = new Rectangle(point!.Value.X, point.Value.Y, 245, 103);
    Equal(monitor.Bottom, card.Bottom);
    Equal(true, card.Top < bar.Top && card.IntersectsWith(bar));
    Equal(8, tray.Left - card.Right);
}

static void ScaledTaskbarDock()
{
    var monitor = new Rectangle(-1920, -1080, 1920, 1080);
    var bar = new Rectangle(-1920, -1080, 1920, 72);
    var tray = new Rectangle(-420, -1080, 420, 72);

    var point = TaskbarPlacement.Find(monitor, bar, tray, 245, 103, 144);
    Equal<(int X, int Y)?>((-800, -1080), point);
    Equal(true, new Rectangle(point!.Value.X, point.Value.Y, 368, 155).Bottom > bar.Bottom);
}

static void TaskbarDockFallback()
{
    var monitor = new Rectangle(0, 0, 1000, 800);
    var verticalBar = new Rectangle(940, 0, 60, 800);
    var hiddenBar = new Rectangle(0, 798, 1000, 48);
    var bottomBar = new Rectangle(0, 752, 1000, 48);
    var wideTray = new Rectangle(220, 752, 780, 48);

    Equal<(int X, int Y)?>((755, 449), TaskbarPlacement.Find(monitor, verticalBar, null, 245, 103, 96));
    Equal<(int X, int Y)?>(null, TaskbarPlacement.Find(monitor, hiddenBar, null, 245, 103, 96));
    Equal<(int X, int Y)?>(null, TaskbarPlacement.Find(monitor, bottomBar, wideTray, 245, 103, 96));
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

static void XtrfyQueries()
{
    byte[] battery = new byte[17];
    battery[0] = 0x08;
    battery[1] = 0x04;
    battery[^1] = 0x49;
    Equal(true, battery.SequenceEqual(XtrfyM4BatteryProtocol.BuildBatteryQuery()));

    byte[] connection = new byte[17];
    connection[0] = 0x08;
    connection[1] = 0x03;
    connection[^1] = 0x4A;
    Equal(true, connection.SequenceEqual(XtrfyM4BatteryProtocol.BuildConnectionQuery()));
}

static void XtrfyBatteryReplies()
{
    Equal(true, XtrfyM4BatteryProtocol.TryParseBatteryReply(XtrfyReply(0x04, 0, 0), out int percent, out bool charging));
    Equal(0, percent);
    Equal(false, charging);
    Equal(true, XtrfyM4BatteryProtocol.TryParseBatteryReply(XtrfyReply(0x04, 100, 1), out percent, out charging));
    Equal(100, percent);
    Equal(true, charging);

    Equal(false, XtrfyM4BatteryProtocol.TryParseBatteryReply(XtrfyReply(0x04, 101, 0), out _, out _));
    Equal(false, XtrfyM4BatteryProtocol.TryParseBatteryReply(XtrfyReply(0x04, 80, 2), out _, out _));
    Equal(false, XtrfyM4BatteryProtocol.TryParseBatteryReply(XtrfyReply(0x04, 80, 0, status: 1), out _, out _));
    Equal(false, XtrfyM4BatteryProtocol.TryParseBatteryReply(XtrfyReply(0x0A, 80, 0), out _, out _));
    byte[] corrupted = XtrfyReply(0x04, 80, 0);
    corrupted[^1] ^= 1;
    Equal(false, XtrfyM4BatteryProtocol.TryParseBatteryReply(corrupted, out _, out _));
    Equal(false, XtrfyM4BatteryProtocol.TryParseBatteryReply(corrupted.AsSpan(0, 16), out _, out _));
}

static void XtrfyConnectionReplies()
{
    Equal(true, XtrfyM4BatteryProtocol.TryParseConnectionReply(XtrfyReply(0x03, 0), out bool connected));
    Equal(false, connected);
    Equal(true, XtrfyM4BatteryProtocol.TryParseConnectionReply(XtrfyReply(0x03, 1), out connected));
    Equal(true, connected);
    Equal(false, XtrfyM4BatteryProtocol.TryParseConnectionReply(XtrfyReply(0x03, 2), out _));
}

static byte[] XtrfyReply(byte opcode, byte value, byte? charging = null, byte status = 0)
{
    var reply = new byte[17];
    reply[0] = 0x09;
    reply[1] = opcode;
    reply[2] = status;
    reply[5] = charging.HasValue ? (byte)2 : (byte)1;
    reply[6] = value;
    reply[7] = charging.GetValueOrDefault();
    reply[^1] = unchecked((byte)(0x55 - reply.Sum(item => item)));
    return reply;
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
