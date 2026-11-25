using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using MouseCharge.Core;
using MouseCharge.Devices;
using MouseCharge.Services;
using MouseCharge.Views;

namespace MouseCharge;

internal sealed class DesktopController : IDisposable
{
    private readonly Application _app;
    private readonly SettingsStore _store = new();
    private readonly BatteryHistory _history = new();
    private readonly MouseBatteryService _battery = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _deviceAccess = new(1, 1);
    private readonly DispatcherTimer _poll;
    private readonly TrayService _tray;
    private readonly BatteryNotificationService _notifications;
    private readonly AppSettings _settings;
    private IReadOnlyList<MouseDevice> _devices = [];
    private MouseDevice? _currentDevice;
    private BatteryScan? _scan;
    private string? _refreshIssue;
    private string? _settingsIssue;
    private string? _startupIssue;
    private bool _refreshing;
    private bool _refreshAgain;
    private bool _suspending;
    private bool _disposed;
    private SettingsWindow? _settingsWindow;
    private DiagnosticsWindow? _diagnosticsWindow;

    public DesktopController(Application app)
    {
        _app = app;
        _settings = _store.Load();
        _settingsIssue = _store.LoadIssue;
        ReadStartupSetting();

        _poll = new DispatcherTimer(DispatcherPriority.Background, app.Dispatcher);
        _poll.Tick += (_, _) => QueueRefresh();

        _tray = new TrayService(
            () => QueueRefresh(followUpIfBusy: true),
            SelectDevice, ShowSettings, ShowDiagnostics, ToggleStartup, Exit);
        _notifications = new BatteryNotificationService(_history, _tray);
    }

    public void Start(bool registerStartup)
    {
        if (registerStartup)
        {
            SetStartup(true, refreshRegistration: true);
            SaveSettings();
        }

        _tray.SetStartupEnabled(_settings.StartWithWindows);
        _poll.Interval = TimeSpan.FromSeconds(_settings.RefreshSeconds);
        Render();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        QueueRefresh();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _poll.Stop();
        _lifetime.Cancel();
        _tray.Dispose();
        _lifetime.Dispose();
    }

    private async void QueueRefresh(bool followUpIfBusy = false)
    {
        if (_disposed || _suspending)
            return;
        if (_refreshing)
        {
            _refreshAgain |= followUpIfBusy;
            return;
        }

        _refreshing = true;
        _poll.Stop();
        try
        {
            do
            {
                _refreshAgain = false;
                await RefreshAsync(_settings.DeviceId, _lifetime.Token);
            } while (_refreshAgain && !_suspending && !_disposed);
        }
        catch (OperationCanceledException) when (_disposed) { }
        finally
        {
            _refreshing = false;
            if (!_disposed && !_suspending)
                _poll.Start();
        }
    }

    private async Task RefreshAsync(string? requestedId, CancellationToken lifetime)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        CancellationToken token = timeout.Token;
        MouseDevice? selected = null;
        BatteryScan? scan = null;
        IReadOnlyList<MouseDevice>? detected = null;
        string? issue = null;
        try
        {
            await _deviceAccess.WaitAsync(token);
            try
            {
                var discovery = await _battery.DiscoverWithDiagnosticsAsync(token);
                detected = discovery.Devices;
                string? previousId = _currentDevice?.Id ?? _settings.LastAutomaticDeviceId;
                foreach (var candidate in Candidates(detected, requestedId, previousId))
                {
                    token.ThrowIfCancellationRequested();
                    var result = candidate.IsConnected ? await _battery.ReadAsync(candidate, token) : null;
                    if (selected is null || result?.Reading is not null)
                    {
                        selected = candidate;
                        scan = result;
                    }

                    bool previouslyRead = candidate.IsConnected && candidate.Id == previousId &&
                        _history.GetLastKnown(candidate.Id) is not null;
                    if (result?.Reading is not null || requestedId is not null || previouslyRead)
                        break;
                }
            }
            finally
            {
                _deviceAccess.Release();
            }
        }
        catch (OperationCanceledException) when (!lifetime.IsCancellationRequested)
        {
            issue = "Device refresh timed out.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            issue = $"Device refresh failed: {ex.Message}";
        }

        lifetime.ThrowIfCancellationRequested();
        if (_suspending || requestedId != _settings.DeviceId)
            return;

        _refreshIssue = issue;
        if (detected is not null)
        {
            _devices = detected;
            _tray.SetDevices(DeviceChoices(), _settings.DeviceId);
        }

        if (selected is not null)
            _currentDevice = selected;
        else if (issue is null && _currentDevice is not null)
            _currentDevice = _currentDevice with { IsConnected = false };

