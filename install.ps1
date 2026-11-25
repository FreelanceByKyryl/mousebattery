[CmdletBinding()]
param(
    [switch] $StartWithWindows,

    [ValidateSet('win-x64', 'win-arm64', 'win-x86')]
    [string] $Runtime,

    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'Run this installer on Windows 10 version 2004 or later, or on Windows 11.'
}

if (-not $Runtime) {
    $Runtime = switch ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture) {
        'X64' { 'win-x64' }
        'Arm64' { 'win-arm64' }
        'X86' { 'win-x86' }
        default { throw 'Unsupported Windows architecture. Specify a supported -Runtime.' }
    }
}

$localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$installDirectory = Join-Path $localAppData 'Programs/MouseCharge'
$installedExe = Join-Path $installDirectory 'MouseCharge.exe'

function Assert-MouseChargeClosed {
    $sessionId = (Get-Process -Id $PID).SessionId
    $processes = Get-Process -Name 'MouseCharge' -ErrorAction SilentlyContinue |
        Where-Object { $_.SessionId -eq $sessionId }
    foreach ($process in $processes) {
        $runningPath = $process.Path
        if ($runningPath -and $runningPath -ine $installedExe) {
            $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($runningPath)
            if ($versionInfo.ProductName -ine 'MouseCharge') { continue }
        }
        throw 'MouseCharge is already open. Choose Exit in its notification-area menu and run the installer again.'
    }
}

Assert-MouseChargeClosed
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1') -Runtime $Runtime
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$publishedExe = Join-Path $PSScriptRoot "artifacts/$Runtime/MouseCharge.exe"
if (-not (Test-Path -LiteralPath $publishedExe -PathType Leaf)) {
    throw "MouseCharge.exe was not found at $publishedExe. Run the installer without -SkipBuild."
}

$stagedExe = Join-Path $installDirectory ('MouseCharge.{0}.tmp' -f [Guid]::NewGuid())

Assert-MouseChargeClosed
[void][IO.Directory]::CreateDirectory($installDirectory)
try {
    Copy-Item -LiteralPath $publishedExe -Destination $stagedExe
    if ([IO.File]::Exists($installedExe)) {
        [IO.File]::Replace($stagedExe, $installedExe, [NullString]::Value)
    }
    else {
        [IO.File]::Move($stagedExe, $installedExe)
    }
}
finally {
    [IO.File]::Delete($stagedExe)
}

$programs = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
$shortcutPath = Join-Path $programs 'MouseCharge.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $installedExe
$shortcut.WorkingDirectory = $installDirectory
$shortcut.Arguments = ''
$shortcut.Description = 'MouseCharge mouse battery monitor'
$shortcut.IconLocation = "$installedExe,0"
$shortcut.Save()

$launch = @{
    FilePath = $installedExe
    WorkingDirectory = $installDirectory
}
if ($StartWithWindows) {
    $launch.ArgumentList = '--start-with-windows'
}
Start-Process @launch
Write-Output "Installed MouseCharge to $installedExe. Look for the battery percentage in the notification area."
