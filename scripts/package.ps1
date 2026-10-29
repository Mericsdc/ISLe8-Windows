# Kurulu (paketli) ISLe8'in güncelleme dosyasını yapar: install.ps1'in kaydettiği klasörün aynısı,
# AppxManifest.xml'iyle birlikte, bir zip'te (ISLe8-<sürüm>-<rid>-package.zip). Uygulamadaki
# Güncelle düğmesi bunu indirir, kendi klasörünün yerine koyar ve paketi yeniden kaydeder.
# Release iş akışı (release.yml) Native AOT ile çalıştırır.
#   powershell -ExecutionPolicy Bypass -File scripts\package.ps1
#   powershell -ExecutionPolicy Bypass -File scripts\package.ps1 -Platform ARM64 -NoAot
param(
    [ValidateSet('x64', 'ARM64')]
    [string]$Platform = $(if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'ARM64' } else { 'x64' }),
    [switch]$NoAot,
    [string]$Output = 'artifacts'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$rid = if ($Platform -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
$version = ([xml](Get-Content "$root\Directory.Build.props")).Project.PropertyGroup.Version
$name = "ISLe8-$version-$rid-package"
$artifacts = Join-Path $root $Output
$staging = Join-Path $artifacts $name

# dotnet publish paketin bildirimini yazmıyor; derleme yazıyor (install.ps1'deki gibi).
dotnet build "$root\src\ISLe8.App" -c Release -r $rid -p:Platform=$Platform
if ($LASTEXITCODE -ne 0) { throw "dotnet build başarısız oldu (çıkış kodu $LASTEXITCODE)." }
$manifest = Get-ChildItem "$root\src\ISLe8.App\bin\$Platform\Release" -Recurse -Filter AppxManifest.xml |
    Where-Object { $_.DirectoryName -like "*\$rid" } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $manifest) { throw 'Derleme AppxManifest.xml üretmedi.' }

if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
$aot = if ($NoAot) { @('-p:PublishAot=false', '-p:SelfContained=true') } else { @() }
dotnet publish "$root\src\ISLe8.App" -c Release -r $rid -p:Platform=$Platform @aot -o "$staging\ISLe8"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish başarısız oldu (çıkış kodu $LASTEXITCODE)." }
Get-ChildItem "$staging\ISLe8" -Recurse -Filter *.pdb | Remove-Item
Copy-Item $manifest.FullName "$staging\ISLe8"

$zip = Join-Path $artifacts "$name.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$staging\ISLe8" -DestinationPath $zip
'{0}  {1:N1} MB' -f $zip, ((Get-Item $zip).Length / 1MB)
