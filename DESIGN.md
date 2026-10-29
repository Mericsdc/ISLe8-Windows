# ISLe8 for Windows: Tasarım Planı

> Hedef: macOS'taki ISLe8'in (by LRXe8) Windows sürümü. Windows'ta notch olmadığı için ada,
> ekranın üst ortasında bir **pill** olarak duruyor; üzerine gelince yay animasyonuyla aşağı doğru
> büyüyüp açılıyor. 1080p, 2K ve 4K monitörlerde, %100–%200 ölçeklerde aynı boyutta görünüyor.
> Hafiflik önce gelir: boştayken ~%0 CPU, düşük bellek.

---

## 1. İlkeler

1. **Native:** C# + WinUI 3 (Windows App SDK), .NET 10, Native AOT. WebView, Electron, arka plan
   sunucusu yok.
2. **Olay güdümlü:** Her kaynak sistem olayıyla beslenir (SMTC, `WM_DPICHANGED`, AppBar bildirimi,
   ileride `WM_CLIPBOARDUPDATE`, `IAudioEndpointVolumeCallback`…). Polling yok.
3. **Kapalı modül = sıfır maliyet:** Kapatılan modülün olay aboneliği ve zamanlayıcısı kalmaz.
4. **Görünmeyen çizilmez:** Ada kapalıyken oynatıcı görsel ağaçtan çıkar (`Visibility.Collapsed`),
   saati durur. Animasyonlar Composition'da; UI iş parçacığı kare başına iş yapmaz.
5. **Mac ile ortak dil:** Aynı 7 tema, aynı tema JSON'u, aynı yay değerleri, aynı Türkçe/İngilizce
   metinler.

### Performans bütçesi (macOS ile aynı)

| Durum | Hedef |
|---|---|
| Boşta (ada kapalı, müzik yok) | CPU < %0,1 |
| Müzik çalıyor, ada kapalı (ses çubukları) | CPU < %1 |
| Ada açıkken | CPU < %3 (anlık) |
| Bellek (özel çalışma kümesi) | < 80 MB |

Ölçüm: `scripts/measure.ps1`, Release + Native AOT yayınıyla (README › Ölçüm).

---

## 2. Teknoloji kararı

**Seçilen: C# + WinUI 3, .NET 10 LTS, Native AOT, MSIX** (kullanıcı onayı, 2026-10-26).

| Seçenek | Bellek (tahmin) | Boşta CPU | Neden |
|---|---|---|---|
| **C# + WinUI 3** ✅ | ~50–70 MB | ~%0 | Gereken bütün Windows API'leri (SMTC, UserNotificationListener, GATT, AppointmentStore, Share UI) C#'ta birinci sınıf. Composition yayları UI iş parçacığı dışında. |
| C++/WinRT + WinUI 3 | ~30–45 MB | ~%0 | 15–25 MB daha az, ama geliştirme 2–3 kat yavaş. |
| Rust + Tauri | 150 MB+ | düşük | WebView2 birden çok süreç açar; hafiflik hedefine ters. |
| Rust + WinUI | — | — | XAML araçları yok, arayüz elle çizilir. |

Bellek tahmindir; W0'ın ilk Windows çalıştırmasında ölçülecek.

### XAML dosyası yok

Arayüz C# ile kuruluyor (`Ui.Text`, `Ui.IconButton`, `IslandView`…). Nedenleri:

- **Mac'te derleme denetimi:** XAML derleyicisi yalnızca Windows'ta çalışır. XAML dosyası olmayınca
  aynı C# kodu Mac'te gerçek WinUI/Windows API'lerine karşı derlenebiliyor
  (`scripts/check-on-mac.sh`). Windows olmadan yapılabilecek en güçlü denetim bu.
- **Native AOT:** XAML'ın yansıma tabanlı tip bilgisi yok; kırpma (trimming) uyarısı çıkmıyor.
- `App`, `IXamlMetadataProvider`'ı WinUI kontrollerinin kendi meta verisine yönlendiriyor;
  kontrol stilleri `XamlControlsResources` ile kodda ekleniyor.

---

## 3. Pencere ve ada

```
 monitörün üst kenarı (ya da üstteki görev çubuğunun altı)
┌──────────────────────── pencere (sabit boyut) ───────────────────────┐
│ gölge payı │            ╭──── pill 200×32 ────╮           │ gölge payı │  ← fare bölgesi: pill + üst kenara kadar
│            │            ╰─────────────────────╯           │            │
│            │   ┌──────── açık ada (W0: 560×160) ───────┐  │            │  ← açıkken fare bölgesi: pencerenin tamamı
│            │   │ [kapak]  Başlık / Sanatçı · Spotify   │  │            │
│            │   │          ━━━━━━━●──────────────────   │  │            │
│            │   │  1:23    ⤮  ⏮  ⏯  ⏭  ⟳       -2:45   │  │            │
│            │   └───────────────────────────────────────┘  │            │
└──────────────────────────────────────────────────────────────────────┘
```

