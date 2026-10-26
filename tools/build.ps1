<#
    Release build for VulcansLivingWP.

    Uses the .NET SDK bundled in .toolchain (falls back to `dotnet` on PATH) and
    writes the public executable to src\FluxWall\bin\Release\net48\VulcansLivingWP.exe,
    which is exactly what installer\setup.iss packages.

    Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$dotnet = Join-Path $root '.toolchain\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

# Windows can expose a "dotnet" alias that resolves but cannot build anything when no
# SDK is installed, so check that the tool actually runs before using it.
$sdkOk = $false
try {
    $version = & $dotnet --version 2>$null | Select-Object -First 1
    $sdkOk = ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($version))
} catch { $sdkOk = $false }
if (-not $sdkOk) {
    throw ('The .NET SDK was not found. Install .NET SDK 6.0 or newer from ' +
           'https://dotnet.microsoft.com/download and run this script again.')
}

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

& $dotnet build (Join-Path $root 'src\FluxWall\FluxWall.csproj') -c $Configuration -v:m
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }

$exe = Get-Item (Join-Path $root ("src\FluxWall\bin\{0}\net48\VulcansLivingWP.exe" -f $Configuration))
Write-Output ("BUILD OK  {0}  {1:N0} bytes  {2}" -f $exe.Name, $exe.Length, $exe.LastWriteTime)
