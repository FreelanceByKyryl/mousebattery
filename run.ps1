[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',

    [switch] $Diagnostics,

    [ValidateNotNullOrEmpty()]
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'MouseCharge needs Windows 10 version 2004 or later to run.'
}
if ($PSBoundParameters.ContainsKey('OutputPath') -and -not $Diagnostics) {
    throw '-OutputPath requires -Diagnostics.'
}
if ($OutputPath) {
    $OutputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
}

$projectDirectory = Join-Path $PSScriptRoot 'MouseCharge'
$project = Join-Path $projectDirectory 'MouseCharge.csproj'
$executable = Join-Path $projectDirectory "bin/$Configuration/net8.0-windows10.0.19041.0/MouseCharge.exe"

Push-Location -LiteralPath $PSScriptRoot
try {
    & dotnet build $project --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Pop-Location
}

if ($Diagnostics) {
    $arguments = '--diagnostics'
    if ($OutputPath) { $arguments += ' "' + $OutputPath + '"' }
    $process = Start-Process -FilePath $executable -WorkingDirectory $projectDirectory -ArgumentList $arguments -Wait -PassThru
    try {
        exit $process.ExitCode
    }
    finally {
        $process.Dispose()
    }
}
else {
    Start-Process -FilePath $executable -WorkingDirectory $projectDirectory
}
