using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using MouseCharge.Devices;

namespace MouseCharge;

public partial class App : Application
{
    private Mutex? _instanceMutex;
    private DesktopController? _desktop;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length > 0 && e.Args[0] == "--diagnostics")
        {
            Shutdown(await RunCommandAsync(e.Args));
            return;
        }

        // Older installers used --tray in the Start menu and sign-in entries.
        if (e.Args.Any(argument => argument is not ("--tray" or "--start-with-windows")))
        {
            ShowUsage();
            Shutdown(2);
            return;
        }

        _instanceMutex = new Mutex(false, "MouseCharge.Desktop", out bool firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("MouseCharge is already running in the system tray.", "MouseCharge");
            Shutdown();
            return;
        }

        _desktop = new DesktopController(this);
        _desktop.Start(registerStartup: e.Args.Contains("--start-with-windows"));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _desktop?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static async Task<int> RunCommandAsync(string[] args)
    {
        if (args.Length > 2)
        {
            ShowUsage();
            return 2;
        }

        try
        {
            string path = args.Length == 2
                ? Path.GetFullPath(args[1])
                : Path.Combine(Path.GetTempPath(), "MouseCharge-diagnostics.txt");
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            string report = await MouseDiagnostics.CreateReportAsync(new MouseBatteryService(), timeout.Token);
            await File.WriteAllTextAsync(path, report, Encoding.UTF8, timeout.Token);
            if (args.Length == 1)
            {
                var editor = new ProcessStartInfo { FileName = "notepad.exe", UseShellExecute = false };
                editor.ArgumentList.Add(path);
                Process.Start(editor)?.Dispose();
            }
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or Win32Exception or OperationCanceledException)
        {
            if (args.Length == 1)
                MessageBox.Show(ex.Message, "MouseCharge diagnostics", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }

    private static void ShowUsage() => MessageBox.Show(
        "MouseCharge.exe [--start-with-windows]\nMouseCharge.exe --diagnostics [report path]",
        "MouseCharge");
}
