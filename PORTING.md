# macOS → Windows taşıma rehberi

Bu dosya, macOS sürümündeki sistemlerin Windows'a nasıl taşınacağını modül modül anlatır. Her aşamada (W1–W6) bu dosya ile [DESIGN.md](DESIGN.md) §4–5 birlikte izlenir.

**Referans:** [Mericsdc/ISLe8-macOS](https://github.com/Mericsdc/ISLe8-macOS) (public, Apache-2.0). Davranışın tek doğru kaynağı oradaki koddur; bu rehber `b3dc5de` anlık görüntüsünden yazıldı. Mac'te bir davranış değişirse burası da güncellenir. Aşağıdaki dosya bağlantıları `main` dalını gösterir.

**Nasıl çalışılır:**
1. Her aşama ayrı bir oturum. Oturum bu dosyanın ilgili bölümlerini ve Mac'teki kaynak dosyaları okuyarak başlar.
2. İş **Windows bilgisayarında** yapılır: derle, çalıştır, dene. Mac'te yalnızca `ISLe8.Core` derlenip test edilebilir (`scripts/check-on-mac.sh`).
3. Önce platformdan bağımsız mantık `ISLe8.Core`'a yazılır ve xUnit testleriyle sınanır; sonra `ISLe8.App`'teki Windows katmanı.
4. Mac'teki Türkçe ve İngilizce metinler aynen kullanılır ([Localizable.xcstrings][mac-strings]). Yeni metin gerekirse iki dilde de yazılır.

**Her aşama nasıl biter:**
- Derleme 0 uyarı ve 0 hatayla geçer; bütün Core testleri geçer.
- Aşamanın kabul kriterleri Windows 11'de canlı denenir ve [TESTING.md](TESTING.md)'ye işlenir.
- `scripts/measure.ps1` ile boştaki CPU < %0,1 (ada kapalı, müzik yok), etkileşimde anlık < %3, bellek < 80 MB.
- Sürüm etiketi atılır.

---

## 1. Ortak sözleşmeler

İki uygulama ayrı koddur; şunlar iki tarafta da aynı kalır.

### Tema dosyası (`.isle8theme.json`)

```json
{
  "id": "graphite",
  "name": "Graphite",
  "material": "solid",
  "background": "#0E0E10FF",
  "backgroundBottom": "#0E0E10FF",
  "surface": "#18181BFF",
  "border": "#FFFFFF17",
  "accent": "#7AA7FF",
  "cornerRadius": 14,
  "typeface": "system"
}
```

- `material` değerleri: `black | solid | gradient | glass | artwork`. `typeface` değerleri: `system | rounded | monospaced | serif`.
- Renkler `#RRGGBBAA` biçiminde; okurken `#RGB` ve `#RRGGBB` de kabul edilir.
- `cornerRadius` 0–26 aralığına sıkıştırılır.
- Eksik alanlar Notch Black'in değerlerini alır; `backgroundBottom` yoksa `background` kullanılır.
- **Yazarken her alan yazılır**, `backgroundBottom` dahil; Windows'ta şu an null olduğunda atlanıyor. Mac bunu zaten opsiyonel okuyor, ama iki taraf aynı dosyayı üretsin.
- Mac: [Theme.swift][mac-theme]. Hazır temaların değerleri oradaki `presets` listesindedir. Kullanıcının hazır bir temayı değiştirmesi bir kopya oluşturur; kopyanın adı "`<ad>` · özel" olur.

### `isle8` komutu ve yerel kanal

Mac'te kanal bir Unix soketidir (`~/Library/Application Support/ISLe8/isle8.sock`, 0600). Windows'ta kullanıcıya özel bir named pipe olur; pipe'ın ACL'i yalnızca mevcut kullanıcının SID'ine izin verir. İletiler aynıdır: her istek **bir JSON satırı**, her yanıt bir JSON satırıdır.

| İstek | Yanıt |
|---|---|
| `{"type":"hook","agent":"claude\|codex","event":{…hook'un stdin JSON'u…},"app_pid":1234}` | `{"ok":true}`; izin isteğinde `{"decision":"allow\|deny","message":"…"}` |
| `{"type":"activity","action":"start\|update\|end","id":"…","title":"…","subtitle":"…","progress":0.4,"icon":"…"}` | `{"ok":true}` |
| `{"type":"island","action":"open\|close","tab":"home\|shelf\|clipboard\|agents\|devices"}` | `{"ok":true}` |
| `{"type":"devices"}` | `{"ok":true,"devices":["AirPods Pro 3 · Sol %90 · Sağ %91 · Kutu %100"]}` |
| `{"type":"ping"}` | `{"ok":true,"app":"ISLe8"}` |

Komutlar: `isle8 hook claude|codex`, `isle8 activity start|update|end --id … [--title …] [--subtitle …] [--progress 0.4] [--icon …]`, `isle8 open [sekme]`, `isle8 close`, `isle8 devices`, `isle8 ping`, `isle8 help`.

**Hook kuralları:**
- Hook **her zaman 0 ile çıkar**.
- ISLe8 çalışmıyorsa hiçbir şey yazmadan hemen çıkar; ajan kendi akışına devam eder.
- İzin isteği (`PermissionRequest`) 590 sn'ye kadar bekler; diğer olaylar 3 sn. Onaylanırsa ya da reddedilirse stdout'a şu yazılır:
  `{"hookSpecificOutput":{"hookEventName":"PermissionRequest","decision":{"behavior":"allow|deny","message":"…"}}}`
- Süre dolarsa, kullanıcı "Terminalde sor" derse ya da ISLe8 kapalıysa çıktı boş kalır.
- `app_pid` yalnızca `SessionStart` ve `UserPromptSubmit` olaylarında gönderilir: ebeveyn süreç zincirinde bulunan ilk pencereli uygulama (terminal, Claude).
- Mac: [CommandLineTool.swift][mac-cli], [LocalServer.swift][mac-server].

**Windows notu:** WinUI uygulaması GUI alt sistemindedir, stdin/stdout bağlı gelmez. `isle8` için pakete ayrı, küçük bir **konsol** projesi eklenmeli (Native AOT, `isle8.exe`) ve MSIX'te App Execution Alias olarak tanımlanmalı. `AttachConsole` yöntemi stdin borusu için güvenilir değil.

---

## 2. Ada davranışı (her modülün üstünde)

Mac'te deneme yanılmayla bulundu; Windows'ta baştan böyle yapılmalı.

| Kural | Değer | Mac kaynağı |
|---|---|---|
| Üzerine gelince açılma | Ara aşama (peek) yok; doğrudan açılır. Açılma gecikmesi varsayılan 0, en az 50 ms süzme (üstünden geçen imleç açmasın) | [IslandModel.swift][mac-model] `setHovering` |
| Kapanma | İmleç çıktıktan 0,35 sn sonra | [SettingsStore.swift][mac-settings] |
| Tıklama modu | Ayarda "Tıklayınca" seçiliyse üzerine gelince hafifçe büyür (peek), tıklayınca açılır | aynı |
| Yay | Açılış response 0,34, damping 0,78. Kapanış 0,28 / 0,95. Animasyon hızı çarpanı 0,6–1,6 | [IslandModel.swift][mac-model] `animation` |
| İçerik geçişi | Açılış: opaklık + ölçek 0,94, üst kenara bağlı, 0,22 sn, 0,06 sn gecikmeli. Kapanış: opaklık + ölçek 0,9, 0,16 sn. Kabuk ve içerik birlikte notch'a / pill'e katlanır; boş bir kutu kalmaz | [IslandView.swift][mac-islandview] |
| Hareketi azalt | Yay yerine 0,16 sn ease-in-out (Windows: `UISettings.AnimationsEnabled`) | [IslandModel.swift][mac-model] |
| Sabit pencere | Pencere en büyük ada kadar sabit; ada içinde büyür. Animasyon sırasında pencere yeniden boyutlanmaz. Windows'ta zaten `SetWindowRgn` var | [IslandController.swift][mac-controller] |
| Hover alanı | Yalnızca adanın kendisi. Her giriş/çıkış olayı imlecin **gerçek konumuyla** doğrulanır; işletim sistemi sahte olay yollayabiliyor | [IslandPanel.swift][mac-panel] |
| Sekmeler aynı yükseklikte | Sekme değişimi adayı yeniden boyutlamaz, yalnızca içerik 0,18 sn'de çapraz geçer. İmleç dock'tayken ada kapanmaz | [IslandModel.swift][mac-model] `contentHeight` |
| Küçülürken açık kalma (slack) | Açık ada kısalırsa (Minimal Home'a dönüş) imleç eski çerçevenin içindeyken açık kalır. İmleç yeni çerçeveye girince ya da ikisinden de çıkınca normale döner | [IslandPanel.swift][mac-panel] `setIslandSize` |
| Dışarı tıklama | Ada açıkken ve imleç üstünde değilken başka yere tıklamak adayı kapatır (sabitlenmişse kapatmaz) | [IslandModel.swift][mac-model] `clickedOutside` |
| Sekme hafızası | 15 sn içinde yeniden açılırsa son sekme, yoksa ilk sekme. Onay bekleyen ajan varsa AI sekmesi | aynı, `chooseTabForOpening` |
| İki parmakla kaydırma | Yatay kaydırma (60 pt) sonraki/önceki sekme. Raf, Pano ve Palet'te yalnızca başlık ve dock satırında | [IslandController.swift][mac-controller] `trackSwipe` |
| Başlık çipleri | Pill'in iki yanındaki alana sığar, asla taşmaz. Sığmazsa sırayla düşer: cihaz çipi → W → uyanık tut. Pil ve kamera/mikrofon kalır | [HeaderBar.swift][mac-header] |
| Kulak önceliği | Onay bekleyen ajan 100 (her zaman önce), ajan 55, medya 50, zamanlayıcı 40, kamera/mikrofon 30; ayarlardan sıralanabilir | [IslandModule.swift][mac-module] |
| Uyarı süreleri | Şarkı değişimi 2,5 sn, AirDrop 3, pil 3, toplantı 10, zamanlayıcı 4, onay 15, ajan bitti 4, limit 6, cihaz bağlandı 4, cihaz pili 5, bildirim 4 (ayar). Banner üzerine gelinince bekler | [AppDelegate.swift][mac-appdelegate] |
| Bildirim kuyruğu | En fazla 5 bekleyen, banner'da "+N". Ada açıkken gelenler kaybolmaz, kapanınca sırayla gösterilir | [IslandModel.swift][mac-model] `showNotification` |

### Boyutlar (Windows'ta etkin piksel / DIP)

| Düzen | Genişlik | İçerik yüksekliği | Not |
|---|---|---|---|
| Geniş (Home panosu) | 820 (ayar 680–980) | 304 | Bütün sekmeler 304 |
| Kompakt | 640 | 196 | Bütün sekmeler 196 |
| Minimal | 600 | Home 64, diğer sekmeler 196 | Küçülürken açık kalma kuralı burada gerekli |

- **Ada yüksekliği:** başlık satırı + 6 + içerik + dock alanı (8 + 36 + 10). ⌘K paletinde dock yok, alt boşluk 14.
- **Kulaklar:** genişliği 40; ses/parlaklık göstergesinde 70.
- **Alt köşe yarıçapı:** açık adada `min(tema köşesi + 12, 28)`.
- **Home sütun ağırlıkları:**
  - Geniş: takvim 1 · medya 1,1 · hava 1 · zamanlayıcı 0,62
  - Kompakt: 1 · 1,15 · 1 · 0,7
  - Minimal: 1,1 · 1,7 · 0,7 · 0,9
- Mac: [HomeView.swift][mac-home].

---

## 3. Modüller

Her modülde: Mac dosyaları → Windows karşılığı → önemli davranışlar → kabul kriterleri. Aşama önerisi §5'te.

### 3.1 Medya (W0 bitti; kalanlar W1)

- **Mac:** [NowPlayingService.swift][mac-nowplaying], [MediaViews.swift][mac-mediaviews], [NowPlaying.swift][mac-nowplaying-model]
- **Windows:** SMTC (var).
- **Kalanlar:**
  - Kapak, yalnızca parça değişince değil **küçük resim referansı değişince** de yenilenmeli; Chrome önce eski kapağı yolluyor.
  - Duraklat→oynat geçişinde zaman çizelgesi yeniden okunmalı; okunmazsa ilerleme sıçrar.
  - İlk SMTC isteği başarısız olursa yeniden denenmeli.
  - `GetCurrentSession` duraklatılmış bir oturum veriyorsa ve başka bir oturum çalıyorsa, çalanı seçmeli (`GetSessions`).
  - Kapalıyken her SMTC olayında oynatıcı yeniden çizilmemeli.
- **Ses kaydırıcısı (W1):**
  - `IMMDeviceEnumerator` + `IAudioEndpointVolume` + `IAudioEndpointVolumeCallback`. Varsayılan çıkış değişince `IMMNotificationClient.OnDefaultDeviceChanged`.
  - Değer 0–100, sessizdeyken 0. Sıfırdan yukarı çekmek sesi açar.
  - Mac: [HUDModule.swift][mac-hud] `setOutputVolume`.
- **Kabul:**
  - Spotify, Chrome/YouTube ve Medya Oynatıcı'da kapak, ilerleme, oynat/duraklat, ileri/geri, karışık çal ve tekrarla çalışıyor.
  - Kaydırıcı sistem sesiyle eşleşiyor, iki yönde de güncelleniyor.
  - Kapalı ada müzik çalarken de boşta sayılır: ses çubukları dışında hiçbir şey çalışmaz.

### 3.2 Ses ve parlaklık göstergesi (plana eklenmeli: W3)

- **Mac:** [HUDModule.swift][mac-hud], [VolumeMonitor.swift][mac-volume], [BrightnessMonitor.swift][mac-brightness], [SystemViews.swift][mac-systemviews] `HUDAlertView`
- **Windows:**
  - **Ses:** `IAudioEndpointVolumeCallback.OnNotify`; olay güdümlü, sorgu yok.
  - **Dahili ekran parlaklığı:** WMI `WmiMonitorBrightnessEvent` (`root\WMI`), COM `IWbemServices::ExecNotificationQueryAsync` ile. `System.Management` Native AOT ile uyumsuz, kullanılmaz.
  - **Harici monitör:** DDC/CI (`dxva2`), yalnızca kısayol tuşuyla; sorgu yok.
- **Davranış:**
  - Gösterge kulaklarda: solda simge, sağda seviye çubuğu; genişlik 70.
  - Parlaklıkta gösterge yalnızca 1 sn içinde en az %4'lük değişiklikte açılır; otomatik parlaklığın yavaş ayarları gösterilmez.
  - Ada açıkken gösterge çıkmaz.
- **Not:** Windows'un kendi ses göstergesini gizlemenin desteklenen bir yolu yok; ISLe8'inki yanında çıkar. Pencere bulup gizleme gibi kırılgan yöntemler kullanılmaz.
- **Kabul:** Ses tuşları ve kaydırıcıyla gösterge çıkıyor; otomatik parlaklık gösterge açmıyor; ayardan ikisi de kapatılabiliyor.

### 3.3 Pil, şarj gücü (W1)

- **Mac:** [SystemMonitors.swift][mac-sysmon], [SystemModule.swift][mac-system], [SystemViews.swift][mac-systemviews]
- **Windows:**
  - `Microsoft.Windows.System.Power.PowerManager` olayları (yüzde, şarj durumu, güç kaynağı).
  - Watt: `Windows.Devices.Power.Battery.AggregateBattery.GetReport().ChargeRateInMilliwatts`, **yalnızca ada açıkken** okunur.
- **Davranış:**
  - Şarja takınca, tam dolunca, %20'de ve %10'da (kritik) banner.
  - Başlık çipi: %10 altı kırmızı, %20 altı turuncu, şarjda yeşil.
  - Şarjda başlıkta "⚡ 61 W".
- **Kabul:** Takma/çıkarma ve eşiklerde banner çıkıyor; W yalnızca açıkken okunuyor (ölçümle).

### 3.4 Kamera/mikrofon göstergesi ve uyanık tut (plana eklenmeli: W3)

- **Mac:** [SystemMonitors.swift][mac-sysmon], [SystemModule.swift][mac-system]
- **Windows:**
  - **Kamera/mikrofon:** `HKCU\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam` ve `\microphone` altındaki uygulama alt anahtarlarında (`NonPackaged` dahil) `LastUsedTimeStop == 0` ise cihaz kullanımda. Değişiklik `RegNotifyChangeKeyValue` ile alt ağaç dahil izlenir; olay güdümlü, sorgu yok.
  - **Uyanık tut:** `PowerCreateRequest` + `PowerSetRequest(PowerRequestDisplayRequired)`, ya da `SetThreadExecutionState(ES_CONTINUOUS | ES_DISPLAY_REQUIRED)`.
- **Davranış:**
  - Kulakta yeşil kamera / turuncu mikrofon (başka bir aktivite yoksa); başlıkta çip.
  - Başlıkta fincan düğmesi uyanık tutmayı açıp kapatır.
- **Kabul:** Teams/Zoom/Kamera uygulamasıyla gösterge açılıp kapanıyor; uyanık tutarken ekran kararmıyor.

### 3.5 Hava (W1)

- **Mac:** [WeatherModule.swift][mac-weather], [WeatherViews.swift][mac-weatherviews]
- **Windows:** Aynı Open-Meteo isteği, `HttpClient` + System.Text.Json kaynak üretimi.
- **İstek:**
  - `current=temperature_2m,apparent_temperature,relative_humidity_2m,wind_speed_10m,wind_direction_10m,uv_index,weather_code,is_day`
  - `hourly=temperature_2m,weather_code,is_day&forecast_hours=6`
  - `daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max&forecast_days=6`
  - `timezone=auto`, Fahrenheit'ta `wind_speed_unit=mph`.
  - Koordinatlar 2 ondalığa yuvarlanır.
- **Davranış:**
  - Konum izni yok. Şehir ayarlanmamışsa saat diliminden bulunur: `TimeZoneInfo.TryConvertWindowsIdToIanaId` ile "Turkey Standard Time" → `Europe/Istanbul` → "İstanbul" (Open-Meteo geocoding).
  - Arka planda zamanlayıcı yok: Home görünür olduğunda veri 30 dk'dan eskiyse yenilenir.
  - Saatlik listenin ilk öğesi geçerli saattir; ada "Şimdi" + sonraki 5 saati gösterir. Günlük listenin ilki bugündür; "Yarın" + sonraki 4 gün gösterilir.
  - Rüzgar yönü 8 nokta: K, KD, D, GD, G, GB, B, KB (İngilizcede N, NE, E, SE, S, SW, W, NW).
  - "Kapalı" (overcast) yerine "Çok bulutlu" kullanılır; "Kapalı" ayarlarda "off" anlamında geçiyor.
- **Kabul:** Geniş, Kompakt ve Minimal'de doğru alanlar görünüyor; ağ yokken "Tekrar dene" çıkıyor.

### 3.6 Takvim (W1 kimlikle; okuma ve hızlı ekleme)

- **Mac:** [CalendarModule.swift][mac-calendar], [CalendarViews.swift][mac-calendarviews]
- **Windows:**
  - `AppointmentManager.RequestStoreAsync(AllCalendarsReadOnly)` (`appointments` yeteneği, paket kimliği gerekir). Değişiklikler `AppointmentStore.StoreChanged` ile izlenir.
  - Windows Takvim/Posta'ya eklenmemiş hesaplar (yeni Outlook, Google) için **ICS abonelik bağlantısı** ayarı (salt okunur) önerilir.
  - **Hızlı ekleme:** `AppointmentManager.ShowAddAppointmentAsync`. Win32'de pencere tutamacıyla (`IInitializeWithWindow`) çağrılır. Uygulama başka takvimlere doğrudan yazamaz; sistem penceresi açılır.
- **Davranış:**
  - **Geniş:** Ay görünümü, ilk gün kullanıcının yerel ayarından. Bugün vurgu renginde dolu daire, seçili gün halka. Etkinlikli günlerde takvim renginde nokta; çok günlük etkinlik her günü işaretler. Ay başlığına tıklamak bugüne döner.
  - Gün listesi: saat (ya da "Tüm gün"), başlık, ayrıntı satırı (toplantı hizmeti adı, yoksa konum, yoksa takvim adı). Toplantı linki varsa katıl düğmesi.
  - **Kompakt:** yalnızca seçili günün listesi + "+". **Minimal:** sıradaki tek etkinlik, yoksa "Etkinlik yok".
  - Toplantı linki tanıma: zoom.us, meet.google.com, teams.microsoft.com, teams.live.com, webex.com, facetime.apple.com (URL, konum ve notlarda).
  - Toplantıdan 5 dk önce banner (10 sn, "Katıl" düğmesiyle).
  - Reddedilen etkinlikler gösterilmez.
  - Hızlı ekleme metni "14:30 Toplantı": saat yoksa bugün için bir sonraki tam saat, başka gün için 09:00; süre 1 saat.
- **Kabul:** Ay gezinme, gün seçme, noktalar, katıl düğmesi, 5 dk uyarısı ve hızlı ekleme penceresi çalışıyor.

### 3.7 Zamanlayıcı / Pomodoro (W1)

- **Mac:** [TimerModule.swift][mac-timer], [TimerViews.swift][mac-timerviews]
- **Windows:** Mantık tamamen Core'da: süreler tarihten hesaplanır, kaymaz. Bitişe tek bir zamanlayıcı kurulur. Ekrandaki saat yalnızca görünürken saniyede bir güncellenir.
- **Davranış:**
  - Hızlı süreler: 5 ve 15 dk; Pomodoro 25/5 (ayarlanabilir); kronometre.
  - Bitince ses (ayar) ve banner.
  - Kulakta kalan süre.
  - Minimal'de: çalışmıyorsa 5/15 dk düğmeleri, çalışırken kalan süre + durdur.
- **Kabul:** Uyku/uyanma sonrası süre doğru; kapalıyken CPU harcamıyor.

### 3.8 Home, dock ve sekmeler (W1)

- **Mac:** [HomeView.swift][mac-home], [HeaderBar.swift][mac-header] (`TabDock`), [IslandView.swift][mac-islandview] (`ExpandedView`), [CustomizationSettings.swift][mac-custom]
- **Davranış:**
  - Sekmeler altta, kapsül içinde ikon dock'u: Home, Raf, Pano, AI, Cihazlar. Seçili sekme vurgu renginde dolgu alır. AI'da onay bekleyen ajan varsa turuncu nokta.
  - Sekmelerin sırası ve görünürlüğü ayarlanabilir; en az biri açık kalır.
  - Home sütunları: takvim, medya, hava, zamanlayıcı (isteğe bağlı). Sıralanabilir ve gizlenebilir.
  - Düzen seçimi: Geniş / Kompakt / Minimal. Adı "Pano" değil, çünkü "Pano" Türkçede Pano sekmesi.
- **Kabul:** Beş sekme arasında dock'tan art arda tıklarken ada açık kalıyor ve boyutu değişmiyor. Minimal'de AI'dan Home'a dönerken ada açık kalıyor.

### 3.9 Pano geçmişi (W2)

- **Mac:** [ClipboardService.swift][mac-clipservice], [ClipboardStore.swift][mac-clipstore], [ClipboardItem.swift][mac-clipitem], [ClipboardViews.swift][mac-clipviews]
- **Windows:**
  - `AddClipboardFormatListener` / `WM_CLIPBOARDUPDATE`. Olay güdümlü; Mac'teki 0,5 sn'lik sayaç kontrolüne gerek yok.
  - Microsoft.Data.Sqlite + FTS5. Native AOT uyumluluğu ilk adımda doğrulanmalı.
- **Davranış:**
  - **Kaydedilmeyenler:**
    - `ExcludeClipboardContentFromMonitorProcessing` biçimini taşıyan içerik;
    - `CanIncludeInClipboardHistory` ya da `CanUploadToCloudClipboard` = 0 olan içerik (parola yöneticileri);
    - hariç tutulan uygulamalar (ayar).
  - Türler: metin, zengin metin, URL, görsel (PNG), dosyalar.
  - Aynı içerik tekrar kopyalanırsa yeni kayıt açılmaz, mevcut kayıt en üste çıkar.
  - Saklama: en fazla 200 öğe, 30 gün; sabitlenenler hiç silinmez.
  - Arama: aksan ve büyük/küçük harf duyarsız, kelime başına göre. **Türkçe ı/I → i katlanır**; SQLite'ın tokenizer'ı bunu yapmıyor, Mac'te ayrı bir katlanmış arama sütunu var.
  - Tıklayınca kopyalar; Alt ile tıklayınca öndeki uygulamaya yapıştırır (`SendInput` Ctrl+V).
  - Kısayol: Ctrl+Alt+V (`RegisterHotKey`). Win+V sistemin pano geçmişi, ona dokunulmaz.
  - `#RRGGBB` metin renk örneğiyle, e-posta adresi "E-posta yaz" düğmesiyle gösterilir.
- **Kabul:** Parola yöneticisinden kopyalanan kaydedilmiyor; "toplanti" araması "Toplantı"yı buluyor; 200/30 gün sınırı çalışıyor.

### 3.10 Raf ve paylaşım (W2)

- **Mac:** [ShelfStore.swift][mac-shelfstore], [ShelfActions.swift][mac-shelfactions], [ShelfViews.swift][mac-shelfviews], [FileDragWatcher.swift][mac-dragwatcher], [AirDropReceiver.swift][mac-airdrop]
- **Windows:**
  - Pill'e sürükle-bırak (`AllowDrop`, `DragOver`/`Drop`, `GetStorageItemsAsync`).
  - Paylaşım: `IDataTransferManagerInterop.ShowShareUIForWindow` (Nearby Share dahil).
  - Zip: `System.IO.Compression`. Görsel dönüştürme: `BitmapDecoder`/`BitmapEncoder` ile PNG/JPEG.
- **Davranış:**
  - Sürükleme pill'e girince ada iki hedef açar: "Rafa bırak" ve "Paylaş" (Mac'te AirDrop).
  - Raf dosyanın kendisini gösterir, taşınsa da bulur; ayarla kopya alınır.
  - Raftan dışarı sürüklenebilir.
  - Sağ tık menüsü: aç, klasörde göster, paylaş, zip, dönüştür, kaldır.
- **Yapılamayan:** AirDrop alımı. Nearby Share ile gelenler İndirilenler'e düşer; Mac'teki "sharingd" işaretinin Windows karşılığı yok, ayırt edilemez. İsteğe bağlı olarak İndirilenler izlenip yalnızca "yeni dosya" banner'ı gösterilebilir.
- **Kabul:** Explorer'dan bırakma, rafta kalma, dışarı sürükleme ve paylaş penceresi çalışıyor.

### 3.11 Bildirim aynası (W3, MSIX gerekir)

- **Mac:** [NotificationDatabase.swift][mac-notifdb], [NotificationModule.swift][mac-notif], [NotificationViews.swift][mac-notifviews]
- **Windows:**
  - `UserNotificationListener` (`userNotificationListener` yeteneği, paket kimliği, kullanıcı izni `RequestAccessAsync`).
  - `NotificationChanged` olayı kullanılır. Olay masaüstünde çalışmazsa yalnızca ada açıkken `GetNotificationsAsync` okunur; sürekli sorgu yok.
- **Davranış:**
  - Banner: uygulama simgesi, başlık, alt başlık, gövde.
  - Üzerine gelince süre durur; tıklayınca bildirimi gönderen uygulama açılır.
  - Kuyruk: en fazla 5 bekleyen, "+N".
  - Uygulama bazında susturma listesi (görülen uygulamalar ayarlarda listelenir).
  - Zamanlama: her zaman / yalnızca kilit açıkken.
  - Odak modunda gösterme (Windows: Odak Yardımı/Rahatsız Etme durumu; okunabiliyorsa).
- **Kabul:** Teams/Outlook/Chrome bildirimi adada çıkıyor; susturulan uygulama çıkmıyor; izin verilmemişse ayarlarda yönlendirme var.

### 3.12 AI: ajan hook'ları ve çalışma kartı (W4)

- **Mac:** [AgentsModule.swift][mac-agents], [AgentViews.swift][mac-agentviews], [AgentHooks.swift][mac-hooks], [CommandLineTool.swift][mac-cli]
- **Hook kurulumu (Mac ile aynı mantık):**
  - **Claude Code:** `%USERPROFILE%\.claude\settings.json`. Olaylar: `SessionStart, UserPromptSubmit, PreToolUse, PostToolUse, PermissionRequest, Notification, Stop, SessionEnd`.
  - **Codex:** `%USERPROFILE%\.codex\hooks.json`. `Notification` hariç aynı olaylar; kullanıcı Codex'te `/hooks` ile güvenmeli.
  - Her olaya matcher'sız bir grup eklenir: `{"type":"command","command":"\"…isle8.exe\" hook claude","timeout":5}`, `PermissionRequest` için `600`.
  - Yalnızca `isle8 hook` çağıran girdilere dokunulur; ilk değişiklikten önce `.isle8-backup` yedeği alınır.
  - **Hook'ları ISLe8 kendiliğinden kurmaz;** kullanıcı Ayarlar > Ajanlar'da "Bağla"ya basar. Bu dosyalar o bilgisayardaki bütün oturumları etkiler.
- **Oturum modeli:**
  - Durum: hazır / çalışıyor / girdi bekliyor / onay bekliyor / bitti. Eylem metni araçtan türetilir: "$ npm test", "Okuyor: X", "Düzenliyor: X", "Arıyor: X", "Plan güncellendi".
  - Son 8 adım ve her birinin süresi tutulur.
  - **Plan:** Claude `TodoWrite` (`todos[].content/status/activeForm`) ya da Codex `update_plan` (`plan[].step/status`) → "Adım 3/6" ve plan kadar parçalı çubuk. Plan yoksa adım sayısı.
  - Değişen dosyalar: `Write`, `Edit` ve `MultiEdit`'in `file_path`'i; `apply_patch` metnindeki "*** Update/Add File:" satırları.
  - Bitince: "Bitti · 13 sn · 6 dosya değişti" ve 4 sn'lik banner (ayar).
  - Bitmiş oturum 10 dk sonra listeden düşer.
- **Onay:**
  - Ada "Onayla / Reddet / Terminalde sor" gösterir. Zaman aşımı 90 sn (15–300) dolunca karar ajana bırakılır.
  - Onay bekleyen ajan kulakta her şeyin önüne geçer; açılışta AI sekmesi gelir.
- **"Oturuma git":**
  - `app_pid`'deki uygulama öne getirilir.
  - Windows'ta ebeveyn zinciri `CreateToolhelp32Snapshot` ile yürünür. Windows Terminal'de pencere `WindowsTerminal.exe`'ye aittir.
  - Öne getirme Windows'un ön plan kısıtlarına takılabilir: ada tıklamayı aldığı anda `SetForegroundWindow` çağrılmalı; gerekirse `AllowSetForegroundWindow`.
- **`isle8 activity`:** Betikler kulağa ve AI sekmesine canlı aktivite koyabilir (başlık, alt başlık, ilerleme).
- **Kabul:**
  - Sahte hook olaylarıyla kart doğru ("Adım 2/4", süreler, bitti satırı).
  - Gerçek Claude Code oturumunda onay adadan veriliyor.
  - ISLe8 kapalıyken hook ajanı bekletmiyor.

### 3.13 Kullanım limitleri (W4, LimitBar'dan)

- **Mac:** [Modules/Usage][mac-usage] (`UsageModule`, `UsageFetchers`, `UsageSpawn`, `UsagePolling`, `UsageWatcher`, `UsagePower`), [UsageViews.swift][mac-usageviews]. Lisans: LimitBar MIT; NOTICE'a eklenmeli.
- **Kaynaklar:**
  - **Codex:** app-server + oturum kayıtları `%USERPROFILE%\.codex\sessions`, `FileSystemWatcher` ile anında.
  - **Claude:** `claude -p /usage --output-format json --max-turns 1` + izolasyon bayrakları. Bayraklar: `--restricted --strict-mcp-config --no-session-persistence --settings {"disableAllHooks":true,"language":"English"}`. **Hook'lar kapalı**; aksi halde ISLe8'in kendi hook'ları tetiklenir.
  - **Antigravity:** `agy -p /quota`.
- **Güvenlik:**
  - Token, çerez ya da kimlik bilgisi asla okunmaz.
  - Seatbelt Windows'ta yok. Her CLI kendi Job Object'inde çalışır (`KILL_ON_JOB_CLOSE`), izin listeli ortam değişkenleri ve boş geçici bir çalışma klasörüyle, süre sınırıyla, `CREATE_NO_WINDOW` ile.
  - CLI çıktısı saklanmaz.
  - Önbellek `%LOCALAPPDATA%\LRXe8\ISLe8\usage`; yalnızca yüzde, zaman ve plan adı tutar.
- **Tempo:**
  - Kapalıyken 5 dk, açıkken 1 dk; **tasarruf modu varsayılan açık**: 15 dk.
  - Claude en fazla 20 dk'da bir; elle yenileme en fazla 30 sn'de bir.
  - Hata geri çekilmesi 1, 2, 4, 8, 16, 30 dk. Kurulu değil ya da giriş yok durumunda 5 dk'da bir kontrol.
  - Uyku sırasında hiçbir şey çalışmaz.
- **Görünüm:**
  - AI sekmesinin altında servis başına halka: %70'te turuncu, %90'da kırmızı. Pencere etiketleri: "5 saat", "Haftalık".
  - %90'ı geçince 6 sn'lik banner; aynı pencere tekrar geçmedikçe bir kez.
- **Kabul:** Üç CLI kuruluyken ve girişliyken değerler Mac'tekiyle aynı; kapalı adada CLI temposu ölçümle doğrulanıyor.

### 3.14 Kablosuz cihaz pilleri ve Cihazlar sekmesi (W5)

- **Mac:** [DevicesModule.swift][mac-devices], [DeviceViews.swift][mac-deviceviews]
- **Windows:**
  - BLE cihazlar: GATT Battery Service 0x180F/0x2A19.
  - Klasik kulaklıklar: PnP özelliği `{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2`.
  - Bağlanma ve kopma `DeviceWatcher` ile izlenir.
  - **AirPods** pili yalnızca Apple yakınlık yayınlarından (`BluetoothLEAdvertisementWatcher`, üretici 0x004C, tip 0x07) okunabilir. Tarama yalnızca Cihazlar sekmesi görünürken açılır.
- **Davranış:**
  - Aynı adlı kayıtlar birleştirilir (Mac'te AirPods LE ve klasik olarak iki kez görünüyor).
  - Kısa ad: "Deniz (AirPods Pro 3)" → "AirPods Pro 3".
  - **Cihazlar sekmesi:** iPhone'un piller widget'ı gibi. Önce "Bu Mac" (Windows'ta "Bu bilgisayar") kartı, sonra cihaz başına kart. Her pil için içinde cihaz simgesi olan bir halka; altında yüzde, kartın altında ad. AirPods'ta Sol, Sağ ve Kutu ayrı halkalar. Kartlar halka sayısına göre genişler; tek kart ortada ve sınırlı genişlikte.
  - Renkler: yeşil; %30 altı turuncu; %15 altı kırmızı.
  - Bağlanınca 4 sn'lik banner. %15'te 5 sn'lik uyarı; %30'un üstüne çıkana kadar bir kez.
  - Okuma: bağlanma/kopma olayında 2 ve 10 sn sonra; ada açılınca 60 sn'den eskiyse; bağlıyken 10 dk'da bir. Cihaz yoksa hiçbir şey çalışmaz.
  - Başlıkta yalnızca pili en az olan cihazın çipi.
- **Kabul:** Bir BLE fare, klasik bir kulaklık ve AirPods'la değerler Windows Ayarları'ndakiyle aynı; çift kayıt yok.

### 3.15 Temalar ve düzenleyici (W5)

- **Mac:** [Theme.swift][mac-theme], [ThemeSettings.swift][mac-themesettings], [IslandView.swift][mac-islandview] (`IslandBackground`)
- **Davranış:**
  - Ada kapalıyken her temada siyah kalır, notch/pill ile kaynaşır.
  - Renkli temalarda üstteki başlık bandı siyahtan temaya geçer.
  - Kartlar tema yüzeyi, kenarlığı ve köşe yarıçapıyla çizilir.
  - Vurgu rengi her yerde (seçili sekme, bugün, halkalar, düğmeler).
  - Yazı tipi ailesi temadan gelir.
  - **Galeri:** her tema için küçük önizleme. **Düzenleyici:** malzeme, renkler, köşe, yazı tipi. JSON olarak dışa/içe aktarma; içe aktarılan tema bir hazır temanın ya da var olan bir temanın kimliğiyle çakışırsa yeni kimlik alır.
  - Windows: Liquid Glass → Mica/Acrylic. Kapak rengi (artwork) teması, SMTC küçük resminin baskın rengiyle çizilir.
- **Kabul:** Mac'te dışa aktarılan bir tema Windows'ta aynı görünüyor; tersi de.

### 3.16 Komut paleti (plana eklenmeli: W2)

- **Mac:** [PaletteModule.swift][mac-palette], [PaletteView.swift][mac-paletteview]
- **Windows:** Ctrl+Alt+K (`RegisterHotKey`). Uygulamalar Başlat menüsü kısayollarından ve `shell:AppsFolder`'dan, ilk açılışta arka planda listelenir.
- **Davranış:**
  - Yazdıkça eşleşme puanı: başlığın başı 80 (uygulamalarda 60), kelime başı 60 (uygulamalarda 40), anahtar kelime 30, içinde geçme 20. Hiçbir şey yazılmamışken yalnızca yerleşik komutlar görünür; uygulamalar yazınca gelir. En fazla 8, yazınca 10 sonuç.
  - "25", "25 dk", "25min" yazınca zamanlayıcı başlatma önerisi.
  - En az 2 karakter yazılınca pano araması da sonuçlara girer (en fazla 3).
  - Oklarla seçilir, Enter çalıştırır, Esc kapatır.
  - Komutlar: sekmeye git, oynat/duraklat, sonraki/önceki, Pomodoro, kronometre, uyanık tut, rafı paylaş/temizle, ayarlar, çıkış.
- **Kabul:** Kısayol odak çalmadan açılıyor; yazınca anında süzüyor.

### 3.17 Ayarlar penceresi, tray ve başlangıç (W1 / W6)

- **Mac:** [SettingsView.swift][mac-settingsview], [CustomizationSettings.swift][mac-custom], [StatusItemController.swift][mac-statusitem]
- **Windows:**
  - Ayrı bir WinUI penceresi: sol menü + bölümler, Mica arka plan.
  - Tray: `Shell_NotifyIcon` ile Ayarlar ve Çıkış.
  - Başlangıçta açılma: MSIX `StartupTask`, paketsizse `HKCU\…\Run`.
  - Uygulama yeniden açılınca (ikinci örnek) Ayarlar açılır.
- **Ayarlar (Mac ile aynı bölümler):** Genel, Görünüm, Düzen, Kısayollar, Medya, Raf, Pano, Bildirimler, Sistem, Widget'lar, Ajanlar. "Kilit Ekranı" yok.
- **Genel bölümü:**
  - Açılma: üzerine gelince / tıklayınca; açılma ve kapanma gecikmesi.
  - Ekran: otomatik / ana / etkin / adla.
  - Dil: Sistem / Türkçe / English; değişince yeniden başlatır.
  - Ayarları JSON olarak dışa/içe aktarma.
- **Kabul:** Şu an JSON'dan değişen her ayar pencereden değişebiliyor.

### 3.18 Taşınmayacaklar

| Mac | Neden |
|---|---|
| Kilit ekranı görünümü | Windows kilit ekranına üçüncü taraf pencere giremiyor |
| AirDrop alma | Karşılığı yok (Nearby Share, İndirilenler; §3.10) |
| Sistem ses göstergesinin yerine geçme | Windows'un kendi göstergesini gizlemenin desteklenen bir yolu yok; ISLe8 yanında gösterir |
| Kestirmeler (Shortcuts) | Windows'ta karşılığı yok; palet yalnızca uygulamaları listeler |

---

## 4. DESIGN.md §4 ile farklar

- **Plana eklenmesi gerekenler:** ses/parlaklık göstergesi (§3.2), kamera/mikrofon göstergesi ve uyanık tut (§3.4), komut paleti (§3.16), ayarlar penceresi (§3.17), takvim hızlı ekleme (§3.6), `isle8.exe` konsol yardımcısı (§1).
- **Öne alınması gereken:** MSIX paket kimliği. Bildirim aynası, takvim, App Execution Alias ve `StartupTask` hepsi paket kimliği istiyor. Kimlik kararı W1'de verilmeli (tam MSIX ya da harici konumlu "sparse" paket). Sürüm zip'leri bu kararla değişir.
- **Varsayılan değişiklikleri:** Tasarruf modu varsayılan açık. Açılma gecikmesi 0. Bütün sekmeler aynı yükseklikte.

## 5. Önerilen aşama sırası

| Aşama | İçerik |
|---|---|
| W1 · Temel + Home | Paket kimliği kararı, ayarlar penceresi, tray, §2'deki ada kuralları, dock, Geniş/Kompakt/Minimal, Home sütunları (takvim, medya + ses kaydırıcısı, hava, zamanlayıcı), pil + W, medya düzeltmeleri |
| W2 · Pano + Raf + Palet | Pano geçmişi, raf ve paylaşım, Ctrl+Alt+K paleti |
| W3 · Bildirim + Sistem | Bildirim aynası, ses/parlaklık göstergesi, kamera/mikrofon, uyanık tut |
| W4 · AI | `isle8.exe` + named pipe, hook kurucu, çalışma kartı, onaylar, kullanım limitleri |
| W5 · Cihazlar + Tema | Bluetooth pilleri ve Cihazlar sekmesi, Mica/Acrylic, tema düzenleyici ve JSON |
| W6 · Dağıtım | İmzalama, başlangıçta açılma, tam ölçüm turu, TESTING.md'nin kalanları |

[mac-strings]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Resources/Localizable.xcstrings
[mac-theme]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Core/Theme.swift
[mac-cli]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/App/CommandLineTool.swift
[mac-server]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Core/LocalServer.swift
[mac-model]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Core/IslandModel.swift
[mac-settings]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Core/SettingsStore.swift
[mac-module]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Core/IslandModule.swift
[mac-islandview]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Views/IslandView.swift
[mac-controller]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Window/IslandController.swift
[mac-panel]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Window/IslandPanel.swift
[mac-header]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Views/HeaderBar.swift
[mac-home]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Views/HomeView.swift
[mac-appdelegate]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/App/AppDelegate.swift
[mac-nowplaying]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/Media/NowPlayingService.swift
[mac-nowplaying-model]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/Media/NowPlaying.swift
[mac-mediaviews]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/Media/MediaViews.swift
[mac-hud]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/System/HUDModule.swift
[mac-volume]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/System/VolumeMonitor.swift
[mac-brightness]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/System/BrightnessMonitor.swift
[mac-systemviews]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/System/SystemViews.swift
[mac-sysmon]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/System/SystemMonitors.swift
[mac-system]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/System/SystemModule.swift
[mac-weather]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/Weather/WeatherModule.swift
[mac-weatherviews]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/Weather/WeatherViews.swift
[mac-calendar]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/Calendar/CalendarModule.swift
[mac-calendarviews]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/Calendar/CalendarViews.swift
[mac-timer]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/Timer/TimerModule.swift
[mac-timerviews]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/Timer/TimerViews.swift
[mac-custom]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Views/CustomizationSettings.swift
[mac-clipservice]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/Clipboard/ClipboardService.swift
[mac-clipstore]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/Clipboard/ClipboardStore.swift
[mac-clipitem]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/Clipboard/ClipboardItem.swift
[mac-clipviews]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/Clipboard/ClipboardViews.swift
[mac-shelfstore]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/Shelf/ShelfStore.swift
[mac-shelfactions]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/Shelf/ShelfActions.swift
[mac-shelfviews]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/Shelf/ShelfViews.swift
[mac-dragwatcher]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/Shelf/FileDragWatcher.swift
[mac-airdrop]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/Shelf/AirDropReceiver.swift
[mac-notifdb]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/Notifications/NotificationDatabase.swift
[mac-notif]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/Notifications/NotificationModule.swift
[mac-notifviews]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/Notifications/NotificationViews.swift
[mac-agents]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/Agents/AgentsModule.swift
[mac-agentviews]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/Agents/AgentViews.swift
[mac-hooks]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/Agents/AgentHooks.swift
[mac-usage]: https://github.com/Mericsdc/ISLe8-macOS/tree/main/ISLe8/Modules/Usage
[mac-usageviews]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/Agents/UsageViews.swift
[mac-devices]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/Devices/DevicesModule.swift
[mac-deviceviews]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/Devices/DeviceViews.swift
[mac-themesettings]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Views/ThemeSettings.swift
[mac-palette]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/Palette/PaletteModule.swift
[mac-paletteview]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/Palette/PaletteView.swift
[mac-settingsview]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Views/SettingsView.swift
[mac-statusitem]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/App/StatusItemController.swift
