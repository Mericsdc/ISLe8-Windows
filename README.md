# ISLe8 for Windows

by **LRXe8**. macOS'taki ISLe8'in Windows sürümü: ekranın üst ortasında duran, üzerine gelince yay
animasyonuyla aşağı doğru açılan bir "Dynamic Island". C# + WinUI 3, .NET 10, Native AOT.

Plan, mimari ve kararlar: [DESIGN.md](DESIGN.md).

**Durum: W0 (iskelet).** Pill, hover'da açılma, DPI ölçekleme, monitör seçimi, tam ekranda gizlenme,
medya (SMTC), 7 tema, Türkçe/İngilizce. Kod Mac'te derleme denetiminden geçti ama **Windows'ta henüz
hiç çalıştırılmadı**. İlk çalıştırmada aşağıdaki test listesini izle.

---

## Windows bilgisayarda çalıştırma

### Gerekenler

- Windows 11 ya da Windows 10 2004+ (x64 veya ARM64)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- İsteğe bağlı: Visual Studio 2026, **"WinUI uygulama geliştirme"** iş yüküyle (.NET 10 için VS 2026
  gerekir; VS 2022 açmaz)

### Yol A: Visual Studio (paketli, MSIX)

1. `ISLe8.slnx`'i aç.
2. Üstteki platformu `x64` seç (ARM64 bilgisayarda `ARM64`).
3. Başlangıç projesi `ISLe8.App`, profil **ISLe8 (Package)** → F5.

Visual Studio paketi kendisi kaydeder; sertifika gerekmez.

### Yol B: Komut satırı (paketsiz)

Visual Studio gerekmez. PowerShell'de, depo klasöründe:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\run.ps1
```

Paketsiz çalışmada medya dahil W0'daki her şey çalışır. Bildirim aynası (W3) paket kimliği ister;
o fazdan sonra Yol A gerekir.

### Kapatma

Adaya sağ tıkla → **ISLe8'den çık**. (Görev çubuğu simgesi W1'de geliyor.)

### Günlük

Bir şey ters giderse günlük dosyası burada (yalnızca hata ve yaşam döngüsü satırları; şarkı adı,
pano ya da bildirim içeriği yazılmaz):

- Paketsiz: `%LOCALAPPDATA%\LRXe8\ISLe8\isle8.log`
- Paketli: `%LOCALAPPDATA%\Packages\com.lrxe8.isle8_*\LocalCache\Local\LRXe8\ISLe8\isle8.log`

Ayarlar aynı klasörde `settings.json`.

---

## W0 test listesi

Her maddeyi işaretle; olmayan varsa günlükle birlikte not et.

**Pencere**
- [ ] Ada, ana ekranın üst ortasında siyah/Ember renkli bir pill olarak duruyor; çevresinde siyah ya
      da beyaz bir kutu **yok**.
- [ ] Görev çubuğunda ve Alt+Tab'da ISLe8 görünmüyor.
- [ ] ISLe8 açılınca o an kullandığın pencere odağı kaybetmiyor.
- [ ] Ekranı kaplayan bir pencerenin pill'in **yanındaki** başlık çubuğuna ya da sekmesine tıklamak
      çalışıyor (tıklama adaya takılmıyor).

**Hover ve animasyon**
- [ ] İmleç pill'de ~150 ms durunca ada aşağı doğru, hafif esneyerek açılıyor.
- [ ] Pill'in üstünden hızla geçince açılmıyor.
- [ ] Pill'e tıklayınca hemen açılıyor.
- [ ] İmleç çıkınca ~250 ms sonra yumuşakça pill'e dönüyor.
- [ ] Açılış Mac'teki ISLe8'e göre belirgin yavaş ya da hızlı değil (değilse: DESIGN.md › Riskler 4).
- [ ] Açık adada bir düğmenin üzerinden adanın başka bir yerine geçmek adayı kapatmıyor.
- [ ] Sağ tık → **Açık tut** işaretliyken imleç çıkınca kapanmıyor.
- [ ] Sağ tık menüsü dışarı tıklayınca kapanıyor; kapanınca önceki pencerede yazmaya devam edilebiliyor.
- [ ] Dokunmatik ekran varsa: dokununca açılıyor, ~4 sn sonra kapanıyor.

**Medya** (Spotify, tarayıcıda YouTube, Medya Oynatıcı, Apple Music)
- [ ] Müzik çalınca pill genişliyor; solda kapak, sağda hareket eden ses çubukları.
- [ ] Duraklatınca çubuklar hemen duruyor, pill ~1,5 sn sonra daralıyor.
- [ ] Şarkı değişirken pill daralıp yeniden genişlemiyor.
- [ ] Çalan uygulamayı (ör. Spotify) tamamen kapatınca ada çökmeden boş duruma geçiyor.
- [ ] Açık adada kapak, başlık, "Sanatçı · Uygulama", ilerleme ve süreler doğru.
- [ ] Oynat/duraklat, sonraki, önceki çalışıyor.
- [ ] Kaydırıcıyla şarkıda ileri/geri gidilebiliyor (destekleyen uygulamalarda).
- [ ] Karışık çal / tekrarla destekleyen uygulamada çalışıyor, desteklemeyende soluk.
- [ ] Hiçbir şey çalmıyorken "Şu an bir şey çalmıyor" yazıyor.

**DPI ve monitörler** (Ayarlar › Sistem › Ekran › Ölçek)

| Ekran | Ölçek | Pill (px) | Açık ada (px) |
|---|---|---|---|
| 1080p | %100 | 200×32 | 560×160 |
| 1080p | %125 | 250×40 | 700×200 |
| 2K (1440p) | %100 / %125 / %150 | 200×32 / 250×40 / 300×48 | 560×160 / 700×200 / 840×240 |
| 4K | %150 / %175 / %200 | 300×48 / 350×56 / 400×64 | 840×240 / 980×280 / 1120×320 |

- [ ] Her ölçekte pill net (bulanık değil) ve gözle aynı fiziksel boyutta.
- [ ] ISLe8 açıkken ölçeği değiştirince pill birkaç saniye içinde yeni boyuta geçiyor.
- [ ] Sağ tık → **Ekran** → başka monitör: ada oraya geçiyor ve o monitörün ölçeğinde doğru boyutta.
- [ ] Farklı ölçekli iki monitör arasında gidip gelince boyut her seferinde doğru.
- [ ] Görev çubuğu üstteyse ada görev çubuğunun altında duruyor.
- [ ] Ekran kesme aracıyla bir ekran görüntüsü alıp pill'i ölçmek, tabloyu doğrulamanın en kolay yolu.

**Tam ekran**
- [ ] YouTube'u tam ekran yapınca (F11 ya da video tam ekranı) ada kayboluyor, çıkınca geri geliyor.
- [ ] Masaüstünü göster (Win+D) adayı gizlemiyor.

**Tema ve dil**
- [ ] Sağ tık → **Tema**: 7 temanın hepsi uygulanıyor (Ember varsayılan).
- [ ] Sağ tık → **Dil**: Türkçe ↔ English anında değişiyor.
- [ ] ISLe8'i kapatıp açınca tema, dil ve ekran seçimi korunuyor.

---

## Ölçüm

Gerçek sayılar için Release + Native AOT yayını kullan (Debug JIT ile çalışır ve daha ağırdır):

```powershell
dotnet publish src\ISLe8.App -c Release -r win-x64 -p:Platform=x64 -p:Unpackaged=true -o publish
publish\ISLe8.exe
```

ARM64 bilgisayarda `-r win-arm64 -p:Platform=ARM64`. Native AOT, Visual Studio'nun **"C++ ile masaüstü
geliştirme"** bileşenini (MSVC bağlayıcısı) ister.

ISLe8 açıldıktan en az 30 sn sonra, ada kapalıyken, fareye dokunmadan:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\measure.ps1 -Seconds 60
```

