[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Install the .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0.'
}

Push-Location -LiteralPath $PSScriptRoot
try {
    $sdkVersion = & dotnet --version
    if ($LASTEXITCODE -ne 0 -or $sdkVersion -notlike '8.*') {
        throw 'The .NET 8 SDK is required. Install the latest .NET 8 SDK servicing release.'
    }

    & dotnet restore (Join-Path $PSScriptRoot 'MouseCharge.sln')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Pop-Location
}