        _scan = scan;
        if (_currentDevice is { } device && scan?.Reading is { } reading)
        {
            _notifications.Observe(device, reading, _settings);

            if (requestedId is null &&
                (_settings.LastAutomaticDeviceId != device.Id || _settings.LastAutomaticDeviceName != device.Name))
            {
                _settings.LastAutomaticDeviceId = device.Id;
                _settings.LastAutomaticDeviceName = device.Name;
                SaveSettings();
            }
        }
        Render();
    }

    private IEnumerable<MouseDevice> Candidates(
        IReadOnlyList<MouseDevice> devices, string? requestedId, string? previousId)
    {
        if (requestedId is not null)
            return devices.Where(device => device.Id == requestedId);

        return devices.OrderByDescending(device => device.IsConnected)
            .ThenByDescending(device => device.Id == previousId)
            .ThenByDescending(device => _history.GetLastKnown(device.Id) is not null)
            .ThenByDescending(device => device.ConnectionType is "Bluetooth" or "Bluetooth LE" or "USB receiver");
    }

    private void Render()
    {
        string name = _currentDevice?.Name
            ?? (_settings.DeviceId is null ? _settings.LastAutomaticDeviceName ?? "Wireless mouse" : "Selected mouse");
        bool connected = !_suspending && _refreshIssue is null && _currentDevice?.IsConnected == true;
        var fresh = connected ? _scan?.Reading : null;
        string? id = _currentDevice?.Id ?? _settings.DeviceId ?? _settings.LastAutomaticDeviceId;
        var previous = id is null ? null : _history.GetLastKnown(id);
        int? percent = fresh?.Percentage ?? previous?.Percentage;
        bool approximate = fresh?.IsApproximate ?? previous?.IsApproximate ?? false;
        bool outdated = fresh is null && previous is not null;
        _tray.SetBattery(name, percent, approximate, outdated, charging: fresh?.IsCharging == true);
    }

    private IReadOnlyList<(string Id, string Name)> DeviceChoices()
    {
        var choices = _devices.Select(device => (device.Id, DisplayName(device))).ToList();
        if (_settings.DeviceId is { } selectedId && !_devices.Any(device => device.Id == selectedId))
            choices.Add((selectedId, _currentDevice?.Name is { } name ? $"{name} · unavailable" : "Saved mouse · unavailable"));
        return choices;
    }

    private static string DisplayName(MouseDevice device)
    {
        string status = device.IsConnected ? device.ConnectionType : $"{device.ConnectionType}, disconnected";
        return $"{device.Name} · {status}";
    }

    private void SelectDevice(string? id)
    {
        if (_settings.DeviceId == id)
            return;
        _settings.DeviceId = id;
        ResetSelection();
        SaveSettings();
        QueueRefresh(followUpIfBusy: true);
    }

    private void ResetSelection()
    {
        _currentDevice = null;
        _scan = null;
        _refreshIssue = null;
        _tray.SetDevices(DeviceChoices(), _settings.DeviceId);
        Render();
    }

    private void Exit()
    {
        Dispose();
        _app.Shutdown();
    }

    private void ToggleStartup()
    {
        SetStartup(!_settings.StartWithWindows);
        SaveSettings();
    }

    private void ReadStartupSetting()
    {
        try { _settings.StartWithWindows = StartupRegistration.IsEnabled(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            _startupIssue = $"Windows startup registration: {ex.Message}";
        }
    }

    private void SetStartup(bool enabled, bool refreshRegistration = false)
    {
        if (!refreshRegistration && enabled == _settings.StartWithWindows)
            return;
        try
        {
            StartupRegistration.SetEnabled(enabled);
            _settings.StartWithWindows = enabled;
            _tray.SetStartupEnabled(enabled);
            _startupIssue = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or InvalidOperationException)
        {
            MessageBox.Show(ex.Message, "Start with Windows", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaveSettings()
    {
        try
        {
            _store.Save(_settings);
            _settingsIssue = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _settingsIssue = $"Settings could not be saved: {ex.Message}";
        }
    }

    private void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var draft = _settings.Copy();
        string? previousId = _settings.DeviceId;
        int previousThreshold = _settings.LowBatteryThreshold;
        bool notificationsWereEnabled = _settings.NotificationsEnabled;
        var window = new SettingsWindow(draft, DeviceChoices(), ShowDiagnostics);
        _settingsWindow = window;
        try
        {
            if (window.ShowDialog() != true || _disposed)
                return;

            _settings.DeviceId = draft.DeviceId;
            _settings.RefreshSeconds = draft.RefreshSeconds;
            _settings.LowBatteryThreshold = draft.LowBatteryThreshold;
            _settings.NotificationsEnabled = draft.NotificationsEnabled;
            SetStartup(draft.StartWithWindows);
            _poll.Interval = TimeSpan.FromSeconds(_settings.RefreshSeconds);
            if (previousId != draft.DeviceId)
                ResetSelection();

            SaveSettings();
            if (previousId != draft.DeviceId || previousThreshold != draft.LowBatteryThreshold ||
                (!notificationsWereEnabled && draft.NotificationsEnabled))
                QueueRefresh(followUpIfBusy: true);
        }
        finally
        {
            _settingsWindow = null;
        }
    }

    private void ShowDiagnostics()
    {
        if (_diagnosticsWindow is not null)
        {
            _diagnosticsWindow.Activate();
            return;
        }

        var window = new DiagnosticsWindow(CreateReportAsync);
        if (_settingsWindow is { IsVisible: true })
            window.Owner = _settingsWindow;
        _diagnosticsWindow = window;
        window.Closed += (_, _) => _diagnosticsWindow = null;
        window.Show();
    }

    private async Task<string> CreateReportAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        await _deviceAccess.WaitAsync(timeout.Token);
        try
        {
            string report = await Task.Run(() => MouseDiagnostics.CreateReportAsync(_battery, timeout.Token), timeout.Token);
            return string.Join(Environment.NewLine, new[] { _refreshIssue, _settingsIssue, _startupIssue,
                    _history.PersistenceIssue, report }
                .Where(line => !string.IsNullOrWhiteSpace(line)));
        }
        finally
        {
            _deviceAccess.Release();
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode is not (PowerModes.Suspend or PowerModes.Resume))
            return;
        _app.Dispatcher.BeginInvoke(() =>
        {
            if (_disposed)
                return;
            _suspending = e.Mode == PowerModes.Suspend;
            if (_suspending)
            {
                _poll.Stop();
                _scan = null;
                Render();
            }
            else
            {
                QueueRefresh(followUpIfBusy: true);
            }
        });
    }
}
