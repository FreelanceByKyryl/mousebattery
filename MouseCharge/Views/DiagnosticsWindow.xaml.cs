using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace MouseCharge.Views;

internal partial class DiagnosticsWindow : Window
{
    private readonly Func<CancellationToken, Task<string>> _createReport;
    private CancellationTokenSource? _scan;
    private bool _closed;

    public DiagnosticsWindow(Func<CancellationToken, Task<string>> createReport)
    {
        _createReport = createReport;
        InitializeComponent();
        Loaded += Refresh;
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _scan?.Cancel();
        base.OnClosed(e);
    }

    private async void Refresh(object sender, RoutedEventArgs e)
    {
        if (_scan is not null || _closed)
            return;

        using var scan = new CancellationTokenSource();
        _scan = scan;
        RefreshButton.IsEnabled = false;
        Status.Text = "Scanning connected devices…";
        try
        {
            var report = await _createReport(scan.Token);
            if (_closed)
                return;

            Report.Text = report;
            Report.ScrollToHome();
            CopyButton.IsEnabled = report.Length > 0;
            Status.Text = "Scan complete.";
        }
        catch (OperationCanceledException) when (scan.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!_closed)
                Status.Text = $"Scan failed. {exception.Message}";
        }
        finally
        {
            _scan = null;
            if (!_closed)
                RefreshButton.IsEnabled = true;
        }
    }

    private void CopyReport(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(Report.Text);
            Status.Text = "Report copied.";
        }
        catch (ExternalException)
        {
            Status.Text = "The clipboard is busy. Try copying again.";
        }
    }
}
