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

    public SettingsWindow(
        AppSettings settings,
        IReadOnlyList<(string Id, string Name)> devices,
        Action showDiagnostics)
    {
        InitializeComponent();
        _settings = settings;
        _showDiagnostics = showDiagnostics;

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
        TaskbarIndicatorChoice.IsChecked = settings.TaskbarIndicatorVisible;
        StartWithWindowsChoice.IsChecked = settings.StartWithWindows;
    }

    private void Save(object sender, RoutedEventArgs e)
    {
        _settings.DeviceId = ((DeviceChoice)MouseChoice.SelectedItem).Id;
        var refresh = (ComboBoxItem)RefreshChoice.SelectedItem;
        _settings.RefreshSeconds = int.Parse((string)refresh.Tag, CultureInfo.InvariantCulture);
        _settings.LowBatteryThreshold = (int)Threshold.Value;
        _settings.NotificationsEnabled = Notifications.IsChecked == true;
        _settings.TaskbarIndicatorVisible = TaskbarIndicatorChoice.IsChecked == true;
        _settings.StartWithWindows = StartWithWindowsChoice.IsChecked == true;
        _settings.Normalize();
        DialogResult = true;
    }

    private void OpenDiagnostics(object sender, RoutedEventArgs e) => _showDiagnostics();

    private sealed record DeviceChoice(string? Id, string Name);
}
