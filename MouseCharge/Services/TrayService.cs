using System.Drawing;
using System.Globalization;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace MouseCharge.Services;

internal sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Icon _defaultImage;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.ToolStripMenuItem _selection;
    private readonly Forms.ToolStripMenuItem _startup;
    private readonly Action<string?> _selectMouse;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private Icon? _batteryImage;
    private (int? Percentage, bool Stale) _indicator;
    private bool _disposed;

    public TrayService(
        Action refresh, Action<string?> selectMouse,
        Action settings, Action diagnostics, Action startup, Action exit)
    {
        _selectMouse = selectMouse;
        _menu = new Forms.ContextMenuStrip();
        AddItem("Refresh battery", refresh);
        _selection = new Forms.ToolStripMenuItem("Select mouse");
        _menu.Items.Add(_selection);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        AddItem("Settings", settings);
        AddItem("Diagnostics", diagnostics);
        _startup = AddItem("Start with Windows", startup);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        AddItem("Exit", exit);
        SetDevices([], null);

        var resource = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Assets/MouseCharge.ico"));
        using var stream = resource?.Stream;
        using var icon = stream is null ? null : new Icon(stream);
        _defaultImage = (Icon)(icon ?? SystemIcons.Application).Clone();

        _icon = new Forms.NotifyIcon
        {
            ContextMenuStrip = _menu,
            Text = "MouseCharge · Looking for mice…",
        };
        RefreshBatteryIcon();
        _icon.Visible = true;
        SystemEvents.UserPreferenceChanged += OnAppearanceChanged;
        SystemEvents.DisplaySettingsChanged += OnAppearanceChanged;
    }

    public void SetStartupEnabled(bool enabled) => _startup.Checked = enabled;

    public void SetDevices(IReadOnlyList<(string Id, string Name)> devices, string? selectedId)
    {
        while (_selection.DropDownItems.Count > 0)
        {
            var item = _selection.DropDownItems[0];
            _selection.DropDownItems.RemoveAt(0);
            item.Dispose();
        }
        AddDevice("Automatic", null, selectedId);
        foreach (var device in devices)
            AddDevice(device.Name, device.Id, selectedId);
    }

    public void SetBattery(string name, int? percent, bool approximate, bool stale, bool charging)
    {
        string amount = percent is int value
            ? (approximate ? "≈" : "") + value.ToString(CultureInfo.InvariantCulture) + "%" +
              (stale ? " (outdated)" : charging ? " · Charging" : "")
            : "Battery unavailable";
        // The Windows notification area tooltip is limited to 63 UTF-16 code units.
        const string prefix = "MouseCharge · ";
        string suffix = " · " + amount;
        int maxNameLength = 63 - prefix.Length - suffix.Length;
        if (name.Length > maxNameLength)
        {
            int end = maxNameLength - 1;
            if (char.IsHighSurrogate(name[end - 1]))
                end--;
            name = name[..end] + "…";
        }
        _icon.Text = prefix + name + suffix;

        var indicator = (percent, stale);
        if (indicator == _indicator)
            return;

        _indicator = indicator;
        RefreshBatteryIcon();
    }

    private void RefreshBatteryIcon()
    {
        var image = TrayBatteryIcon.Create(_indicator.Percentage, _indicator.Stale);
        _icon.Icon = image ?? _defaultImage;
        _batteryImage?.Dispose();
        _batteryImage = image;
    }

    private void OnAppearanceChanged(object? sender, EventArgs args) =>
        _dispatcher.InvokeAsync(() =>
        {
            if (!_disposed)
                RefreshBatteryIcon();
        }, DispatcherPriority.Background);

    public void ShowWarning(string title, string message) =>
        _icon.ShowBalloonTip(5000, title, message, Forms.ToolTipIcon.Warning);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnAppearanceChanged;
        SystemEvents.DisplaySettingsChanged -= OnAppearanceChanged;
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _batteryImage?.Dispose();
        _defaultImage.Dispose();
    }

    private Forms.ToolStripMenuItem AddItem(string label, Action handler)
    {
        var item = new Forms.ToolStripMenuItem(label);
        item.Click += (_, _) => handler();
        _menu.Items.Add(item);
        return item;
    }

    private void AddDevice(string label, string? id, string? selectedId)
    {
        var item = new Forms.ToolStripMenuItem(label.Replace("&", "&&")) { Checked = id == selectedId };
        item.Click += (_, _) => _selectMouse(id);
        _selection.DropDownItems.Add(item);
    }
}
