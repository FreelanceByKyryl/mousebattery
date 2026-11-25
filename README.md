# MouseCharge

MouseCharge is a small Windows desktop widget for the battery level of a wireless mouse. It reads locally from Windows and the mouse. It needs no account, internet connection at runtime, background service, or administrator access.

## Run on Windows

Use Windows 10 version 2004 (build 19041) or later, or Windows 11. For development, install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). Visual Studio 2022 with the **.NET desktop development** workload is optional. If you prefer the IDE, open `MouseCharge.sln` and select **MouseCharge** as the startup project. Package restore needs access to NuGet when the required SDK packs are absent from the machine.

Run these commands from PowerShell in the repository.

```powershell
.\setup.ps1
.\run.ps1
```

Drag the widget to move it between monitors. Use the notification-area icon to show or hide the widget, refresh the battery, select the mouse, toggle always-on-top or startup, open Settings or Diagnostics, or exit. The icon may appear in the Windows tray overflow. Use the tray to change settings when the widget is click-through.

Settings include the refresh interval (30, 60, 120, or 300 seconds), low-battery warnings, appearance, opacity and size, position lock, and click-through. Settings and last readings are saved in `%LOCALAPPDATA%\MouseCharge`. **Start with Windows** registers the current executable for the current user. If you move the executable, toggle this setting off and on again from the new location.

## Battery support

| Connection | Battery source |
| --- | --- |
| Bluetooth LE | Battery Service (`0x180F`) and Battery Level (`0x2A19`) when Windows allows access |
| Windows HID mouse | Standard Battery Strength and Relative State of Charge reports when the driver exposes and permits reading them |
| USB wireless receiver | Standard HID battery reports when attributable to the mouse, or a battery property Windows publishes for the device |
| Xtrfy M4 Wireless | Battery and charging status through the USB receiver (`25A7:FA99`) or cable (`25A7:FA92`) |
| Windows device properties | OS-reported battery level, labeled approximate because Windows may cache or round it |

Actual support depends on the device and driver. Some USB receivers expose a generic mouse interface, while the mouse battery is accessible only through the manufacturer's software. Proprietary receiver protocols may therefore show **Battery unavailable**. Diagnostics lists the device identity as Windows reports it and explains which battery sources were available. MouseCharge uses read-only requests and does not change mouse settings, drivers, or firmware.

The Xtrfy provider sends only the documented battery and receiver connection queries. The community reference verified the protocol on mouse firmware 3.18. Compatibility with other firmware revisions depends on the receiver's HID interfaces and replies. [Protocol reference](https://github.com/Xurify/xtrfy-m4-control-software/blob/cf71362c06f0c8a911078298bd89af4046e821c6/README.md#protocol-notes)

When a refresh cannot retrieve a percentage, MouseCharge marks a last successful reading as **outdated**. The default interval is 60 seconds. A warning appears when a fresh reading is below the configured threshold (15% by default) once per low-battery period. Charging animation appears only when a provider reports that the mouse is charging.

## Build a standalone executable

```powershell
.\build.ps1 -Runtime win-x64
.\artifacts\win-x64\MouseCharge.exe
```

Choose `win-arm64` for ARM64 or `win-x86` for 32-bit x86 Windows. Copy the executable for the target architecture to a compatible Windows computer. The executable includes the .NET runtime.

## Install for everyday use

If MouseCharge is open, choose **Exit** from its notification-area menu. In PowerShell, run the installer from the repository folder.

```powershell
.\install.ps1 -StartWithWindows
```

The installer builds and copies the standalone executable to `%LOCALAPPDATA%\Programs\MouseCharge\MouseCharge.exe`, creates a Start menu shortcut, and launches MouseCharge in the tray with the widget hidden. You can close PowerShell after installation. The `-StartWithWindows` switch registers the installed copy to launch when you sign in. You can change startup later in the tray menu.

The tray icon and floating widget show an Xtrfy M4 Wireless silhouette on the left and the battery percentage on the right. Fresh readings are green and include the percent sign. A gray percentage is an older reading. Hover over the icon for the device name and reading status. If the icon is under the arrow next to the clock, drag it into the visible notification area or enable **MouseCharge** in Windows taskbar settings. Windows controls its position among the tray icons. Use **Show widget** in the tray menu for a larger illustration and readout.

To update, exit MouseCharge, get the updated source, and rerun the installer. The installer selects your Windows architecture by default. Use `-Runtime` to specify it or `-SkipBuild` to install the executable already present under `artifacts`.

## Run hardware diagnostics

Connect or wake the mouse and choose **Diagnostics** in the tray. You can also save the report from PowerShell.

```powershell
.\run.ps1 -Diagnostics -OutputPath .\MouseCharge-diagnostics.txt
Get-Content .\MouseCharge-diagnostics.txt
```

Use this command with the standalone build.

```powershell
Start-Process -FilePath .\artifacts\win-x64\MouseCharge.exe -ArgumentList '--diagnostics' -Wait
```

The standalone command above opens the report in Notepad. The report contains device names, hardware IDs, connection details, exposed battery properties and the outcome of each attempted read. Review it before sharing since it may include device identifiers.

## Automated checks

```powershell
dotnet run --project MouseCharge.Tests/MouseCharge.Tests.csproj -c Release
```

The automated checks exercise local settings, cached readings, notification thresholds, and parsing of Xtrfy M4 Wireless replies. Launch, rendering, tray behavior, and physical mouse detection require a Windows desktop to verify.
