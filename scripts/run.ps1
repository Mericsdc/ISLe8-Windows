# ISLe8'i Visual Studio olmadan, paketsiz (unpackaged) çalıştırır. Yalnızca .NET 10 SDK gerekir.
#   powershell -ExecutionPolicy Bypass -File scripts\run.ps1
#   powershell -ExecutionPolicy Bypass -File scripts\run.ps1 -Release
param([switch]$Release)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$platform = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'ARM64' } else { 'x64' }
$configuration = if ($Release) { 'Release' } else { 'Debug' }

dotnet run --project "$root\src\ISLe8.App" -c $configuration -p:Platform=$platform -p:Unpackaged=true
