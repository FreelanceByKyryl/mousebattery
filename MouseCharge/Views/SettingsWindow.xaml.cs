using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MouseCharge.Core;

namespace MouseCharge.Views;

internal partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Action _showDiagnostics;
    private readonly Action _resetPosition;
    private bool _positionResetRequested;

    public SettingsWindow(
        AppSettings settings,
        IReadOnlyList<(string Id, string Name)> devices,
        Action showDiagnostics,
        Action resetPosition)
    {
        InitializeComponent();
        _settings = settings;
        _showDiagnostics = showDiagnostics;
        _resetPosition = resetPosition;

        var choices = new List<DeviceChoice> { new(null, "Automatic") };
        var deviceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in devices)
        {
            if (deviceIds.Add(device.Id))
                choices.Add(new DeviceChoice(device.Id, device.Name));
        }

        var selected = choices.FindIndex(choice =>
            StringComparer.OrdinalIgnoreCase.Equals(choice.Id, settings.DeviceId));
        if (selected < 0)
        {
            selected = choices.Count;
            choices.Add(new DeviceChoice(settings.DeviceId, "Previously selected mouse (not detected)"));
        }

        MouseChoice.ItemsSource = choices;
        MouseChoice.SelectedIndex = selected;
        RefreshChoice.SelectedIndex = settings.RefreshSeconds switch
        {
            30 => 0,
            120 => 2,
            300 => 3,
            _ => 1
        };
        Threshold.Value = settings.LowBatteryThreshold;
        Notifications.IsChecked = settings.NotificationsEnabled;
        Scale.Value = settings.WidgetScale;
        WidgetOpacity.Value = settings.WidgetOpacity;
        ThemeChoice.SelectedIndex = settings.Theme == "Light" ? 1 : 0;
        Transparent.IsChecked = settings.TransparentBackground;
        TopmostChoice.IsChecked = settings.AlwaysOnTop;
        LockPosition.IsChecked = settings.PositionLocked;
        ClickThroughChoice.IsChecked = settings.ClickThrough;
        StartWithWindowsChoice.IsChecked = settings.StartWithWindows;
    }

    private void Save(object sender, RoutedEventArgs e)
    {
        _settings.DeviceId = ((DeviceChoice)MouseChoice.SelectedItem).Id;
        var refresh = (ComboBoxItem)RefreshChoice.SelectedItem;
        _settings.RefreshSeconds = int.Parse((string)refresh.Tag, CultureInfo.InvariantCulture);
        _settings.LowBatteryThreshold = (int)Threshold.Value;
        _settings.NotificationsEnabled = Notifications.IsChecked == true;
        _settings.WidgetScale = Scale.Value;
        _settings.WidgetOpacity = WidgetOpacity.Value;
        _settings.Theme = (string)((ComboBoxItem)ThemeChoice.SelectedItem).Content;
        _settings.TransparentBackground = Transparent.IsChecked == true;
        _settings.AlwaysOnTop = TopmostChoice.IsChecked == true;
        _settings.PositionLocked = LockPosition.IsChecked == true;
        _settings.ClickThrough = ClickThroughChoice.IsChecked == true;
        _settings.StartWithWindows = StartWithWindowsChoice.IsChecked == true;
        _settings.Normalize();

        if (_positionResetRequested)
            _resetPosition();

        DialogResult = true;
    }

    private void QueuePositionReset(object sender, RoutedEventArgs e)
    {
        _positionResetRequested = true;
        ResetButton.IsEnabled = false;
        ResetMessage.Visibility = Visibility.Visible;
    }

    private void OpenDiagnostics(object sender, RoutedEventArgs e) => _showDiagnostics();

    private sealed record DeviceChoice(string? Id, string Name);
}
