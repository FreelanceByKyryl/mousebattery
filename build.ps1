[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64', 'win-x86')]
    [string] $Runtime = 'win-x64',

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'MouseCharge/MouseCharge.csproj'
$output = Join-Path $PSScriptRoot "artifacts/$Runtime"

Push-Location -LiteralPath $PSScriptRoot
try {
    & dotnet publish $project --configuration $Configuration --runtime $Runtime --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=embedded --output $output
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Pop-Location
}

Write-Output (Join-Path $output 'MouseCharge.exe')
