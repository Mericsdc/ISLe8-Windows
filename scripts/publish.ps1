# ISLe8'i yayınlar ve indirilebilir bir zip yapar: GitHub Release'teki dosyanın aynısı.
# Zip'in içinde ISLe8 klasörü var; çıkarıp ISLe8.exe'yi çalıştırmak yeterli, kurulum gerekmez.
#
# Varsayılan Native AOT: Visual Studio'nun "C++ ile masaüstü geliştirme" bileşenini (MSVC bağlayıcısı)
# ister. O yoksa -NoAot aynı klasörü JIT ile, kendi içinde .NET'iyle üretir (daha büyük, biraz daha ağır).
#   powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
#   powershell -ExecutionPolicy Bypass -File scripts\publish.ps1 -Platform ARM64
#   powershell -ExecutionPolicy Bypass -File scripts\publish.ps1 -NoAot
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
$name = "ISLe8-$version-$rid"
$artifacts = Join-Path $root $Output
$staging = Join-Path $artifacts $name

if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
$aot = if ($NoAot) { @('-p:PublishAot=false', '-p:SelfContained=true') } else { @() }

dotnet publish "$root\src\ISLe8.App" -c Release -r $rid -p:Platform=$Platform -p:Unpackaged=true @aot -o "$staging\ISLe8"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish başarısız oldu (çıkış kodu $LASTEXITCODE)." }

# Hata ayıklama sembolleri indirmeye girmez.
Get-ChildItem "$staging\ISLe8" -Recurse -Filter *.pdb | Remove-Item

$zip = Join-Path $artifacts "$name.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$staging\ISLe8" -DestinationPath $zip
'{0}  {1:N1} MB' -f $zip, ((Get-Item $zip).Length / 1MB)
