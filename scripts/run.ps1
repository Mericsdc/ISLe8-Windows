# ISLe8'i Visual Studio olmadan, paketsiz (unpackaged) çalıştırır. Yalnızca .NET 10 SDK gerekir.
#   powershell -ExecutionPolicy Bypass -File scripts\run.ps1
#   powershell -ExecutionPolicy Bypass -File scripts\run.ps1 -Release
param([switch]$Release)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$platform = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'ARM64' } else { 'x64' }
$configuration = if ($Release) { 'Release' } else { 'Debug' }

# .NET 6/8 kurulu olup 10 olmayınca dotnet anlaşılmaz bir hata verir; önce açıkça söyle.
$sdks = if (Get-Command dotnet -ErrorAction SilentlyContinue) { @(dotnet --list-sdks) } else { @() }
if (-not ($sdks -match '^10\.')) {
    Write-Host '.NET 10 SDK bulunamadı. Kurmak için:' -ForegroundColor Yellow
    Write-Host '  winget install Microsoft.DotNet.SDK.10'
    Write-Host '  (ya da https://dotnet.microsoft.com/download/dotnet/10.0)'
    Write-Host 'Kurduktan sonra yeni bir PowerShell penceresi aç. Yalnızca kullanmak istiyorsan SDK gerekmez:'
    Write-Host 'GitHub › Releases sayfasındaki zip''i indirip ISLe8.exe''yi çalıştır.'
    exit 1
}

dotnet run --project "$root\src\ISLe8.App" -c $configuration -p:Platform=$platform -p:Unpackaged=true
