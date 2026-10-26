# ISLe8 for Windows

by **LRXe8**. macOS'taki ISLe8'in Windows sürümü: ekranın üst ortasında duran, üzerine gelince yay
animasyonuyla aşağı doğru açılan bir "Dynamic Island". C# + WinUI 3, .NET 10, Native AOT.

**Durum: W0 (iskelet).** Pill, hover'da açılma, DPI ölçekleme, monitör seçimi, tam ekranda gizlenme,
medya (SMTC), 7 tema, Türkçe/İngilizce. İlk kez Windows 11'de çalıştırıldı ve düzeltildi
(2026-10-26); neyin denendiği [TESTING.md](TESTING.md)'de. Plan ve mimari: [DESIGN.md](DESIGN.md).

---

## İndir ve çalıştır

1. [Releases](https://github.com/Mericsdc/ISLe8-Windows/releases/latest) sayfasından işlemcine uyan
   zip'i indir: çoğu bilgisayar için **win-x64**, Snapdragon / ARM bilgisayarlar için **win-arm64**.
2. Zip'i bir klasöre çıkar (ör. `Belgeler\ISLe8`) ve **ISLe8.exe**'yi çalıştır. Kurulum ya da .NET
   gerekmez.
3. Windows "Windows kişisel bilgisayarınızı korudu" derse: **Ek bilgi › Yine de çalıştır**. Uygulama
   henüz imzalı değil (imzalama W6'da).

Gereken: Windows 10 2004+ ya da Windows 11.

### Kullanım

- İmleci üstteki pill'de ~150 ms tut ya da pill'e tıkla: ada açılır. İmleç çıkınca kapanır.
- Müzik çalarken (Spotify, tarayıcı, Medya Oynatıcı, Apple Music…) pill genişler; açık adada oynatıcı
  var.
- Sağ tık: **Açık tut**, **Tema**, **Ekran**, **Dil**, **ISLe8'den çık**.
- Oturum açılınca kendiliğinden başlasın istersen (W6'ya kadar elle): ISLe8.exe'ye bir kısayol
  oluştur, Win+R › `shell:startup` ile açılan klasöre koy.

### Günlük ve ayarlar

Bir şey ters giderse günlük dosyası burada (yalnızca hata ve yaşam döngüsü satırları; şarkı adı,
pano ya da bildirim içeriği yazılmaz):

- Zip'ten ya da `scripts\run.ps1` ile: `%LOCALAPPDATA%\LRXe8\ISLe8\isle8.log`
- Paketli (MSIX): `%LOCALAPPDATA%\Packages\com.lrxe8.isle8_*\LocalCache\Local\LRXe8\ISLe8\isle8.log`

Ayarlar aynı klasörde `settings.json`.

---

## Geliştirme

### Gerekenler

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0): `winget install Microsoft.DotNet.SDK.10`.
  Yalnızca .NET 6/8 kuruluysa derleme açılmaz; `global.json` bunu açıkça söyler.
- Native AOT yayını için: Visual Studio 2026 ya da Build Tools, **"C++ ile masaüstü geliştirme"**
  bileşeniyle (MSVC bağlayıcısı). Yoksa `-NoAot` kullan ya da yayını GitHub Actions'a bırak.
- İsteğe bağlı: Visual Studio 2026, **"WinUI uygulama geliştirme"** iş yüküyle (VS 2022 .NET 10'u açmaz).

### Çalıştırma

Visual Studio gerekmez. PowerShell'de, depo klasöründe:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\run.ps1
```

Visual Studio ile (paketli, MSIX): `ISLe8.slnx`'i aç, platformu `x64` (ARM64 bilgisayarda `ARM64`) seç,
profil **ISLe8 (Package)** › F5. Bildirim aynası (W3) paket kimliği ister; o fazdan sonra bu yol gerekir.

### Yayın (zip)

```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
```

`artifacts\ISLe8-<sürüm>-win-x64.zip` üretir; Releases'teki dosyanın aynısı. `-Platform ARM64` ARM için,
`-NoAot` C++ araçları olmayan bir bilgisayarda (JIT, kendi .NET'iyle; ~65 MB).

### Sürüm yayınlama

1. `Directory.Build.props` › `Version` ve `src/ISLe8.App/Package.appxmanifest` › `Version`'ı artır.
2. Etiketle ve gönder:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

`.github/workflows/release.yml` testleri çalıştırır, x64 ve ARM64 için Native AOT ile derler ve zip'leri
o etiketin GitHub Release'ine koyar. `windows.yml` yalnızca elle çalışan bir derleme denetimi; ikisi de
her push'ta çalışmaz, çünkü özel repolarda Windows dakikaları iki kat sayılır.

### Ölçüm

Gerçek sayılar için Release + Native AOT yayınını kullan (Debug JIT ile çalışır ve daha ağırdır).
ISLe8 açıldıktan en az 30 sn sonra, ada kapalıyken, fareye dokunmadan:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\measure.ps1 -Seconds 60
```

Müzik çalarken: `-Label muzik`. Hedefler DESIGN.md › Performans bütçesi'nde (boşta CPU < %0,1,
bellek < 80 MB).

### MSIX imzalama (ileride, W6)

Paket, `Package.appxmanifest`'teki yayıncıyla (`CN=LRXe8`) aynı konulu bir sertifikayla imzalanmalı.
Kendi bilgisayarın için kendinden imzalı bir sertifika yeterli (PowerShell, yönetici):

```powershell
New-SelfSignedCertificate -Type Custom -Subject "CN=LRXe8" -KeyUsage DigitalSignature -FriendlyName "ISLe8 dev" -CertStoreLocation "Cert:\CurrentUser\My" -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
```

Sertifikayı "Güvenilen Kişiler" deposuna da ekledikten sonra Visual Studio › Paketle ve Yayımla ›
Uygulama Paketleri Oluştur ile paket üretilir. Ayrıntılar W6'da.

### Mac'te denetim

Uygulama Mac'te çalışmaz, ama Core testleri ve uygulamanın bütün C# kodunun gerçek WinUI/Windows
API'lerine karşı derlenmesi Mac'te de denetlenebilir (PRI, MSIX ve Native AOT yalnızca Windows'ta):

```bash
brew install --cask dotnet-sdk
```

```bash
scripts/check-on-mac.sh
```

---

## macOS sürümüyle ilişki

Aynı ürün, ayrı kod. Mac sürümü Swift/SwiftUI, bu sürüm C#/WinUI; ortak kod satırı yok, çünkü işin
çoğu platformun kendi API'si (MediaRemote ↔ SMTC, notch ↔ pill, EventKit ↔ AppointmentStore).
Ortak olan sözleşme: aynı 7 tema ve aynı tema JSON'u (Mac'te dışa aktarılan tema burada açılır), aynı
yay değerleri, aynı Türkçe/İngilizce metinler, aynı faz adları. Birinde bunlardan biri değişince
diğerine de taşınır.

Mac'teki sistemlerin buraya nasıl taşınacağı, modül modül Mac kaynak dosyaları, davranış ayrıntıları,
eksikler ve kabul kriterleriyle [PORTING.md](PORTING.md)'de.

---

## Klasörler

```
src/ISLe8.Core         platformdan bağımsız: yerleşim/DPI, yaylar, temalar, ayarlar, medya modeli, günlük
src/ISLe8.App          WinUI 3 uygulaması (XAML dosyası yok)
  Shell/               pencere, saydamlık, monitörler, tam ekran
  Island/              ada denetleyicisi, görünüm, Composition yüzeyi
  Media/               SMTC servisi, oynatıcı, ses çubukları
  Interop/             Win32 çağrıları (LibraryImport, AOT uyumlu)
  Strings/             tr-TR ve en-US .resw
tests/ISLe8.Core.Tests xUnit; Mac'te de çalışır
scripts/               run.ps1, publish.ps1, measure.ps1, check-on-mac.sh, make-assets.swift
.github/workflows/     release.yml (etiketle yayın), windows.yml (elle derleme denetimi)
```

---

## Lisans

Kod [Apache License 2.0](LICENSE) ile lisanslı. **ISLe8** ve **LRXe8** adları, logoları ve uygulama simgesi bu lisansa dahil değil; bir çatal dağıtacaksan kendi adını ve simgesini kullan ([TRADEMARKS.md](TRADEMARKS.md)). Telif ve üçüncü taraf bildirimleri [NOTICE](NOTICE) dosyasında.

Windows, Microsoft Corporation'ın; Apple ve Dynamic Island, Apple Inc.'in markalarıdır. ISLe8 bağımsız bir projedir.