- **Pencere:** kenarlıksız (`WS_POPUP`), her zaman üstte, görev çubuğunda ve Alt+Tab'da yok
  (`WS_EX_TOOLWINDOW`, `IsShownInSwitchers = false`), odak almaz (`WS_EX_NOACTIVATE`,
  `AppWindow.Show(false)`).
- **Saydamlık:** WinUI'nin sistem arka planı tamamen saydam bir fırça (`TransparentBackdrop`); DWM
  boş bir bölge üzerinde blur-behind ile piksel başına alfayı uyguluyor. Windows 11'in köşe
  yuvarlama ve kenarlığı kapalı.
- **Sabit pencere, değişen bölge:** Pencere açık ada + gölge kadar büyük ve animasyon sırasında hiç
  boyut değiştirmiyor (Mac'teki Faz 7 hatasının dersi). Fareyi hangi kısmın alacağına `SetWindowRgn`
  karar veriyor: kapalıyken yalnızca pill (+2 DIP pay, ekranın üst kenarına kadar uzatılmış, fareyi
  yukarı fırlatınca da isabet etsin diye), açıkken pencerenin tamamı. Pill'in yanına yapılan tıklama
  alttaki pencereye (ör. tarayıcı sekmesine) geçiyor. Bedeli: ada açıkken çevresindeki 36 DIP'lik
  gölge payına yapılan tıklama alttaki pencereye geçmiyor (bölge gölgeyi de kırptığı için).
- **Şekil Composition'da:** Tek bir `CompositionRoundedRectangleGeometry` pill ile açık ada arasında
  yaylanıyor; aynı değerlerle ikinci bir geometri içeriği kırpıyor. XAML düzeni yalnızca monitör ya
  da DPI değişince çalışıyor, animasyon sırasında hiç çalışmıyor.
- **Gölge:** Boş bir `SpriteVisual`'ın `DropShadow`'u; maskesi şeklin canlı kopyası
  (`CompositionVisualSurface`), böylece gölge yayla birlikte büyüyor. Yalnızca açıkken görünür.
  `LayerVisual` + `DropShadow` bu pencerede pill'in üstüne opak siyah bir kutu çiziyor (Riskler 1).
- **Tam ekran:** Pencere boşluk ayırmayan bir AppBar olarak kayıtlı; kabuk, tam ekran bir uygulama
  (oyun, video, sunum) açılıp kapanınca `ABN_FULLSCREENAPP` gönderiyor. Adanın monitöründeyse ada
  gizleniyor. Polling yok.

### Hover

Kurallar Mac'le aynı (PORTING.md §2) ve `ISLe8.Core.Island.IslandModel`'de; uygulama yalnızca olayları
iletip durumu çiziyor. Süreler testlerde elle ilerletilen bir saatle sınanıyor.

| Olay | Davranış |
|---|---|
| İmleç pill'e girdi (üzerine gelince modu) | Açılma gecikmesinden sonra (varsayılan 0, ayar 0–1 sn) doğrudan açılır; ara aşama yok. Gecikme 0 olsa da imleç en az 50 ms durmalı: üstünden geçen imleç açmaz |
| İmleç pill'e girdi (tıklayınca modu) | 60 ms sonra pill hafifçe büyür (peek: +18 × 6 DIP); tıklayınca açılır |
| Pill'e tıklandı | Her iki modda hemen açılır |
| İmleç adadan çıktı | Açıksa 0,35 sn sonra kapanır (ayar 0–1,5 sn), peek ise 50 ms'de geri döner |
| Hover alanı | Yalnızca adanın kendisi (pill, peek ya da açık ada); pencerenin geri kalanı sayılmaz. Her giriş, çıkış ve hareket olayı `GetCursorPos` ile doğrulanıyor: adanın içindeki bir düğmeden çıkmak adadan çıkmak sayılmıyor, kapanırken büyük kalan pencere bölgesine girmek de adaya girmek sayılmıyor |
| Dışarı tıklama | Ada açıkken ve imleç üstünde değilken başka yere tıklamak adayı hemen kapatır (sabitliyse kapatmaz). Ada odak almadığı için Windows bunu bildirmiyor; düşük seviyeli bir fare kancası yalnızca bu durumdayken kurulu (`OutsideClickWatcher`) |
| Dokunma | Dokununca açılır; parmak kalkınca 4 sn açık kalır |
| Müzik durdu | Pill 8 sn sonra daralır (Mac: MediaModule.swift); kısa bir duraklatma ya da şarkı geçişi pill'i titretmez |
| Banner | Pill'den büyür, süresi bitince geri döner (şarkı 2,5 sn). İmleç üstündeyken bekler, çıkınca 1,2 sn sonra kapanır; üstünde durmak adayı açmaz, tıklamak açar. Ada açıkken, imleç üstündeyken ya da menü açıkken gelmez; yeni banner eskisinin yerini ve süresini alır |
| Sağ tık | Menü: Açık tut, Tema, Ekran, Dil, Çık. Menü açıkken ada ne açılır ne kapanır; kapanınca imleç dışarıdaysa ada kapanma gecikmesiyle kapanır. Menü açılırken ada geçici olarak öne alınıyor (dışarı tıklayınca kapansın diye), kapanınca odak önceki pencereye dönüyor. Mac'te bu menü yok (sabitleme ve ayarlar başlıktaki düğmelerde); Windows alışkanlığı olduğu için duruyor |

**Gecikme kararı:** W0'da Windows'a özgü 150 ms seçilmişti (üst ortada başlık çubukları ve tarayıcı
sekmeleri var diye). PORTING.md §4 ile Mac'teki 0 + 50 ms'ye dönüldü; 50 ms'lik süzme, pill'in
üstünden geçen imleci zaten eliyor (TESTING.md › W1 adım 1). Rahatsız ederse tek bir ayar.

### Açık ada (W1)

PORTING.md §2 › Boyutlar; değerler `IslandSizes`'ta, Core testleriyle.

| Düzen | Genişlik | İçerik | Açık ada |
|---|---|---|---|
| Geniş | 820 (ayar 680–980) | 304 | 820 × 396 |
| Kompakt | şeridin ihtiyacı + 48, en az 640 (varsayılan sütunlarla 870) | Home 100, diğerleri 196 | 870 × 192 / 288 |
| Minimal | şeridin ihtiyacı + 48, en az 600 (varsayılan sütunlarla 720) | Home 64, diğerleri 196 | 720 × 156 / 288 |

- **Yükseklik:** başlık satırı (pill kadar, 32) + 6 + içerik + dock alanı (8 + 36 + 10). İçerik yanlardan 14.
- **Pencere:** o düzenin en büyük sekmesine göre bir kez boyutlanıyor; sekme değişince pencere değil
  yalnızca şekil yaylanıyor. Düzen ya da genişlik ayarı değişince pencere yeniden yerleşiyor.
- **Köşeler:** açık adada `min(tema köşesi + 12, 28)`; pill ve peek tam yuvarlak.
- **Zemin:** ada siyah; açıkken ve banner'da tema siyahın **üstüne** açılır (0,22 sn), kapanırken söner
  (0,16 sn). Siyah hiç değişmediği için geçişin ortasında ada yarı saydam görünmez; yalnızca cam teması
  açıkken siyahı söndürür (Mac: `IslandBackground`). Kapalı pill her temada siyah.
- **Sabit çerçeve:** her durumun görünümü (pill'in kapak ve çubukları, banner, açık ada) kendi son
  çerçevesinde, pencerede ortalı durur; animasyonda yalnızca zemin ve kırpma şekli büyür.
- **Başlık satırı:** solda durum çipleri (pil 8. adımda), sağda sabitle ve ayarlar. Ayarlar düğmesi
  pencereyi açıp adayı kapatıyor (Mac'teki gibi).
- **Dock:** altta, tema yüzeyi renginde bir kapsül; seçili sekme vurgu renginin %20'siyle dolu. Düğmeler
  `Button` değil, düz `Border`: ada odak almıyor ve seçime bağlı görünüm, `Button`'ın görsel
  durumlarıyla çatışırdı.
- **Sekmeler:** Home, Raf, Pano, AI, Cihazlar (Mac'in kimlikleri). W1'de yalnızca Home dolu; ötekiler
  "Windows'a yakında geliyor" diyor. Geçiş 0,18 sn'lik çapraz solma; Geniş'te ada boyut değiştirmiyor,
  Kompakt ve Minimal'de Home şeridi ile öteki sekmeler arasında yaylanıyor. 15 sn içinde yeniden
  açılınca son sekme, sonra ilk sekme.
- **Küçülürken açık kalma:** Kompakt ya da Minimal'de Home'a dönünce ada kısalıyor ve dock'a tıklayan imleç birden
  altında kalıyor. `HoverOutline` eski çerçeveyi, imleç yeni çerçeveye girene ya da ikisinden de
  çıkana kadar "üstünde" sayıyor.
- **İki parmakla kaydırma:** Hassas dokunmatik yüzey yatay tekerlek olayı yolluyor, hareketin başı sonu
  yok; `SwipeTracker` bir tekerlek çentiğini (120) bir sekme sayıyor, 250 ms duraklama yeni bir
  kaydırma başlatıyor. Raf ve Pano'da yalnızca başlık ve dock satırı.
- **Home sütunları:** Ayarlar › Düzen'deki sütunlar soldan sağa, tam yükseklikte (Mac'in HomeView.swift
  değerleri, `HomeColumns` ve `HomeStrip`). Sütun yoksa "Home boş…" yazıyor. Takvim, hava ve
  zamanlayıcı kendi adımlarına kadar "Windows'a yakında geliyor".
  - **Geniş:** her sütun bir kart (tema yüzeyi, kenarlığı, köşesi), aralık 10; genişlik ağırlıkla: takvim
    1, medya 1,1, hava 1, zamanlayıcı 0,62.
  - **Kompakt ve Minimal şeritleri:** kart yok; sütunlar 1'lik ince çizgilerle ayrılıyor (iki yanında 10).
    Her sütunun ihtiyacı: Kompakt takvim 270, medya 240, hava 270, zamanlayıcı 120; Minimal 170, 250,
    190, 130. Oynatıcı ortada, pill'in altında; iki yanındaki sütunlar eşit yarıları paylaşıyor. İki yan
    80'den fazla dengesizse (zamanlayıcı bir yanda) ya da oynatıcı uçtaysa sütunlar yan yana diziliyor.
    Şerit adanın iki yanından 24 içeride (Mac'in omuz ve kenar payı); ada genişliği şerit + 48.
- **Medya sütunu:**
  - **Geniş:** kapak 104 (gölgeli), başlık ve sanatçı iki satıra kadar, ilerleme çubuğu (4, sürüklerken
    6; bırakınca atlar), karışık çal · önceki · halkalı oynat (52) · sonraki · tekrarla, hepsi kartın
    yüksekliğine yayılmış.
  - **Kompakt:** kapak 64 (gölgeli), yanında başlık, sanatçı ve aynı beş düğme; oynat 34, halkası vurgu
    renginde. İlerleme çubuğu yok.
  - **Minimal:** tek satır: kapak 38, başlık ve sanatçı, önceki · oynat (dolu daire) · sonraki.
  - Saat yalnızca Home görünürken ve çalarken, yarım saniyede bir işliyor; ada kapalıyken medya olayları
    Home'u çizmiyor, açılınca yetişiyor.
- **Banner:** 340 × 78; üst satır (pill kadar) boş, içerik altında; köşe 20 (Mac'in alt köşesi). Şarkı
  banner'ı: kapak 30, başlık, sanatçı, sağda çubuklar (çubuk ayarından bağımsız, Mac'teki gibi).

### Üstte ayrılan şerit

- Mac'te ada menü çubuğunun içinde, pencerelerin dışında. Windows'ta böyle bir yer yok; ada pencerelerin
  (tam ekran tarayıcının sekmelerinin) üstüne biniyordu. Ada bu yüzden görev çubuğu gibi bir uygulama çubuğu
  (`SHAppBarMessage`, `ABM_SETPOS`, üst kenar) olarak ekranın üstünde 44 DIP'lik bir şerit ayırıyor: pill'in kenar
  boşluğu, yüksekliği ve peek büyümesi. Tam ekran pencereler şeridin altından başlıyor; açık ada yine pencerelerin
  üstüne açılıyor. Üstte başka bir çubuk (üstteki görev çubuğu) varsa şerit onun altına giriyor. Ayarlar › Genel
  › "Pencereler adanın altında başlasın" ile kapatılabiliyor.

### Paket ve bildirim aynası

- **Paket:** Tam MSIX (`com.lrxe8.isle8`). Bildirim aynası ve "Girişte başlat"ın başlangıç görevi paket kimliği
  istiyor. Paketin dosya ve kayıt defteri sanallaştırması kapalı (`desktop6:FileSystemWriteVirtualization`,
  `unvirtualizedResources`): ayarlar ve geçmiş zip sürümüyle aynı klasörde. Paketsiz (zip) sürüm de çalışıyor;
  orada Ayarlar › Bildirimler "Kurulu sürüm gerekiyor" diyor. Yerel kurulum `scripts\install.ps1` (Geliştirici
  Modu); imzalı .msix W6'da.
- **Bildirim aynası** (`NotificationService`, Mac: NotificationModule.swift): Mac Bildirim Merkezi'nin veritabanını
  okuyor; Windows'ta `UserNotificationListener` var. Yeni bildirim `NotificationChanged` olayıyla geliyor, arada
  hiçbir şey çalışmıyor. Toast'un ilk satırı başlık, kalanı tek satır (Mac'te alt başlık · gövde). Gmail gibi siteler
  tarayıcının bildirimiyle geliyor.
- **Banner:** Mac'teki gibi 400 × (pill + 74): uygulama simgesi (34), adı ve "+N", başlık, iki satır. Üzerine gelince
  süre duruyor; tıklayınca gönderen uygulamanın açık penceresi öne geliyor (pencerenin `System.AppUserModel.ID`'si),
  yoksa uygulama AppUserModelID'siyle başlatılıyor. Toplantı banner'ı da Mac'teki gibi bu boyutta.
- **Kuyruk** (`IslandModel.ShowNotification`): banner varken ya da ada açıkken gelenler en fazla 5 bekliyor, ada
  dinlenince sıradaki çıkıyor. Oturum kilitlenince (`WTSRegisterSessionNotification`) bekleyenler siliniyor, banner çıkmıyor.
- **Rahatsız etmeyin:** Windows bunu başka uygulamalara bir API ile vermiyor; kabuğun yayımladığı WNF durumu
  (`WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED`, 0 = kısıtsız) okunuyor. Elle, zamanlamayla ya da odak oturumuyla açılanı görüyor.

### Raf sekmesi

- **Bırakma hedefi:** Mac'te dosya sürüklemesi notch'a yaklaşınca algılanıyor; Windows'ta dosya pill'in üstüne
  gelince (`DragEnter`) ada 460 × 144'lük bırakma hedefine açılıyor: boş pill satırının altında "Rafa bırak"
  ve "Paylaş" (Mac'te AirDrop). Sürükleme adadan çıkınca pill'e dönüyor. Rafa bırakılınca Raf sekmesi açılıyor.
- **Raf** (`ShelfStore`, Mac: ShelfStore.swift): dosyanın kendisini gösteriyor; taşınsa ya da adı değişse de
  aynı sürücüde NTFS dosya kimliğiyle (`OpenFileById`) bulunuyor, Mac'teki yer imleri gibi. Ayarla kopya alınıyor.
  `shelf.json`. Kutucuklar Explorer'ın küçük resmiyle; dışarı sürüklenebiliyor.
- **Paylaş:** Windows'un paylaşım penceresi (`IDataTransferManagerInterop.ShowShareUIForWindow`), Yakındaki
  Paylaşım dahil. Zip `System.IO.Compression` ile, dönüştürme `BitmapDecoder`/`BitmapEncoder` ile, dosyanın yanına
  (yazılamıyorsa ISLe8'in klasörüne), "ad 2.zip" gibi boş bir adla.

### Pano sekmesi

- **Kayıt** (`ClipboardService`, Mac: ClipboardService.swift): Mac panoyu yarım saniyede bir sorguluyor;
  Windows değişince haber veriyor (`AddClipboardFormatListener`, `WM_CLIPBOARDUPDATE`), hiçbir şey sorgulamıyor.
  `ExcludeClipboardContentFromMonitorProcessing` ya da `Clipboard Viewer Ignore` taşıyan, `CanIncludeInClipboardHistory`
  ya da `CanUploadToCloudClipboard` değeri 0 olan içerik ve kaydedilmeyen uygulamalardan (öndeki pencerenin
  .exe adı; varsayılan parola yöneticileri) kopyalanan okunmuyor. ISLe8'in kendi geri kopyalaması 1 sn
  boyunca yeniden kaydedilmiyor.
- **Geçmiş** (`ClipboardHistory`): `clipboard.json` ve görseller için `Clipboard` klasörü. Mac'teki SQLite + FTS5
  yerine bellekte arama (en fazla 1000 öğe); arama Mac'teki gibi kelime başına göre, büyük/küçük harf ve aksan
  duyarsız, ı/I → i katlanıyor. Sabitlenenler üstte, sonra en yenisi.
- **Kullanım:** tıklama geri kopyalar; Alt ile tıklama ada kapanınca önceki pencereye Ctrl+V basar (ada odak
  almadığı için klavye zaten oradadır). Ctrl+Alt+V (`RegisterHotKey`) adayı Pano'da açar, arama klavyeyi alır.

### Notlar ve Sistem sekmeleri

- **Sekmeler:** Mac'teki yedisi, aynı sırayla: Home, Raf, Pano, Notlar, AI, Cihazlar, Sistem.
- **Notlar** (`NotesStore`, Mac: NotesModule.swift): üstte yazma satırı, altında kartlar (en az 200 genişlikte,
  2–4 sütun). `%LOCALAPPDATA%\LRXe8\ISLe8\notes.json`, yalnızca kullanıcının okuyabildiği klasörde (Mac'te
  0600). Satıra tıklayınca ada klavyeyi alıyor, kapanınca geri veriyor. Arada iş yok.
- **Sistem** (`SystemSampler`, Mac: SystemStats.swift): yalnızca sekme ekrandayken, iki saniyede bir, arka
  planda bir iş parçacığında ölçülüyor; değerler animasyonsuz değişiyor. İşlemci `GetSystemTimes`, bellek
  `GlobalMemoryStatusEx` (kullanılan = toplam − boş), disk sistem sürücüsü, ağ Ethernet ve Wi-Fi bağdaştırıcıları
  (sanal olanlar hariç), süreçler kullanıcının oturumundakiler; bellekleri çalışma kümesi. ↗ Görev Yöneticisi.

### Zamanlayıcı, hava ve takvim (W1)

- **Zamanlayıcı** (`IslandTimer`, Mac: TimerModule.swift): geri sayım, Pomodoro (25/5, Ayarlar › Widget'lar)
  ve kronometre. Süreler saatten hesaplanıyor, kaymıyor; bekleyen tek şey fazın sonuna kurulan zamanlayıcı.
  Ekrandaki süre yalnızca görünürken ve çalışırken saniyede bir güncelleniyor. Kulak önceliği medya 50,
  zamanlayıcı 40: müzik durunca medya kulağı 8 sn daha tutuyor. Faz bitince ses (Windows'un
  "Hatırlatıcı" sesi) ve 4 sn'lik banner.
- **Hava** (`WeatherService`, Mac: WeatherModule.swift): Open-Meteo, anahtarsız. Şehir seçilmemişse saat
  diliminden ("Turkey Standard Time" → "Europe/Istanbul" → İstanbul), konum izni yok. Arka planda iş yok:
  Home görünür olduğunda rapor 30 dk'dan eskiyse ya da şehir/birim değiştiyse yenileniyor. Mac'in renkli SF
  simgeleri yerine Windows'un renkli emojileri (☀️ ⛅ 🌧️).
- **Takvim** (`CalendarService`, Mac: CalendarModule.swift): bağlantı yokken sade takvim (ay, hafta, bugün
  bilgisayarın tarihinden); etkinliklerin kaynağı ICS abonelik bağlantıları (PORTING.md §3.6); tekrarlayan etkinlikler (RRULE, EXDATE, taşınan tekrar), Windows ve IANA saat dilimleri. Açılışta,
  Home görünürken 15 dk'dan eskiyse ve bağlantı varken 30 dk'da bir okunuyor; arada yalnızca bir sonraki
  önemli an bekleniyor (5 dk uyarısı, etkinlik sonu, gece yarısı). Hızlı ekleme bir `.ics` dosyası yazıp
  varsayılan takvim uygulamasında açıyor. Yazma satırı için ada klavyeyi alıyor, kapanınca geri veriyor.

### Tray, Ayarlar ve ikinci açılış (W1)

- **Tray:** `Shell_NotifyIcon` ile bildirim alanında bir kapsül (Mac'teki `capsule.fill`); koyu görev
  çubuğunda beyaz, açıkta siyah, ölçeğe göre boyutlu (`scripts/make-icons.ps1`). Tıklama Ayarlar'ı
  açar, bağlam menüsü Mac'teki gibi yalnızca "Ayarlar…" ve "ISLe8'den çık". Kendi penceresi yok; adanın
  penceresinin mesajlarını dinliyor. Explorer yeniden başlayınca (`TaskbarCreated`) kendini yeniden ekler.
- **Ayarlar penceresi:** Sıradan bir WinUI penceresi, Mica, solda menü, kartlı bölümler. Ada hep koyu;
  Ayarlar Windows'un açık/koyu modunu izliyor (`UISettings`). İlk açılınca kuruluyor, kapanınca
  bırakılıyor. Bölümler W1 adımlarıyla geliyor; şu an Genel, Görünüm, Düzen ve Medya.
- **İkinci açılış:** `AppInstance.RedirectActivationToAsync` ile çalışan örneğe gidiyor, o da Ayarlar'ı
  açıyor (ön plana çıkabilmesi için yeni süreç `AllowSetForegroundWindow` veriyor).
- **Dil ve içe aktarma:** Mac'teki gibi ISLe8'i yeniden başlatıyor (`AppInstance.Restart`); bütün
  pencereler yeni dille kuruluyor. W0'da sağ tık menüsündeki dil canlı değişiyordu; tek yola indi.
- **Girişte başlat:** Paketsizken `HKCU\…\Run`; anahtar tek kayıt olduğu için düğme Windows'un gerçekte
  ne yapacağını gösteriyor. Klasör taşınırsa açılışta onarılıyor. Paketliyken `StartupTask` (9. adım).
- **Etkin ekran:** Ada, öne getirdiğin pencerenin ekranına geçiyor. `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)`
  yalnızca bu modda kurulu; masaüstüne tıklanınca imlecin ekranı sayılıyor.

### DPI (Per-Monitor v2)

- `app.manifest`: `dpiAwareness = PerMonitorV2`.
- Bütün ölçüler DIP cinsinden (`IslandMetrics`); XAML bunları monitörün ölçeğiyle çarpıyor.
- Yerleşim tek bir saf fonksiyonda: `IslandLayout.Compute(monitör, çalışma alanı, ölçek, açık boyut)`.
  Fiziksel piksel, ortalama, küçük ekranda küçültme ve üstteki görev çubuğu burada; Mac'te birim
  testleriyle denetleniyor.
- `WM_DPICHANGED`, `WM_DISPLAYCHANGE` ve çalışma alanı değişince (`SPI_SETWORKAREA`) pencere
  yeniden yerleşiyor. Başka monitöre geçiş de aynı yoldan gidiyor.
- Güvenlik ağı: XAML'ın ölçeği (`XamlRoot.RasterizationScale`) monitörünkinden farklı kalırsa pencere
  yeniden yerleşiyor ve günlüğe bir satır düşüyor.

| Ekran | Ölçek | Pill (px) | W0 açık ada (px) |
|---|---|---|---|
| 1920×1080 | %100 | 200×32 | 560×160 |
| 1920×1080 | %125 | 250×40 | 700×200 |
| 2560×1440 | %100 | 200×32 | 560×160 |
| 2560×1440 | %125 | 250×40 | 700×200 |
| 2560×1440 | %150 | 300×48 | 840×240 |
| 3840×2160 | %150 | 300×48 | 840×240 |
| 3840×2160 | %175 | 350×56 | 980×280 |
| 3840×2160 | %200 | 400×64 | 1120×320 |

### Hareket

- Büyüyen geçiş (açılma, peek, pill'in genişlemesi) `response 0,34 / damping 0,78`, küçülen
  `0,28 / 0,95` (Mac Faz 7 değerleri). Animasyon hızı çarpanı ayarda 0,6–1,6, modelde 0,5–2'ye
  sıkıştırılır.
- Composition'ın `Period` değeri `response / 2π` olarak veriliyor (bkz. Riskler).
- İçerik Mac'teki gibi: açılışta kabuktan 60 ms sonra 220 ms'de, üst kenara bağlı olarak %94'ten
  büyüyerek ve belirerek (ease-out); kapanışta 160 ms'de %90'a küçülüp silinerek (ease-in). Böylece
  kabuk pill'e katlanırken boş bir kutu kalmıyor.
- Windows'ta "Animasyon efektleri" kapalıysa yay yerine 160 ms'lik ease-in-out; içerik yalnızca solar.

---

## 4. Özellik eşlemesi (macOS → Windows)

| Özellik | macOS | Windows | Faz |
|---|---|---|---|
| Medya | MediaRemote köprüsü | `GlobalSystemMediaTransportControlsSessionManager` (SMTC): kapak, ilerleme, oynat/duraklat/sonraki/önceki, karışık çal, tekrarla, sarma | **W0** ✅ |
| Ses kaydırıcısı | Core Audio | `IMMDeviceEnumerator` + `IAudioEndpointVolume` + `IAudioEndpointVolumeCallback` (kaynak üretimli COM, AOT uyumlu) | W1 |
| Bu bilgisayarın pili | IOKit | `Microsoft.Windows.System.Power.PowerManager` olayları | W1 |
| Hava | Open-Meteo, saat diliminden şehir | Aynısı; `TimeZoneInfo.TryConvertWindowsIdToIanaId` ile "Turkey Standard Time" → `Europe/Istanbul` | W1 |
| Zamanlayıcı / Pomodoro | | `DispatcherQueueTimer` (yalnızca görünürken), bitince `AppNotificationManager` | W1 |
| Home (3 sütun) + dock | SwiftUI | Takvim ayı + günün etkinlikleri · büyük oynatıcı · hava (hissedilen, nem, rüzgar, UV, 5 saat, 5 gün); Geniş / Kompakt / Minimal düzen ("Pano" değil: Pano bir sekme) | W1 |
| Pano geçmişi | changeCount, SQLite + FTS5 | `AddClipboardFormatListener` (`WM_CLIPBOARDUPDATE`), Microsoft.Data.Sqlite + FTS5. `ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory = 0` ya da `CanUploadToCloudClipboard = 0` taşıyan içerik (parola yöneticileri) kaydedilmez | W2 |
| Raf | sürükle-bırak, AirDrop | Pill'e sürükle-bırak (OLE drop), Share UI (`IDataTransferManagerInterop.ShowShareUIForWindow`; Nearby Share dahil) | W2 |
| Bildirim aynası | usernoted veritabanı | `UserNotificationListener` (MSIX + `userNotificationListener` yeteneği + kullanıcı izni), `NotificationChanged` olayı | **W3** ✅ |
| Takvim | EventKit | `AppointmentManager.RequestStoreAsync(AllCalendarsReadOnly)` (`appointments` yeteneği) | W3 |
| AI sekmesi | Unix soket + hook'lar | `\\.\pipe\isle8-agents`; hook'lar `isle8` komutunu çağırır (MSIX App Execution Alias). componest tarzı kart: mevcut adım, süre, "Adım 3/6", son adımlar, Onayla/Reddet | W4 |
| Kullanım limitleri | LimitBar veri katmanı | `codex`, `claude -p /usage`, `agy` CLI'ları; tasarruf modu 15 dk. Token/şifre okunmaz | W4 |
| Kablosuz cihaz pilleri | Bluetooth raporu | BLE: GATT Battery Service (0x180F/0x2A19). Klasik kulaklıklar: PnP özelliği `{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2`, `DeviceWatcher` ile (olay güdümlü) | W5 |
| Tema sistemi | 7 tema, JSON | Aynı 7 tema ve JSON biçimi (Mac'te dışa aktarılan tema Windows'ta açılır). Glass → Mica/Acrylic | W0 (temalar) / W5 (cam, düzenleyici) |
| Dil | String Catalog | `Strings/tr-TR`, `Strings/en-US` `.resw`; menüden uygulamaya özel dil | **W0** ✅ |

---

## 5. Yol haritası

| Faz | İçerik |
|---|---|
| **W0 · İskelet** | Pill, hover'da yaylı açılma, DPI ve monitör seçimi, tam ekranda gizlenme, SMTC medya (kompakt pill + oynatıcı), 7 tema, TR/EN, Mac'te derleme denetimi + Core testleri, CI dosyası |
| W1 · Home | Alt ikon dock'u (Home, Raf, Pano, AI, Cihazlar), 3 sütunlu Home, düzen seçimi, ses kaydırıcısı, pil, hava, zamanlayıcı/Pomodoro, bildirim alanı simgesi |
| W2 · Pano + Raf | Pano geçmişi ve arama, raf, Share UI |
| W3 · Bildirim + Takvim | Bildirim aynası (MSIX), AppointmentStore |
| W4 · AI | Named pipe, hook'lar, çalışma kartı, Onayla/Reddet, kullanım limitleri |
| W5 · Cihaz + Cam | Bluetooth pilleri, Mica/Acrylic ada, tema düzenleyici, JSON dışa/içe aktarma |
| W6 · Dağıtım | MSIX imzalama, oturum açılışında başlatma, ölçüm turu |

Her faz `scripts/measure.ps1` ile biter; bütçe aşılırsa faz bitmiş sayılmaz.

---

## 6. Riskler ve Windows'ta doğrulanacaklar

İlk Windows çalıştırmasında (2026-10-26, TESTING.md) 1–3 doğrulandı; geri kalanı bekliyor.

1. **Saydam pencere:** Saydam sistem arka planı + DWM blur-behind tekniği **çalışıyor**. İki tuzak
   çıktı ve düzeltildi: `LayerVisual` gölgesi opak siyah bir kutu çiziyordu (maskeli `DropShadow`'a
   geçildi), `AppWindow.Show()` `WS_DLGFRAME`'i geri koyup 2 DIP beyaz çerçeve çiziyordu (çerçeve
   gösterimden sonra yeniden sökülüyor, `WM_NCCALCSIZE` istemci alanını pencerenin tamamı yapıyor).
2. **`AppWindow.Show(false)`:** İçerik `Window.Activate()` olmadan da çiziliyor ve girdi alıyor; odak
   çalınmıyor.
3. **`SetWindowRgn` + WinUI:** Bölge hem fareyi hem çizimi kırpıyor; pill'in yanındaki nokta alttaki
   pencereye gidiyor. Not: fare otomasyonuyla test ederken `SetCursorPos` WinUI'ye pointer olayı
   vermiyor; gerçek bir hareket (`SendInput`) gerekiyor.
4. **Yay hızı:** Composition `Period` = `response / 2π` varsayımı. Açılış Mac'tekinden belirgin
   yavaş ya da hızlıysa `SpringSpec.CompositionPeriod` tek satırda değişir.
5. **SMTC farkları:** Karışık çal/tekrarla her uygulamada desteklenmiyor (düğme devre dışı görünür).
   Bazı uygulamalar zaman çizelgesini seyrek güncelliyor; konum son rapordan hesaplanıyor.
6. **`ABN_FULLSCREENAPP`:** Masaüstü ya da görev çubuğu yanlışlıkla "tam ekran" sayılabilir; bunlar
   sınıf adından eleniyor.
7. **Cam tema (W5):** `SystemBackdrop` bütün pencereye uygulanır, ada ise pencereden küçük. Şekilli
   akrilik için araştırma gerekiyor; o zamana kadar Glass yarı saydam düz renk.
8. **Bildirimler (W3):** `NotificationChanged` olayı paketli masaüstü uygulamalarında tetiklenmeyebilir;
   o durumda `UserNotificationChangedTrigger` arka plan görevi gerekir. **Doğrulandı (2026-10-29):** olay
   Windows 11'de paketli ISLe8'e geliyor.
9. **Takvim (W3):** AppointmentStore yalnızca Windows'a eklenmiş hesapları görür; yeni Outlook bu
   depoya yazmıyor. Gerekirse Microsoft Graph ya da ICS aboneliği.
10. **Menü ve odak:** Odak almayan bir pencerenin açılır menüsü dışarı tıklayınca kapanmayabilir; bu
    yüzden menü açılırken ada öne alınıyor. Menü yine açık kalırsa ada kapanamaz (menü açıkken
    kapanma engelli).
11. **Kapanan oynatıcı:** Bir oynatıcı kapanınca SMTC oturum nesnesi ölü kalıp hata atabiliyor. Her
    SMTC çağrısı korumalı; hata olursa Windows'un o anki oturumuna geçiliyor.

---

## 7. Kararlar

| Konu | Karar |
|---|---|
| Teknoloji | C# + WinUI 3, .NET 10, Native AOT, MSIX |
| Paket kimliği | `com.lrxe8.isle8`, yayıncı `CN=LRXe8`; tam MSIX, sanallaştırma kapalı (2026-10-29) |
| Platformlar | x64 ve ARM64 (32 bit yok) |
| En düşük Windows | Windows 10 2004 (19041) |
| Varsayılan tema | Ember |
| Açılma gecikmesi | 0, en az 50 ms süzme; Mac ile aynı (W0'da 150 ms'ydi; PORTING.md §4) |
| Test | Kullanıcının Windows bilgisayarı; Mac'te derleme denetimi |
| Klasör | `~/Documents/LRXe8/ISLe8-Windows` |

---

## 8. Durum

| Faz | Durum | Not |
|---|---|---|
| W0 · İskelet | Windows 11'de çalışıyor (2026-10-26) | 71 Core testi geçiyor. İlk çalıştırmada iki çizim hatası bulunup düzeltildi (Riskler 1). Pencere, hover, menü, medya (Spotify dahil), tema ve 4K %150 doğrulandı. Native AOT, müzik çalarken: CPU %0,43, özel bellek 37 MB (bütçe içinde); boşta ölçüm ve TESTING.md'de kalanlar bekliyor. Sürümler `release.yml` ile GitHub Releases'e zip olarak çıkıyor (0.1.1'den beri AI/ML kütüphanesiz). |
