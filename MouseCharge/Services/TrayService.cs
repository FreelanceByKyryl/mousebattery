using System.Drawing;
using System.Globalization;
using Forms = System.Windows.Forms;

namespace MouseCharge.Services;

internal sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Icon _image;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.ToolStripMenuItem _show;
    private readonly Forms.ToolStripMenuItem _hide;
    private readonly Forms.ToolStripMenuItem _selection;
    private readonly Forms.ToolStripMenuItem _topmost;
    private readonly Forms.ToolStripMenuItem _startup;
    private readonly Action<string?> _selectMouse;
    private Icon? _batteryImage;
    private (int? Percentage, bool Approximate, bool Stale) _indicator;

    public TrayService(
        Action show, Action hide, Action refresh, Action<string?> selectMouse, Action topmost,
        Action settings, Action diagnostics, Action startup, Action exit)
    {
        _selectMouse = selectMouse;
        _menu = new Forms.ContextMenuStrip();
        _show = AddItem("Show widget", show);
        _hide = AddItem("Hide widget", hide);
        AddItem("Refresh battery", refresh);
        _selection = new Forms.ToolStripMenuItem("Select mouse");
        _menu.Items.Add(_selection);
        _topmost = AddItem("Always on top", topmost);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        AddItem("Settings", settings);
        AddItem("Diagnostics", diagnostics);
        _startup = AddItem("Start with Windows", startup);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        AddItem("Exit", exit);
        SetDevices([], null);

        var resource = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Assets/MouseCharge.ico"));
        using (var stream = resource?.Stream)
        using (var icon = stream is null ? null : new Icon(stream))
            _image = (Icon)(icon ?? SystemIcons.Application).Clone();

        _icon = new Forms.NotifyIcon
        {
            Icon = _image,
            ContextMenuStrip = _menu,
            Text = "MouseCharge · Looking for mice…",
            Visible = true,
        };
        _icon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                show();
        };
    }

    public void SetState(bool visible, bool topmost, bool startup)
    {
        _show.Enabled = !visible;
        _hide.Enabled = visible;
        _topmost.Checked = topmost;
        _startup.Checked = startup;
    }

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

    public void SetBattery(string name, int? percent, bool approximate, bool stale)
    {
        string amount = percent is int value
            ? (approximate ? "≈" : "") + value.ToString(CultureInfo.InvariantCulture) + "%" +
              (stale ? " (outdated)" : "")
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

        var indicator = (percent, approximate, stale);
        if (indicator == _indicator)
            return;

        var image = TrayBatteryIcon.Create(percent, approximate, stale);
        _icon.Icon = image ?? _image;
        _batteryImage?.Dispose();
        _batteryImage = image;
        _indicator = indicator;
    }

    public void ShowWarning(string title, string message) =>
        _icon.ShowBalloonTip(5000, title, message, Forms.ToolTipIcon.Warning);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _batteryImage?.Dispose();
        _image.Dispose();
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
