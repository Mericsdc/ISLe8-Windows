# ISLe8'i bu bilgisayara paketli (MSIX kimliğiyle) kurar: bildirim aynası ve "Girişte başlat"ın
# başlangıç görevi bu kimliği ister. Zip'ten açılan ISLe8'de ikisi de yok.
#
# Gereken: Windows Ayarları › Sistem › Geliştiriciler için › Geliştirici Modu açık. Paket imzasız,
# klasöründen kaydedilir (imzalı .msix W6'da). Kurulan dosyalar artifacts\package-<rid> klasöründe
# kalır; o klasörü silme. Kaldırmak için: Get-AppxPackage com.lrxe8.isle8 | Remove-AppxPackage
# (ayarlar ve geçmiş %LOCALAPPDATA%\LRXe8\ISLe8'de kalır).
#   powershell -ExecutionPolicy Bypass -File scripts\install.ps1
#   powershell -ExecutionPolicy Bypass -File scripts\install.ps1 -NoAot
param(
    [ValidateSet('x64', 'ARM64')]
    [string]$Platform = $(if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'ARM64' } else { 'x64' }),
    [switch]$NoAot
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$rid = if ($Platform -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
$layout = Join-Path $root "artifacts\package-$rid"
$name = 'com.lrxe8.isle8'

# dotnet publish paketin bildirimini (AppxManifest.xml) yazmıyor; derleme yazıyor. Önce derlenir,
# sonra yayınlanır ve bildirim yayının yanına konur.
dotnet build "$root\src\ISLe8.App" -c Release -r $rid -p:Platform=$Platform
if ($LASTEXITCODE -ne 0) { throw "dotnet build başarısız oldu (çıkış kodu $LASTEXITCODE)." }
$manifest = Get-ChildItem "$root\src\ISLe8.App\bin\$Platform\Release" -Recurse -Filter AppxManifest.xml |
    Where-Object { $_.DirectoryName -like "*\$rid" } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $manifest) { throw 'Derleme AppxManifest.xml üretmedi.' }

$installed = Get-AppxPackage $name
if ($installed) {
    # Kapanan ISLe8 dosyalarını bir an daha tutar; klasör ancak süreç bitince silinebilir.
    Get-Process ISLe8 -ErrorAction SilentlyContinue | Stop-Process -PassThru | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
    # Başka bir klasörden kayıtlıysa önce o kaldırılır; Windows aynı paketi iki yerden kaydetmez.
    if ($installed.InstallLocation -ne $layout) { $installed | Remove-AppxPackage }
}

for ($try = 1; Test-Path $layout; $try++) {
    try { Remove-Item $layout -Recurse -Force -ErrorAction Stop }
    catch { if ($try -ge 5) { throw } else { Start-Sleep -Seconds 1 } }
}
$aot = if ($NoAot) { @('-p:PublishAot=false', '-p:SelfContained=true') } else { @() }
dotnet publish "$root\src\ISLe8.App" -c Release -r $rid -p:Platform=$Platform @aot -o $layout
if ($LASTEXITCODE -ne 0) { throw "dotnet publish başarısız oldu (çıkış kodu $LASTEXITCODE)." }
Get-ChildItem $layout -Recurse -Filter *.pdb | Remove-Item
Copy-Item $manifest.FullName $layout

Add-AppxPackage -Register (Join-Path $layout 'AppxManifest.xml') -ForceApplicationShutdown
$package = Get-AppxPackage $name
'{0} {1} kuruldu: {2}' -f $package.Name, $package.Version, $layout
Start-Process "shell:AppsFolder\$($package.PackageFamilyName)!App"