Müzik çalarken: `-Label muzik`. Hedefler DESIGN.md › Performans bütçesi'nde (boşta CPU < %0,1,
bellek < 80 MB).

---

## Bu Mac'te ne yapılabilir, ne yapılamaz

| İş | Mac | Windows |
|---|---|---|
| Core (yerleşim, DPI hesabı, tema JSON, ayarlar, medya modeli) derleme + testler | ✅ | ✅ |
| Uygulamanın bütün C# kodunu gerçek WinUI/Windows API'lerine karşı derleme | ✅ | ✅ |
| `.resw` metinlerinin TR/EN eşleşmesi ve koddaki her anahtarın varlığı (test) | ✅ | ✅ |
| PRI kaynakları, MSIX paketi, Native AOT | ❌ | ✅ |
| Uygulamayı çalıştırma: pencere, saydamlık, animasyon, DPI, medya, ölçüm | ❌ | ✅ |

Mac'te denetim:

```bash
brew install --cask dotnet-sdk
```

```bash
scripts/check-on-mac.sh
```

### Windows'ta test seçenekleri

1. **Kendi Windows bilgisayarın (seçilen yol).** Gerçek monitörler, gerçek DPI, gerçek ölçüm. Yukarıdaki
   test listesi bunun için.
2. **Mac'te sanal makine.** Parallels Desktop ya da VMware Fusion + Windows 11 ARM. ARM64 derlemesi
   çalışır; işlev testi için yeterli. DPI, sanal makinenin çözünürlüğü ve Windows ölçeği değiştirilerek
   denenebilir; Parallels birden çok sanal ekran verebilir. CPU/bellek ölçümü gerçek bir PC'yi
   temsil etmez.
3. **GitHub Actions (`windows-latest`).** Depo özel olarak
   [github.com/Mericsdc/ISLe8-Windows](https://github.com/Mericsdc/ISLe8-Windows) adresinde.
   `.github/workflows/windows.yml` yalnızca elle çalışır (Actions › windows › Run workflow): x64 ve
   ARM64 derler, Core testlerini çalıştırır, Native AOT ile yayınlayıp indirilebilir bir zip bırakır.
   Push'ta kendiliğinden çalışmaz, çünkü özel repolarda Windows dakikaları iki kat sayılır. Arayüzü
   test edemez; yalnızca derlenebilirliği kanıtlar.

---

## MSIX'i Visual Studio dışında kurmak (ileride, W6)

Paket, `Package.appxmanifest`'teki yayıncıyla (`CN=LRXe8`) aynı konulu bir sertifikayla imzalanmalı.
Kendi bilgisayarın için kendinden imzalı bir sertifika yeterli (PowerShell, yönetici):

```powershell
New-SelfSignedCertificate -Type Custom -Subject "CN=LRXe8" -KeyUsage DigitalSignature -FriendlyName "ISLe8 dev" -CertStoreLocation "Cert:\CurrentUser\My" -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
```

Sertifikayı "Güvenilen Kişiler" deposuna da ekledikten sonra Visual Studio › Paketle ve Yayımla ›
Uygulama Paketleri Oluştur ile paket üretilir. Ayrıntılar W6'da.

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
scripts/               run.ps1, measure.ps1, check-on-mac.sh, make-assets.swift
```
