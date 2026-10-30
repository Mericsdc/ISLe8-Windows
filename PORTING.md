# macOS → Windows taşıma rehberi

Bu dosya, macOS sürümündeki sistemlerin Windows'a nasıl taşınacağını modül modül anlatır. Her aşamada (W1–W6) bu dosya ile [DESIGN.md](DESIGN.md) §4–5 birlikte izlenir.

**Referans:** [Mericsdc/ISLe8-macOS](https://github.com/Mericsdc/ISLe8-macOS) (public, Apache-2.0). Davranışın tek doğru kaynağı oradaki koddur. Bu rehber `b3dc5de` üzerine yazıldı, `dab77e8` ile güncellendi (Kompakt/Minimal şeritleri, Notlar, Sistem sekmesi, LAN, klavye temizliği, özel günler, iPhone/iPad pilleri, durum kulakları). Mac'te bir davranış değişirse burası da güncellenir. Aşağıdaki dosya bağlantıları `main` dalını gösterir.

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
- Mac: [Theme.swift][mac-theme]. Hazır temaların değerleri oradaki `presets` listesindedir. Bir temayı değiştirmek **kopya oluşturmaz**: değişiklik aynı kimlikle kullanıcının temalarına yazılır; değiştirilmiş hazır tema "Varsayılana döndür" ile ilk haline döner.

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

**Windows'ta yapılan (2026-10-29):**
- Konsol projesi `src/ISLe8.Cli`. Dosyası **`isle8-cli.exe`**: NTFS büyük/küçük harf ayırmadığı için `isle8.exe` aynı klasördeki `ISLe8.exe` ile aynı dosya olurdu. Paketin ikinci uygulama girdisi (`Cli`, `desktop4:Subsystem="console"`, Başlat'ta yok) `isle8.exe` takma adını verir; her terminalde `isle8` çalışır. Zip sürümünde komut klasördeki `isle8-cli.exe`.
- Pipe adı `\\.\pipe\isle8-<kullanıcı SID>-<oturum no>`, iki uçta da `PipeOptions.CurrentUserOnly`. ISLe8 kapalıyken bağlantı 150 ms içinde vazgeçer.
- Hook'un stdin'i UTF-8; başında BOM (PowerShell'in borusu) ya da UTF-16 gelirse de okunur.
- `app_pid`: ebeveyn zinciri `CreateToolhelp32Snapshot` ile; kendi görünür penceresi olan ilk süreç (Windows Terminal, VS Code, Claude). Explorer'da durulur; bulunamazsa klasik konsolun penceresi.
- `--icon`, SF Symbol yerine bir Segoe Fluent Icons kod noktası alır (`E768`).

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
| Sabit pencere | Pencere en büyük ada kadar sabit; animasyon sırasında yeniden boyutlanmaz. **Görünümler de pencere boyutunda sabit bir çerçevede, notch'un altında ortalı durur; yalnızca zemin ve kırpma şekli büyür.** Her durumun (kulaklar, banner, açık panel) kendi çerçevesi vardır. Büyüyen bir çerçevenin içinde yeni beliren görünüm o çerçevenin sol kenarına göre yerleşir ve açılırken sağdan sola kayar; ada sağa doğru büyüyormuş gibi görünür. Mac'te bu hata iki kez yaşandı | [IslandView.swift][mac-islandview], [IslandShape.swift][mac-shape] `CenteredIslandShape` |
| Tema geçişi | Tema rengi siyahın **üstüne** açılır, siyahı değiştirmez; yoksa geçişin ortasında ada yarı saydam görünür. Yalnızca cam teması siyahı söndürür | [IslandView.swift][mac-islandview] `IslandBackground` |
| Pencerelerin altında kalmama (Windows) | Mac'te notch menü çubuğunda, pencerelerin dışında. Windows'ta ada ekranın üstünde uygulama çubuğu olarak 44 DIP'lik bir şerit ayırır (görev çubuğu gibi); tam ekran pencereler pill'in altında başlar. Ayarla kapatılabilir | — |
| Hover alanı | Yalnızca adanın kendisi. Her giriş/çıkış olayı imlecin **gerçek konumuyla** doğrulanır; işletim sistemi sahte olay yollayabiliyor | [IslandPanel.swift][mac-panel] |
| Sekme yükseklikleri | Geniş'te bütün sekmeler aynı yükseklikte. Kompakt (Home 100) ve Minimal (Home 64) şeritlerinde diğer sekmeler 196; aradaki geçişi bir alttaki "slack" kuralı taşır. İçerik 0,18 sn'de çapraz geçer | [IslandModel.swift][mac-model] `contentHeight` |
| Küçülürken açık kalma (slack) | Açık ada kısalırsa (Kompakt/Minimal Home'a dönüş) imleç eski çerçevenin içindeyken açık kalır. İmleç yeni çerçeveye girince ya da ikisinden de çıkınca normale döner | [IslandPanel.swift][mac-panel] `setIslandSize` |
| Dışarı tıklama | Ada açıkken ve imleç üstünde değilken başka yere tıklamak adayı kapatır (sabitlenmişse kapatmaz) | [IslandModel.swift][mac-model] `clickedOutside` |
| Sekme hafızası | 15 sn içinde yeniden açılırsa son sekme, yoksa ilk sekme. Onay bekleyen ajan varsa AI sekmesi | aynı, `chooseTabForOpening` |
| İki parmakla kaydırma | Yatay kaydırma (60 pt) sonraki/önceki sekme. Raf, Pano ve Palet'te yalnızca başlık ve dock satırında | [IslandController.swift][mac-controller] `trackSwipe` |
| Başlık çipleri | Pill'in iki yanındaki alana sığar, asla taşmaz. Sığmazsa sırayla düşer: cihaz çipi → W → uyanık tut. Pil, LAN ve kamera/mikrofon kalır | [HeaderBar.swift][mac-header] |
| Kulak önceliği | Onay bekleyen ajan 100 (her zaman önce), ajan 55, medya 50, zamanlayıcı 40, kamera/mikrofon 30; ayarlardan sıralanabilir | [IslandModule.swift][mac-module] |
| Uyarı süreleri | Şarkı değişimi 2,5 sn, AirDrop 3, pil 3, toplantı 10, zamanlayıcı 4, onay 15, ajan bitti 4, limit 6, cihaz bağlandı 4, cihaz pili 5, şarj/LAN kulakları 3, bildirim 4 (ayar). Banner üzerine gelinince bekler | [AppDelegate.swift][mac-appdelegate] |
| Durum kulakları | Şarj aleti takıldı/çıkarıldı, Ethernet kablosu takıldı/çıkarıldı: ada banner açmaz, notch'un iki yanına doğru açılır (kulak 104). Yay response 0,42, damping 0,6 (görünür bir sekme); simge ve yazı notch'un içinden küçük başlayıp dışarı kayar, simge bir kez zıplar. Kapanırken önce ikisi içeri kayar (0,22 sn), sonra ada aynı yayla küçülür. Takınca yeşil, çıkarınca beyaz ("%61" ya da "Wi-Fi"/"Çevrimdışı"). "Tam şarj oldu" bildirimi yok | [IslandModel.swift][mac-model] `isStatusAlert`, [SystemViews.swift][mac-systemviews] `StatusEarsView` |
| Bildirim kuyruğu | En fazla 5 bekleyen, banner'da "+N". Ada açıkken gelenler kaybolmaz, kapanınca sırayla gösterilir | [IslandModel.swift][mac-model] `showNotification` |

**Windows'a özgü ada seçenekleri (2026-10-30, Ayarlar › Görünüm › Ada).** Mac'te ada notch'tan sarkar ve ortası notch'tur;
Windows'ta ikisi de serbest, bu yüzden kullanıcı isteğiyle eklendi. Hepsinin varsayılanı Mac'teki görünüm.

- **Konum:** ekranın üstü (varsayılan) ya da görev çubuğunun üstünde solda, ortada, sağda. Görev çubuğundayken pill çalışma
  alanının altından 6 DIP yukarıda durur, ada yukarı doğru açılır (yaylar ve boyutlar aynı); köşede ekran kenarından 16 DIP içeride,
  o kenara yaslanarak büyür. Banner'larda boş pill satırı yok (0.2.7, aşağıda "Windows'a özgü: oyunlar…"). Görev çubuğundaki simgeleri kapatmaz.
- **Görev çubuğunun içinde** (0.2.9, kullanıcı isteği: "görev çubuğunun içine ama konumu ayarlanabilir olsun, görev
  çubuğundan adayı açayım"): kapalı ada görev çubuğunun şeridinin ortasında durur (çalışma alanının altıyla ekranın altı
  arası), yeri **Görev çubuğundaki yeri** kaydırıcısıyla %0–100 (pill'in ortası, varsayılan %25). Açılınca pill'in alt
  kenarından yukarı doğru, ekran kenarından 16 DIP içeride kalarak büyür; geniş pill ve banner'lar pill'in yerini korur
  (`IslandLayout.Align` artık 0–1 arası bir oran). Şerit ayrılmaz. Görev çubuğuna tıklanınca görev çubuğu kendini en üste
  alır; ada bunu `EVENT_SYSTEM_FOREGROUND` ile duyar ve hemen ve 250 ms sonra yeniden en üste çıkar. Kendini gizleyen ya da
  yanda/üstte duran görev çubuğunda şerit yok: ada çalışma alanının kenarında durur.
- **Üst kenara bitişik:** pill'in üstünde boşluk yok; ada Mac'in notch şekli olur (IslandShape.swift): üst kenara iki içbükey
  omuzla akar (boşta 0, geniş pill ve peek 6, banner 8, açık ada 10 DIP) ve alt köşeleri Mac'teki gibi yuvarlanır (boşta 9, geniş pill
  12, peek 14, banner 20, açık ada temanınki). Omuzlar, köşesine çember ortalanmış kalın bir halka olarak çizilip kareye kırpılır (bu
  pencerede maske fırçası çizilmiyor); yerleri ve boyutları şeklin yaylarına bağlı. Pencere bölgesi omuz kadar geniştir.
- **Boştayken saat:** boştaki pill'in ortasında kullanıcının bölge ayarındaki kısa saat; dakikada bir, dakika başında güncellenir.
- **Şarkı adı:** müzik çalarken geniş pill'de kapak ile çubukların arasında; sığmazsa her turun başında 1,6 sn bekleyip 28 DIP/sn
  kayar (kompozisyon animasyonu, ISLe8 tarafında zamanlayıcı yok). Yalnızca geniş pill görünürken kayar.
- **Tam ekranda gizle** (Ayarlar › Genel; önceden ayarı vardı, düğmesi yoktu).
- **Büyütülen pencereler** (0.2.3, Görünüm › Ada; önce Genel'de aç/kapat): adanın altından (şerit pill + 6 DIP büyüme payı),
  pill'in hemen altından (şerit tam pill kadar; büyüyen pill pencereye 6 DIP taşar) ya da ekranın en üstünden (şerit yok). Görev
  çubuğundayken şerit görev çubuğunun üstünde (`ABE_BOTTOM`).

### Boyutlar (Windows'ta etkin piksel / DIP)

| Düzen | Genişlik | İçerik yüksekliği | Not |
|---|---|---|---|
| Geniş (Home panosu) | 820 (ayar 680–980) | 304 | Bütün sekmeler 304 |
| Kompakt | ~870 (sütunların ihtiyacından) | Home 100, diğer sekmeler 196 | Kartsız şerit; oynatıcı notch'un tam altında |
| Minimal | ~720 | Home 64, diğer sekmeler 196 | Tek ince satır; oynatıcı ortada |

- **Ada yüksekliği:** başlık satırı + 6 + içerik + dock alanı (8 + 36 + 10). ⌘K paletinde dock yok, alt boşluk 14.
- **Kulaklar:** 40; ses/parlaklık göstergesinde 92 (çubuk + yüzde); şarj/LAN kulaklarında 104.
- **Alt köşe yarıçapı:** açık adada `min(tema köşesi + 12, 28)`.
- **Geniş'te Home sütun ağırlıkları:** takvim 1 · medya 1,1 · hava 1 · zamanlayıcı 0,62; sütunlar kart.
- **Kompakt ve Minimal şeritleri** (`HomeStrip`): kart yok, sütunlar 1 pt'lik ince çizgilerle ayrılır (çizginin iki yanında 10 boşluk). Her sütunun ihtiyaç duyduğu genişlik var. Kompakt: takvim 270, medya 240, hava 270, zamanlayıcı 120. Minimal: 170, 250, 190, 130. Oynatıcı ortada, notch'un altında durur; iki yanındaki sütunlar eşit yarıları paylaşır. İki yan 80'den fazla dengesizse (zamanlayıcı bir yanda) sütunlar yan yana dizilir. Ada genişliği = şerit + 48 (omuzlar ve kenar boşlukları).
- Mac: [HomeView.swift][mac-home].

---

## 3. Modüller

Her modülde: Mac dosyaları → Windows karşılığı → önemli davranışlar → kabul kriterleri. Aşama önerisi §5'te.

### 3.1 Medya (W0 bitti; kalanlar W1)

- **Mac:** [NowPlayingService.swift][mac-nowplaying], [MediaViews.swift][mac-mediaviews], [NowPlaying.swift][mac-nowplaying-model]
- **Windows:** SMTC (var).
- **W1 adım 4'te yapıldı** (kurallar `MediaRules`'ta, Core testleriyle):
  - Kapak, yalnızca parça değişince değil **küçük resim referansı değişince** de yenileniyor (`ArtworkVersion`); Chrome önce eski kapağı yolluyor.
  - Duraklatınca konum durduğu yerde tutuluyor; oynat'ta zaman çizelgesi yeniden okunuyor, oynatıcı yeni rapor yollamadıysa konum devam ettiği andan sayılıyor. Yoksa ilerleme duraklatma süresi kadar sıçrıyordu.
  - İlk SMTC isteği başarısız olursa 1, 2, 4, 8, 16 sn sonra, sonra 30 sn'de bir yeniden deneniyor.
  - Bütün oturumların oynatma durumu dinleniyor: `GetCurrentSession` duraklatılmış bir oturum verirken başka biri çalıyorsa çalan seçiliyor (`GetSessions`).
  - Kapalıyken SMTC olayları yalnızca pill'i, kapağı ve çubukları güncelliyor; Home açılınca yetişiyor.
- **Şarkı banner'ı (Mac: MediaModule.swift):** ISLe8 açıldığında çalan ve duraklatılmışken seçilip sonra
  çalınan şarkı banner almaz. Windows'a özgü: SMTC yeni bir oturumu önce başlıksız bildiriyor; o rapor
  sayılmıyor, başlıklı olan sayılıyor.
- **Taşınmayanlar:**
  - Ayarlar › Medya'daki "MediaRemote köprüsü" satırı: Windows'ta köprü yok, SMTC sistemin parçası.
  - Medya sütunundaki uygulama simgesi ("Uygulamayı aç"): henüz yok. Simge AUMID'den
    `shell:AppsFolder\<AUMID>` ve `IShellItemImageFactory` ile alınabilir; Spotify'ın masaüstü sürümü
    gibi kısayolu farklı AUMID'li oynatıcılarda bulunamayabilir.
- **Ses kaydırıcısı (W1):**
  - `IMMDeviceEnumerator` + `IAudioEndpointVolume` + `IAudioEndpointVolumeCallback`. Varsayılan çıkış değişince `IMMNotificationClient.OnDefaultDeviceChanged`.
  - Değer 0–100, sessizdeyken 0. Sıfırdan yukarı çekmek sesi açar.
  - Mac: [HUDModule.swift][mac-hud] `setOutputVolume`.
- **Kabul:**
  - Spotify, Chrome/YouTube ve Medya Oynatıcı'da kapak, ilerleme, oynat/duraklat, ileri/geri, karışık çal ve tekrarla çalışıyor.
  - Kaydırıcı sistem sesiyle eşleşiyor, iki yönde de güncelleniyor.
  - Kapalı ada müzik çalarken de boşta sayılır: ses çubukları dışında hiçbir şey çalışmaz.

- **Varsayılan müzik uygulaması (W1):** Ayarlar > Medya: Son kullanılan / Spotify / Müzik. Hiçbir şey çalmıyorken oynat'a basınca seçilen uygulama (yoksa son kullanılan) arka planda açılır ve 1,5 ile 4 sn sonra iki kez "play" gönderilir; açılan uygulama ilk anda komut almıyor. Son kullanılan, çalan uygulamanın kimliğinden hatırlanır. Windows: uygulamayı başlat, SMTC oturumu belirince `TryPlayAsync`.
- **Spotify'ın kendi sesi (W1):** Kompakt'ta, Spotify çalarken kontrollerin altında ince bir çubuk; Mac'in genel sesine dokunmaz, Spotify dışındaki çalarlarda gizlenir. Mac'te AppleScript `sound volume`. Windows: Spotify'ın Core Audio oturumu (`IAudioSessionManager2` ile oturumu bul, `ISimpleAudioVolume`). Sürüklerken saniyede en fazla on kez ayarlanır.
- **Spotify Canvas videoları taşınmayacak:** Resmî API'de yok; tek yol Spotify'ın erişim korumasını aşmak (bkz. §3.18).

### 3.2 Ses ve parlaklık göstergesi (plana eklenmeli: W3)

- **Mac:** [HUDModule.swift][mac-hud], [VolumeMonitor.swift][mac-volume], [BrightnessMonitor.swift][mac-brightness], [SystemViews.swift][mac-systemviews] `HUDAlertView`
- **Windows:**
  - **Ses:** `IAudioEndpointVolumeCallback.OnNotify`; olay güdümlü, sorgu yok.
  - **Dahili ekran parlaklığı:** WMI `WmiMonitorBrightnessEvent` (`root\WMI`), COM `IWbemServices::ExecNotificationQueryAsync` ile. `System.Management` Native AOT ile uyumsuz, kullanılmaz.
  - **Harici monitör:** DDC/CI (`dxva2`), yalnızca kısayol tuşuyla; sorgu yok.
- **Davranış:**
  - Gösterge kulaklarda: solda simge, sağda seviye çubuğu ve yüzde ("%62", İngilizcede "62%"); kulak genişliği 92.
    **Windows (0.2.7):** çentik olmadığından kulakların arası boş siyah bir şerit kalıyordu (gösterge ~400 DIP, "alakasız
    büyük"); simge, çubuk ve yüzde artık tek satırda, 260 DIP genişliğinde (`IslandSizes.HudWidth`).
  - **Parlaklık göstergesi yalnızca parlaklık tuşuyla açılır.** Mac son 1,2 sn içinde bir NX_SYSDEFINED (medya/parlaklık tuşu) olayı olup olmadığına bakıyor (`CGEventSource.secondsSinceLastEventType`, izin gerektirmez). Otomatik parlaklık, ekranın kararması ve uyanması göstermez. Windows: Raw Input ile HID tüketici sayfasındaki parlaklık artır/azalt kullanımları (0x6F/0x70) dinlenir; değişiklik o tuşu izlemiyorsa gösterge açılmaz.
  - Ada açıkken gösterge çıkmaz.
- **Not:** Windows'un kendi ses göstergesini gizlemenin desteklenen bir yolu yok; pencere bulup gizleme gibi kırılgan yöntemler kullanılmaz. Mac'teki gibi tuşları ISLe8 karşılarsa gösterge hiç çıkmaz (aşağıda, 2026-10-30).
- **Kabul:** Ses tuşlarıyla gösterge çıkıyor (kaydırıcı açık adada; Mac'te de açık adanın üstüne gösterge gelmez); otomatik parlaklık gösterge açmıyor; ayardan ikisi de kapatılabiliyor.
- **Windows'ta yapılan (2026-10-29):**
  - **Ses:** `IMMDeviceEnumerator` + `IAudioEndpointVolume`, geri çağrılar (`IAudioEndpointVolumeCallback`, varsayılan aygıt
    değişince `IMMNotificationClient`) elle yazılmış COM nesneleri; Native AOT için üretilmiş interop yok. Aynı değeri tekrar
    bildiren uç nokta süzülüyor (0,001). Gösterge kulakta ve imleç adanın üstündeyken de çıkıyor; açık adanın ve bildirimin üstüne gelmiyor.
  - **Sesi sabit aygıtlar:** sesini kendi ayarlayan kulaklıklar (ör. Arctis Nova Pro) ve dijital çıkışlar Windows'a tek seviyelik
    bir aralık bildiriyor (0–0 dB); seviye hep %100. Mac'teki "ayarlanamaz" gibi: kaydırıcı yerinde kalır, gösterge yalnızca
    sessize alınınca çıkar.
  - **Parlaklık:** WMI `WmiMonitorBrightnessEvent` (`ROOT\WMI`), yarı eşzamanlı `ExecNotificationQuery` ile tek bir iş
    parçacığında; olay gelene kadar uyur. Dahili ekranı olmayan masaüstünde hiçbir şey başlamaz. Kapağı kapalı dizüstünde dahili
    ekran WMI'da görünmez; kapağı olan bilgisayar (`GetPwrCapabilities`, LidPresent) olayları yine de bekler, kapak açılınca çalışır.
  - **Parlaklık tuşları:** Raw Input, HID tüketici sayfası (0x0C/0x01, `RIDEV_INPUTSINK`), `HidP_GetUsages` ile 0x6F/0x70; yalnızca
    dahili ekranı olan ya da kapaklı bilgisayarda kaydedilir. Fn tuşunu kendi sürücüsüyle (ACPI) karşılayan dizüstülerde tuş
    Windows'a HID olarak gelmez; orada parlaklık göstergesi çıkmaz (otomatik parlaklık gibi sayılır).
  - **"Sistem göstergesinin yerine ISLe8'inkini kullan" (2026-10-30, varsayılan kapalı, Mac'teki gibi):** ses tuşları
    (VK_VOLUME_MUTE/DOWN/UP) kendi iş parçacığındaki alt düzey klavye kancasıyla (`WH_KEYBOARD_LL`) önce ISLe8'e gelir; ISLe8
    sesi kendisi ayarlar, Windows'un göstergesi çıkmaz. Başka tuşa bakılmaz. Adım Windows'unki gibi %2, tam adımlara oturur (Mac
    16 adımına oturuyor); yukarı tuşu sessizi açar. Sesi sabit aygıtta (sesini kendi ayarlayan kulaklık) Windows da sesi
    değiştiremiyor; tuş yine ISLe8'de kalır ve gösterge seviyeyi gösterir (2026-10-30 düzeltme: önce Windows'a bırakılıyordu, Windows'un
    göstergesi çıkıyordu). Hiç çıkış aygıtı yokken tuş Windows'a bırakılır. Mac izin
    istiyor; Windows'ta izin gerekmez. **Parlaklık tuşları** klavye kancasına hiç gelmez (ACPI ya da HID tüketici kullanımı,
    sanal tuş kodu yok), bu yüzden Windows'un parlaklık göstergesi kapatılamaz; ISLe8'inki onun yanında çıkar.
  - **Yapılmayan:** Mac'teki ince adım (Shift+Option ile 1/64): Windows'ta yok. Harici monitör (DDC/CI): Mac de yalnızca dahili
    ekranı izliyor.

### 3.3 Pil, şarj gücü (W1)

- **Mac:** [SystemMonitors.swift][mac-sysmon], [SystemModule.swift][mac-system], [SystemViews.swift][mac-systemviews]
- **Windows:**
  - `Microsoft.Windows.System.Power.PowerManager` olayları (yüzde, şarj durumu, güç kaynağı).
  - Watt: `Windows.Devices.Power.Battery.AggregateBattery.GetReport().ChargeRateInMilliwatts`, **yalnızca ada açıkken** okunur.
  - **Mac'ten farkı:** Mac adaptörden giren gücü gösterir; Windows'ta adaptör gücünü veren genel bir API yok. `ChargeRateInMilliwatts` **pile giren** gücü verir: pil dolunca ya da şarj durunca 0 ya da boştur, boşalırken eksidir. Bu yüzden Windows'ta W çipi yalnızca pil gerçekten şarj olurken görünür ve açıklaması "Pile giren güç" olur.
- **Davranış:**
  - Şarja takınca ve çıkarınca §2'deki **durum kulakları** (banner değil). %20'de ve %10'da (kritik) uyarı banner'ı. "Tam şarj oldu" bildirimi yok.
  - Başlık çipi: %10 altı kırmızı, %20 altı turuncu, şarjda yeşil.
  - Şarj olurken başlıkta "⚡ 61 W" (yukarıdaki fark).
- **Kabul:** Takma/çıkarma ve eşiklerde banner çıkıyor; W yalnızca açıkken okunuyor (ölçümle).
- **Windows'ta yapılan (2026-10-29):** Pil Cihazlar'daki `PowerManager` okumasından; olay kuralları Mac'teki gibi Core'da
  (`BatteryAnnouncer`: ilk okuma sessiz, takınca uyarılar unutulur, %10 ve %20 deşarj başına bir kez, ikisini birden geçen düşüş
  kritik der). Takma/çıkarma durum kulakları, %20/%10 banner, hepsi 3 sn. Başlıkta pil çipi (yeşil/turuncu/kırmızı), W çipi
  `AggregateBattery` ile yalnızca ada açıkken ve pil gerçekten şarj olurken, sarı şimşekle "61 W", ipucu "Pile giren güç". Çipler
  sığmazsa Mac'teki sırayla düşer (cihaz çipi, sonra W). Ayarlar: Sistem › Pil, Görünüm › Başlık çubuğu › Pil.

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
  - `daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max&forecast_days=10` (bugün + 9 gün; bugünün yağış ihtimali de tutulur)
  - `timezone=auto`, Fahrenheit'ta `wind_speed_unit=mph`.
  - Koordinatlar 2 ondalığa yuvarlanır.
- **Davranış:**
  - Konum izni yok. Şehir ayarlanmamışsa saat diliminden bulunur: `TimeZoneInfo.TryConvertWindowsIdToIanaId` ile "Turkey Standard Time" → `Europe/Istanbul` → "İstanbul" (Open-Meteo geocoding).
  - Arka planda zamanlayıcı yok: Home görünür olduğunda veri 30 dk'dan eskiyse yenilenir.
  - Saatlik listenin ilk öğesi geçerli saattir; Geniş'te ada "Şimdi" + sonraki 5 saati gösterir. Günlük listenin ilki bugündür; Geniş'te "Yarın" + sonraki 4 gün.
  - **Kompakt:** solda simge + sıcaklık, şehir, gökyüzü; yanında beşer gün (Bugün, Yarın, gün adları) ve ›/‹ düğmesiyle sonraki beş gün. Bir güne dokununca altında tek satır: "Cmt 31 Eki · Sağanak · Y 18° D 12° · Yağış %70". Varsayılan seçili gün bugün.
  - **Minimal:** üstte şehir, altta simge + sıcaklık, yanında gökyüzü ve "Y 21° D 15°".
  - Rüzgar yönü 8 nokta: K, KD, D, GD, G, GB, B, KB (İngilizcede N, NE, E, SE, S, SW, W, NW).
  - "Kapalı" (overcast) yerine "Çok bulutlu" kullanılır; "Kapalı" ayarlarda "off" anlamında geçiyor.
- **Kabul:** Geniş, Kompakt ve Minimal'de doğru alanlar görünüyor; ağ yokken "Tekrar dene" çıkıyor.

### 3.6 Takvim (W1 kimlikle; okuma ve hızlı ekleme)

- **Mac:** [CalendarModule.swift][mac-calendar], [CalendarViews.swift][mac-calendarviews]
- **Windows:**
  - **Asıl kaynak ICS abonelik bağlantısı** (salt okunur; Outlook, Google ve iCloud takvimlerinin "gizli/ICS adresi"). Paket kimliği gerektirmez.
  - **Ek kaynak:** `AppointmentManager.RequestStoreAsync(AllCalendarsReadOnly)` (`appointments` yeteneği, paket kimliği gerekir), değişiklikler `AppointmentStore.StoreChanged` ile. Bu depoyu dolduran Windows Posta ve Takvim uygulamasının desteği 2024 sonunda bitti ve yeni Outlook bu depoya yazmıyor; çoğu Windows 11'de boş döner. Bu yüzden asıl yol değil.
  - **Hızlı ekleme:** `AppointmentManager.ShowAddAppointmentAsync` statik bir UWP çağrısı; Win32 penceresi için bir interop arabirimi (`IInitializeWithWindow` benzeri) belgelenmemiş, masaüstünde çalışıp çalışmadığı ilk adımda denenmeli. Çalışmazsa yedek: geçici bir `.ics` dosyası yazıp varsayılan takvim uygulamasında açmak. Uygulama başka takvimlere doğrudan yazamaz.
- **Davranış:**
  - **Geniş:** Ay görünümü, ilk gün kullanıcının yerel ayarından. Bugün vurgu renginde dolu daire, seçili gün halka. Etkinlikli günlerde takvim renginde nokta; çok günlük etkinlik her günü işaretler. Ay başlığına tıklamak bugüne döner.
  - Gün listesi: saat (ya da "Tüm gün"), başlık, ayrıntı satırı (toplantı hizmeti adı, yoksa konum, yoksa takvim adı). Toplantı linki varsa katıl düğmesi.
  - **Kompakt:** bu hafta, Geniş'teki ay görünümüyle aynı biçimde (başlık, oklar haftayı değiştirir, gün adları, gün dairesi 28). Bir güne dokununca altında küçük satır: "Per 29 · 14:00 Toplantı +1". Üzerine gelince günün etkinlikleri ipucunda. Etkinlik noktası günün 5 pt altında (seçim halkasına değmez; ay görünümünde satır aralığı 4). **Minimal:** sıradaki etkinlik; yoksa bugünün tarihi ve "Etkinlik yok".
  - Toplantı linki tanıma: zoom.us, meet.google.com, teams.microsoft.com, teams.live.com, webex.com, facetime.apple.com (URL, konum ve notlarda).
  - Toplantıdan 5 dk önce banner (10 sn, "Katıl" düğmesiyle).
  - Reddedilen etkinlikler gösterilmez.
  - Hızlı ekleme metni "14:30 Toplantı": saat yoksa bugün için bir sonraki tam saat, başka gün için 09:00; süre 1 saat.
  - **Windows'ta yapılan (W1):** Bağlantı yokken sütun sade bir takvimdir: ay, hafta ve bugün bilgisayarın
    kendi tarihinden gelir (saati `time.windows.com` ile eşitleyen sistem saati); Mac'teki "Takvimi bağla"
    ekranı yok, etkinlikler isteğe bağlı. ICS bağlantıları Ayarlar › Widget'lar'da (Mac'teki "Erişim" satırının yerine);
    ICS'de takvim rengi olmadığı için her bağlantı Mac'in takvim renklerinden birini sırayla alır. Hızlı ekleme
    yedek yolla: `.ics` dosyası varsayılan takvim uygulamasında açılır. Mac'teki "Bugünkü hatırlatıcıları da
    göster" taşınmadı: Windows'ta okunacak bir hatırlatıcı kaynağı yok.
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
- **Kabul:** Sekmeler arasında dock'tan art arda tıklarken ada açık kalıyor. Kompakt ve Minimal'de AI'dan Home'a dönerken ada açık kalıyor. Oynatıcı Kompakt ve Minimal'de notch'un tam altında.

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
- **Windows'ta yapılan (değişiklik):** Microsoft.Data.Sqlite + FTS5 yerine `clipboard.json` ve bellekte arama.
  Geçmiş en fazla 1000 öğe olduğu için arama yine anında; yerel bir SQLite kütüphanesine ve onun Native AOT
  uyumluluğuna gerek kalmıyor. Kural Mac'tekiyle aynı (kelime başı, aksan ve büyük/küçük harf duyarsız, ı/I → i).
  "Erişim" satırı yok: Windows pano için izin istemiyor. Mac'teki ⌃⌥V ve ⌥ tıklama metinleri Ctrl+Alt+V ve
  "Alt ile tıkla" oldu; "Finder'da göster" "Klasörde göster".

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
- **Windows'ta yapılan:** Mac'teki yer imi (bookmark) yerine NTFS dosya kimliği; aynı sürücüde taşınan ya da
  adı değişen dosya bulunur, başka sürücüye taşınan bulunamaz. Dönüştür menüsünde PNG ve JPEG var; HEIC
  kodlayıcısı her Windows'ta kurulu olmadığı için yok. Mac'teki "notch'a sürükle" ve "AirDrop'la gönder"
  metinleri "pill'e sürükle" ve "paylaş" oldu.
- **Düzeltme (2026-10-30):** Başka bir paketli uygulamanın kendi klasöründen (ör. Claude masaüstü) sürüklenen dosyanın yolu
  ISLe8'den görünmüyordu; açma ve küçük resim "Erişim engellendi (UNABLE_TO_MASK_PATH)" veriyordu. Artık böyle bir dosya bırakıldığı
  anda Windows'un verdiği dosya nesnesiyle rafın klasörüne kopyalanıyor (kaldırılınca silinir). Açma `StorageFile` yerine Explorer'daki
  çift tıklama gibi kabukla. Paylaş bölgesine bırakılanlar da yollarından değil, bırakılan nesnelerden paylaşılıyor.

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
- **Windows'ta yapılan:**
  - `NotificationChanged` olayı paketli masaüstü uygulamasında çalışıyor; yukarıdaki "olay çalışmazsa" yedeğine gerek kalmadı.
  - Zamanlama seçicisi ("Her zaman / Sadece ekran açıkken / Sadece kilit ekranında") yok: Windows'ta kilit ekranı
    adası yok (§3.18), kilitliyken ada zaten görünmüyor. Kilitliyken gelenler banner olmuyor, kuyrukta da beklemiyor.
  - Odak modu Windows'ta "Rahatsız etmeyin": anahtar "Rahatsız etmeyin açıkken gösterme" oldu. Mac'teki
    "zamanlanmışları algılayamaz" notu Windows'ta geçerli değil; zamanlanmış ve odak oturumuyla açılanı da görüyor.
  - Erişim satırı Mac'teki "Tam Disk Erişimi" yerine Windows'un izni: "İzin ver…", reddedildiyse "Windows ayarları…"
    (Gizlilik › Bildirimler), zip sürümünde "Kurulu sürüm gerekiyor". Açıklama Windows'a göre yazıldı.
  - **Zip'ten kurulu sürüme geçiş (0.2.7):** zip'teki ISLe8'in Bildirimler sayfasında "Kurulu sürüme geç" kartı var. Paket
    henüz imzasız olduğundan Windows Geliştirici Modu ister; kapalıysa kart Windows Ayarları › Geliştiriciler için sayfasını
    açar. Açıksa son sürümün `-package.zip`'i güncelleme gibi indirilir, SHA-256 ile denetlenir ve
    `%LOCALAPPDATA%\LRXe8\ISLe8\package` klasörüne açılır. Zip ISLe8 kapanınca `move.ps1` paketi kaydeder, zip'in `Run`
    girdisini siler ve kurulu ISLe8'i başlatır. Ayarlar ve geçmiş aynı veri klasöründe olduğu için kalır. Hata olursa zip
    ISLe8 yeniden açılır ve nedeni gösterir.
  - Uygulamalar AppUserModelID ile tutuluyor (Mac'te bundle ID); listede Windows'un verdiği ad ve simge.
  - Tıklama gönderen uygulamanın açık penceresini öne getiriyor; çalışan bir tarayıcıyı yeniden başlatmak yeni
    pencere açacağı için önce açık pencere aranıyor. Bildirimin kendi eylemi (ör. Gmail'de o e-postayı açmak)
    başka bir uygulamadan tetiklenemiyor.

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
- **Windows'ta yapılan:**
  - Hook komutu tırnaklı tam yol ve ileri eğik çizgiyle yazılır (`"C:/Users/…/WindowsApps/isle8.exe" hook claude`): Claude Code hook'ları Windows'ta Git Bash'le de çalıştırabiliyor.
  - Ajan simgeleri Claude ve ChatGPT (`OpenAI.Codex`) masaüstü uygulamalarının logosundan; uygulama yoksa Claude için çizilmiş kıvılcım, Codex için Segoe'nin kod simgesi.
  - "Oturuma git" pencereyi `app_pid`'in ilk görünür penceresinden bulup öne getirir; tıklama adaya geldiği anda çağrıldığı için Windows izin verir.
  - Mac'te AI sekmesinin altındaki/yanındaki kullanım halkaları (§3.13) henüz yok; o gelene kadar çalışma kartları tüm genişliği kullanır.
  - Mac'te onay banner'ı kararla kendiliğinden kapanmıyor; burada karar verilince (ya da hook vazgeçince) banner kapanır.
- **Kabul:**
  - Sahte hook olaylarıyla kart doğru ("Adım 2/4", süreler, bitti satırı).
  - Gerçek Claude Code oturumunda onay adadan veriliyor.
  - ISLe8 kapalıyken hook ajanı bekletmiyor.

### 3.13 Kullanım limitleri (W4, LimitBar'dan)

- **Mac:** [Modules/Usage][mac-usage] (`UsageModule`, `UsageFetchers`, `UsageSpawn`, `UsagePolling`, `UsageWatcher`, `UsagePower`), [UsageViews.swift][mac-usageviews]. Lisans: LimitBar MIT; NOTICE'a eklenmeli.
- **Kaynaklar:**
  - **Codex:** app-server + oturum kayıtları `%USERPROFILE%\.codex\sessions`, `FileSystemWatcher` ile anında.
  - **Claude:** `claude -p /usage --output-format json --max-turns 1` + izolasyon bayrakları. Bayraklar: `--restricted --strict-mcp-config --no-session-persistence --settings {"disableAllHooks":true,"language":"English"}`. **Hook'lar kapalı**; aksi halde ISLe8'in kendi hook'ları tetiklenir.
  - **Antigravity:** `agy -p /quota`, Windows'ta `AGY_CLI_DISABLE_AUTO_UPDATE=1` ile (0.2.6). Yoksa agy her çalışmada kendini
    günceller: yeni sürümü (~70 MB) indirir, kendinin bir kopyasını yeni bir konsolda başlatır; Windows Terminal penceresi bir
    an açılıp kapanıyordu. İş bitince Job onu değiştirmeden öldürdüğü için bu her sorguda (5 dk) baştan başlıyordu. Mac'te
    Seatbelt agy'nin kendi klasörüne yazmasını engellediği için güncelleme sessizce başarısız olur; sonuç aynı.
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
  - Geniş'te AI sekmesinin altında servis başına halka; Kompakt ve Minimal'de (196 pt) çalışma kartının **sağında**, eşit satırlardan oluşan 270 pt'lik bir sütunda (pencereler tek satırda, sıfırlanma zamanı ipucunda). %70'te turuncu, %90'da kırmızı. Pencere etiketleri: "5 saat", "Haftalık".
  - %90'ı geçince 6 sn'lik banner; aynı pencere tekrar geçmedikçe bir kez.
- **Kabul:** Üç CLI kuruluyken ve girişliyken değerler Mac'tekiyle aynı; kapalı adada CLI temposu ölçümle doğrulanıyor.
- **Windows'ta yapılan (2026-10-29):**
  - **Bulma:** PATH'e körü körüne güvenilmez. Sabit kurulum yerleri (`%USERPROFILE%\.local\bin`, `%LOCALAPPDATA%\agy\bin`,
    `%APPDATA%\npm`, bun/volta/pnpm) ve kullanıcının PATH'inde yalnızca profil ya da Program Files altındaki klasörler.
    0.2.9'dan beri bunlarda yoksa masaüstü uygulamalarının kendi kopyası (en yenisi): Claude'un Code sekmesininki
    `%LOCALAPPDATA%\Claude-Data\<paket>\claude-code\<sürüm>\<yapı>\claude.exe` (önce `%APPDATA%\Claude\claude-code`),
    Codex uygulamasınınki `%LOCALAPPDATA%\OpenAI\Codex\bin\<yapı>\codex.exe`, Codex kurucusununki
    `%USERPROFILE%\.codex\packages\standalone\current\bin\codex.exe`. Kullanıcı ikisini de "Bulunamadı" görüyordu; uygulamanın
    `claude.exe`'si ISLe8'in ortamıyla oturumu görüyor (`auth status`: loggedIn). Antigravity uygulamasında agy yok.
  - **Pencere yok** (0.2.9): kullanıcı "cmd hâlâ açılıyor" dedi. CLI'lar artık `CreateProcess` ile ISLe8'in görünmeyen kendi
    masaüstünde (`WinSta0\ISLe8-cli`), askıda başlar ve Job'a girdikten sonra devam eder (önce Job'a başladıktan sonra
    giriyordu, o arada başlattıkları dışarıda kalabilirdi). Yalnızca üç boru ucu miras alınır. Deneme: yeni konsol isteyen
    `start /min` bu masaüstünde pencere açmadı, normal masaüstünde `ConsoleWindowClass` açtı. Süre dolunca Job sonlandırılır;
    bekleyen okuma boru kapanınca biter.
    npm kısayolu (`.cmd`) cmd.exe ya da node olmadan, gösterdiği yerel ikiliye çözülür: Claude Code paketindeki
    `claude.exe`, Codex'in platform paketindeki `codex.exe`.
  - **Güven:** Mac'teki sahiplik/izin denetiminin karşılığı ACL denetimi. Dosya, klasörü (DLL yerleştirilebilecek yer) ve üstündeki
    her klasör; bu kullanıcının, Yöneticiler'in, SYSTEM'in ya da TrustedInstaller'ın olmalı ve Everyone, Users, Authenticated
    Users, Interactive ya da Guests'e yazma, silme, izin/sahiplik değiştirme vermemeli. Geçmeyen çalıştırılmaz.
  - **Ortam:** izin listesi (USERPROFILE, APPDATA, LOCALAPPDATA, SystemRoot, PATHEXT …); PATH yalnızca CLI'nin klasörü ve
    System32. ISLe8'in ortamındaki hiçbir token ya da anahtar geçmez. Bu önemli çıktı: Claude masaüstü uygulamasının içinden
    başlatılan bir süreç `CLAUDE_CODE_*`/`ANTHROPIC_*` değişkenlerini taşıyor ve `claude auth status` o zaman yanlış
    sonuç veriyordu.
  - **Claude:** `/usage` kullandıkça ödenen bir hesapta 5 saatlik/haftalık satır yerine maliyet özeti ("Total cost: …")
    döndürüyor. Mac bunu "okunamadı" sayıyor; Windows'ta "Kota sınırı yok" (yeni durum `NoLimits`).
  - **Uyku:** `WM_POWERBROADCAST` (askıya alma / devam). Mac'teki Düşük Güç modu yerine Windows'un Enerji tasarrufu.
  - Codex oturum kayıtları `FileSystemWatcher` ile izlenir (Mac'te kqueue), yazmalar 2 sn'de bir okunur.
  - Önbellek `%LOCALAPPDATA%\LRXe8\ISLe8\usage\state.json`. LimitBar'ın MIT lisansı pakette `Assets\LimitBar-LICENSE.txt`.

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
- **Windows'ta yapılan:**
  - **Kendi alıcısıyla bağlanan cihazlar (Mac'te yok, Windows'ta asıl durum):** oyun kulaklıkları ve fareleri çoğunlukla
    Bluetooth değil 2.4 GHz alıcıyla bağlanır ve pilini Windows'a bildirmez. Üreticinin kendi HID protokolüyle, yalnızca
    soru sorarak okunur (hiçbir ayar gönderilmez):
    - **Razer** (1532): ilk arayüzdeki 90 baytlık feature report; komut sınıfı 0x07, 0x80 pil (0–255), 0x84 şarj.
      İşlem kimliği 0x1F, 0x3F, 0xFF sırayla denenir. Fare/klavye arayüzü erişimsiz açılır; feature report yine geçer.
      Uyuyan fare ya da kablolu cihaz yanıt vermez. Razer Viper V3 Pro (1532:00C1) ile doğrulandı.
    - **SteelSeries Arctis Nova Pro Omni** (1038:2290): 0xFFC0 satıcı koleksiyonunda rapor kimliği + 0xB0 durum sorusu;
      yanıtta [6] kulaklık %, [7] baz istasyonundaki yedek pil %, [15] durum (0x01 kapalı, 0x02 kabloyla şarj, 0x08 bağlı).
      Yedek pil kartta ikinci halka ("Yedek pil"). Nova 7 gibi diğer SteelSeries modelleri başka komutlar kullanıyor;
      elde cihaz olmadan eklenmedi.
    - **SteelSeries Arctis 7+ ailesi** (1038:220E 7+, 2212 7P+, 2216 7X+, 2236 7+ Destiny; 0.2.7): aynı 0xB0 sorusu,
      0xFFC0 / kullanım 0x01 koleksiyonunda, rapor kimliği 0. Yanıtta (Windows'un önüne koyduğu rapor kimliğiyle sayınca)
      [1] 0xB0 yankısı, [2] 0x01 ise kulaklık kapalı, [3] pil 0–4 (her adım %25), [4] 0x01 ise şarj. HeadsetControl'ün
      (Sapd/HeadsetControl, `steelseries_arctis_7_plus`) okuduğu gibi; elde cihaz yok, bir kullanıcının 7P+'sı için eklendi.
    - **ASUS ROG, ROG Omni Receiver** (0B05:1ACE; 0.2.5): alıcının arayüz 2'sinde üç satıcı koleksiyonu, hepsi 64 baytlık
      raporlar. col01 (rapor 1, 0xFF02) `A0`: eşleşmiş cihazlar, 5. bayttan 4'er baytlık yuvalarda küçük-endian PID. Klavye
      col02'de (rapor 2, 0xFF00) `12 01`: [6] pil %, [9] 1 ise şarj; fare col03'te (rapor 3, 0xFF01) `12 07`: [5] pil %,
      [10] şarj. %0 ve şarjda değil = uyuyor, kart yok. PID'ler ve komutlar G-Helper'dan (seerge/g-helper, GPL-3.0; yalnızca
      protokol bilgisi, kod alınmadı). Armoury Crate aynı koleksiyonla konuşabildiği için başka raporlar atlanır, kaybolan soru bir
      kez daha sorulur. ROG Azoth (alıcıda 0x1A85) ile doğrulandı: %47, şarj oluyor. Alıcısız (kablolu ya da Azoth'un kendi
      donanım kilidiyle) ASUS cihazları ve Bluetooth'taki Azoth bu yoldan okunmaz; Bluetooth'ta Windows'un pil özelliği okunur.
    - Alıcı takılıp çıkarılınca HID arayüzü izleyicisi (`DeviceWatcher`, VendorId 0x1532/0x1038, 0x0B05:0x1ACE) tetikler; kulaklık ya da
      fare açılıp kapanınca olay yok, 10 dk'lık okuma ve ada açılışı bunu yakalar.
  - Mac'in boş durum metni ("AirPods, Magic Mouse ya da …") Windows'ta "Kablosuz kulaklık, fare, klavye ya da …" oldu.
  - Yüzde önce Windows'un kendi tuttuğu PnP özelliğinden okunuyor (BLE cihazlar için de); GATT okuması yalnızca o yoksa.
    Böylece sayılar Windows Ayarları'ndakiyle aynı ve HID cihazlarına ayrıca bağlanılmıyor.
  - AirPods'un ayrı kulaklık/kutu yüzdeleri yalnızca sekme açıkken geliyor (yukarıdaki kural). Sekme kapalıyken ve
    bağlanma banner'ında AirPods tek yüzdeyle (Windows'un HFP'den aldığı) görünüyor; Mac'te banner'da kulaklıklar ve kutu var.
    Sekme son 10 dk'da açıldıysa son kulaklık okuması kullanılıyor. Yakındaki başka birinin AirPods'u, bağlı cihazın ürün
    kimliğiyle ve en güçlü sinyalle ayıklanıyor.
  - "Bu Mac" kartı "Bu bilgisayar"; masaüstünde (pil yoksa) kart yok. Mac'teki "Şarj oluyor · 65 W" adaptör gücü
    Windows'ta okunamadığı için yalnızca "Şarj oluyor".
  - Mac'te Kompakt/Minimal düzende (196) iki satır kart sığmıyor; Windows'ta satırlar yüksekliği paylaşıyor, halkalar küçülüyor.
  - Simgeler Segoe Fluent'ten; AirPods kulaklıkları ve kutusu için Segoe'de simge olmadığından birkaç şekilden çiziliyor.
  - Ayarlar › Sistem'de şimdilik yalnızca "Kablosuz cihazlar" bölümü var; "iPhone ve iPad pilleri" anahtarı ve okuması
    (aşağıda) daha yapılmadı.

- **Dock simgesi:** AirPods değil, Mac + iPhone (`macbook.and.iphone`).
- **iPhone ve iPad pilleri (W5, varsayılan kapalı):** Bu bilgisayara güvenen cihazlar, Finder'ın (Windows'ta Apple Aygıtları / iTunes) onlara ulaştığı yolla salt okunur okunur. Mac: Apple'ın `MobileDevice` çerçevesi çalışma anında yüklenir, bağlanma/kopma bildirimleri, lockdown oturumu, `com.apple.mobile.battery` (`BatteryCurrentCapacity`, `BatteryIsCharging`). Windows: "Apple Mobile Device Service" (usbmuxd) `127.0.0.1:27015`'te dinler; `ReadPairRecord` + lockdown TLS oturumu (bkz. [araştırma][mac-batteries-doc]). Kart: cihaz simgeli halka, şarjdayken ⚡ ve "Şarj oluyor". %15 uyarısı var, "bağlandı" banner'ı yok (Wi-Fi'de cihaz uyudukça gelip gider). Mac: [AppleDeviceSource.swift][mac-appledevices].

### 3.15 Temalar ve düzenleyici (W5)

- **Mac:** [Theme.swift][mac-theme], [ThemeSettings.swift][mac-themesettings], [IslandView.swift][mac-islandview] (`IslandBackground`)
- **Davranış:**
  - Ada kapalıyken her temada siyah kalır, notch/pill ile kaynaşır.
  - Renkli temalarda üstteki başlık bandı siyahtan temaya geçer.
  - Kartlar tema yüzeyi, kenarlığı ve köşe yarıçapıyla çizilir.
  - Vurgu rengi her yerde (seçili sekme, bugün, halkalar, düğmeler).
  - Yazı tipi ailesi temadan gelir.
  - **Galeri:** her tema için küçük önizleme. **Düzenleyici:** malzeme, renkler, köşe, yazı tipi; değişiklik seçili temaya yazılır (kopya yok). JSON olarak dışa/içe aktarma; içe aktarılan tema bir temanın kimliğiyle çakışırsa yeni kimlik alır.
  - **Cam:** Mac'te Apple'ın şeffaf (`.clear`) camı + hafif karartma; ortası (yazıların olduğu yer) okunurluk için daha koyu, alt kenarda ince bir ışık çizgisi, notch satırı siyah. Kartlar camda koyu yarı saydam. **Kapak rengi** siyah zeminin üstüne çizilir, arkası görünmez.
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
  - Tray: `Shell_NotifyIcon` ile Ayarlar ve Çıkış. **Tray simgesi Ayarlar > Genel'den gizlenebilir**; o zaman ayarlar adadaki ⚙ düğmesiyle ya da uygulama yeniden açılınca açılır.
  - Başlangıçta açılma: MSIX `StartupTask` (`ISLe8Startup`), paketsizse `HKCU\…\Run`. Kullanıcı görevi Windows'un
    Başlangıç listesinden kapattıysa ISLe8 onu açamıyor; anahtar o zaman Windows Ayarları › Uygulamalar › Başlangıç'ı açıyor.
  - Uygulama yeniden açılınca (ikinci örnek) Ayarlar açılır.
  - **Varsayılana döndür (Windows, 0.2.7):** açık ada genişliği, animasyon hızı, açılma/kapanma gecikmesi ve bildirim
    süresi kaydırıcılarının yanında; değer zaten varsayılansa soluk. Mac'te bu kaydırıcılarda yok (yalnızca temalarda).
- **Ayarlar (Mac ile aynı bölümler):** Genel, Görünüm, Düzen, Kısayollar, Medya, Raf, Pano, Bildirimler, Sistem, Widget'lar, Ajanlar. "Kilit Ekranı" yok.
- **Genel bölümü:**
  - Açılma: üzerine gelince / tıklayınca; açılma ve kapanma gecikmesi.
  - Ekran: ana / etkin / adla. Mac'teki "otomatik" (notch'lu ekran, yoksa ana ekran) Windows'ta ana
    ekranla aynı olacağı için yok; Mac'in ana ekran açıklaması da ("Menü çubuğunun olduğu ekran")
    Windows'a göre yazıldı.
  - Dil: Sistem / Türkçe / English; değişince yeniden başlatır.
  - Ayarları JSON olarak dışa/içe aktarma.
- **Kabul:** Şu an JSON'dan değişen her ayar pencereden değişebiliyor.

### 3.19 Notlar (plana eklenmeli: W2)

- **Mac:** [NotesModule.swift][mac-notes], [NotesViews.swift][mac-notesviews]
- **Davranış:**
  - Dock'ta Notlar sekmesi. Üstte yazma satırı (Enter ekler, Esc vazgeçer, en fazla 3 satır görünür); altında notlar kart olarak (genişliğe göre 2–4 sütun).
  - Karta tıklamak kopyalar ("Kopyalandı" 1,2 sn); üzerine gelince düzenle ve sil; sağ tık menüsünde de aynıları. Boşaltılan not silinir.
  - Kayıt: tek JSON dosyası, yalnızca kullanıcı okuyabilir (Mac'te 0600). Arka planda iş yok.
  - Yazma satırına tıklamak adaya klavye verir; ada kapanınca klavye önceki uygulamaya döner. Windows: ada penceresi `WS_EX_NOACTIVATE`; satıra tıklayınca etkinleştir, kapanınca önceki pencereye geri ver.
- **Kabul:** Not ekleme, kopyalama, düzenleme, silme; uygulama yeniden açılınca notlar duruyor.
- **Windows'ta yapılan:** `%LOCALAPPDATA%\LRXe8\ISLe8\notes.json`; klasörün ACL'i zaten yalnızca kullanıcıya (ve
  SYSTEM/Yöneticiler'e) açık, dosyaya ayrıca izin verilmiyor. İngilizce metinlerde Mac'in "Return"ü "Enter" oldu.
- **Windows'ta farklı (0.2.7, bir kullanıcının isteğiyle):**
  - **Shift+Enter** yeni satır açar; Enter yine ekler. Kutunun `\r`'leri kayıtta `\n` olur.
  - **Yazarken ada açık kalır:** satıra tıklayınca (adaya klavye verilince) imlecin çıkması adayı kapatmaz; başka yere
    tıklamak kapatır; satır odağı bırakınca imleç dışarıdaysa ada kapanır. Mac'te imleç çıkınca kapanıyor;
    Windows'ta yazılan bir not böyle kaybolmuştu. Takvimin hızlı ekleme satırı ve pano araması da aynı kuralı izler.

### 3.20 Sistem sekmesi ve LAN (plana eklenmeli: W3)

- **Mac:** [SystemStats.swift][mac-stats], [SystemStatsViews.swift][mac-statsviews], [SystemModule.swift][mac-system] `NetworkLink`
- **Sistem sekmesi (Etkinlik Monitörü kısaca):** dört gösterge kartı (işlemci, bellek, disk, ağ) ve en çok işlemci kullanan 5 süreç (yüzde + bellek). ↗ düğmesi Etkinlik Monitörü'nü (Windows'ta Görev Yöneticisi) açar.
  - Mac kaynakları ve Windows karşılıkları: işlemci `host_statistics` → `GetSystemTimes`; bellek "kullanılan" (uygulama + kablolu + sıkıştırılmış) → `GlobalMemoryStatusEx`; disk → `GetDiskFreeSpaceEx`; ağ trafiği (Ethernet + Wi-Fi bayt sayaçları) → `GetIfTable2`; süreçler (yalnızca kullanıcının kendi süreçleri) → `NtQuerySystemInformation` ya da süreç başına `GetProcessTimes` + `PROCESS_MEMORY_COUNTERS`.
  - **Yalnızca sekme ekrandayken, iki saniyede bir** ölçülür; örnekleme sekmenin yaşam süresine bağlı. Mac'te `onAppear`/`onDisappear` sayacı bir kez çift sayıp örneklemeyi açık bıraktı; `.task` ile çözüldü. Windows'ta da görünür olmaya bağlanmalı, sayaçla değil.
  - Değerler animasyonsuz değişir: her güncellemedeki halka/sayı animasyonları sekmeyi saniyede onlarca kez çizdirip CPU'yu %7'ye çıkarıyordu (animasyonsuz %1,2).
  - Göstergeler kullanım arttıkça yeşil → turuncu (%60) → kırmızı (%85).
- **LAN:** Bağlantı türü olay tabanlı izlenir (Mac `NWPathMonitor`; Windows `NetworkInformation.NetworkStatusChanged` + `GetInternetConnectionProfile().NetworkAdapter.IanaInterfaceType == 6`). Ethernet'teyken başlıkta yeşil **LAN** çipi; dar ekranda düşmez. Kablo takılınca/çıkarılınca §2'deki durum kulakları. Uygulama açılırken zaten kabloluysa bildirim yok. Ayar: Görünüm > Başlık çubuğu > "Kablolu bağlantı (LAN)".
- **Kabul:** Kabloyu takıp çıkarınca kulaklar ve çip doğru; sekme kapalıyken örnekleme yok (ölçümle).
- **Windows'ta yapılan:** Sistem sekmesi. Süreç belleği çalışma kümesi (Mac'te phys_footprint); "Etkinlik
  Monitörü'nü aç" yerine "Görev Yöneticisi'ni aç".
- **Windows'ta yapılan (LAN, 2026-10-29):** `NetworkInformation.NetworkStatusChanged`, art arda gelen olaylar bir okumada.
  İnternet bağlantısının bağdaştırıcısı 6 (Ethernet) ise yeşil "LAN" çipi; kablo takılınca yeşil "LAN", çıkınca beyaz
  "Wi-Fi" ya da "Çevrimdışı" kulakları (bağlantı otururken kulak güncellenir). Açılışta zaten kabloluysa bir şey çıkmaz.

### 3.21 Klavye temizliği (plana eklenmeli: W3)

- **Mac:** [KeyboardLock.swift][mac-keyboardlock], [KeyboardLockWindow.swift][mac-keyboardwindow]
- **Davranış:** Ayarlar > Sistem'de "Klavyeyi kilitle" ve palette "Klavyeyi temizle". Bütün tuşlar, değiştiriciler ve medya tuşları iki dakikalığına yutulur; fare çalışır. Ekranın ortasında "Klavye kilitli" kartı, geri sayım ve "Kilidi aç" düğmesi. İki dakika sonra kendiliğinden açılır; süreç ölürse kilit de kalkar.
- **Windows:** `SetWindowsHookEx(WH_KEYBOARD_LL)` ile her olaya 1 döndür (Ctrl+Alt+Del yine çalışır, bu iyi). Mac'te Erişilebilirlik izni gerekiyor; Windows'ta özel izin yok.
- **Kabul:** Kilitliyken hiçbir tuş yazmıyor; düğme ve süre kilidi açıyor.

### 3.22 Özel günler (plana eklenmeli: W5)

- **Mac:** [Seasonal.swift][mac-seasonal] (tarihler, renkler), [SeasonalDecor.swift][mac-seasonaldecor] (sahneler)
- **Günler (yalnızca dünyada kutlananlar):**

  | Gün | Tarih | Renk (üst → alt, vurgu) | Sahne |
  |---|---|---|---|
  | Sevgililer Günü | 13–14 Şubat | #2B0710 → #4D0A1F, #FF2D55 | Kenarlarda atan kalpler, yükselen kalpler, ışıltı |
  | Aziz Patrick Günü | 16–17 Mart | #04170B → #0B3D1C, #22C55E | Sallanan yoncalar, yeşil şapkalar, paralar, dönerek düşen yoncalar |
  | Paskalya | Paskalya cuması – pazarı (hesapla) | #15150A → #3B3410, #F59E0B | Zıplayan tavşan, yumurtalar, civciv, çiçek sırası, çayır ışığı, kelebekler |
  | Cadılar Bayramı | 24–31 Ekim | #1A0A03 → #3F1505, #F97316 | Ay ve şato silueti, titreyen kabaklar, yarasalar, kıvılcımlar, örümcek ağı |
  | Ay Yeni Yılı | Çin takvimiyle bulunur; bir gün öncesi – iki gün sonrası | #2A0303 → #5C0A07, #EF4444 | Sallanan fenerler, ejderhalar, kırmızı zarflar, altın kıvılcımlar |
  | Kış tatili | 20–30 Aralık, 2–6 Ocak | #06142B → #0E2C55, #3B82F6 | Kar, çamlar, kardan adam, ışıklı köy, ışık zinciri |
  | Yılbaşı | 31 Aralık – 1 Ocak | #0E0820 → #2E1065, #FBBF24 | Konfeti, havai fişek halkaları, şampanya, balonlar, parlayan yıl yazısı |

- **Davranış:** Gün sürerken açık ada günün temasına bürünür (degrade, vurgu, kenarda nefes alan parlak çizgi); köşe yuvarlaklığı ve yazı tipi kullanıcınınki. Sahne içeriğin arkasında; sabit parçalar kenarlarda ve dock'un yanındaki köşelerde, hareketliler soluk. Ayarlar > Görünüm > Özel günler: aç/kapat ve herhangi bir günü önizle. Hareketi azalt açıkken sahne durur.
- **Performans dersi:** Mac'te sahne önce bir Canvas'ta 20–24 fps çiziliyordu ve ada açıkken %6–7 CPU harcıyordu. Şimdi her parça bir Core Animation katmanı; hareket bir kez kurulup render sunucusunca oynatılıyor, kare başına 0 CPU. **Windows: aynısı Composition katmanında** (`Visual` + `KeyFrameAnimation`/`ExpressionAnimation`, DWM'de oynar); emoji görselleri (Segoe UI Emoji) bir kez `CompositionSurface`'a çizilir. XAML `Storyboard` ya da kare başına yeniden çizim kullanılmamalı.
- **Kabul:** Yedi gün önizlemede doğru; ada açıkken CPU kare başına artmıyor.

### 3.18 Taşınmayacaklar

| Mac | Neden |
|---|---|
| Kilit ekranı görünümü | Windows kilit ekranına üçüncü taraf pencere giremiyor |
| AirDrop alma | Karşılığı yok (Nearby Share, İndirilenler; §3.10) |
| Sistem ses göstergesinin yerine geçme | Windows'un kendi göstergesini gizlemenin desteklenen bir yolu yok; ISLe8 yanında gösterir |
| Kestirmeler (Shortcuts) | Windows'ta karşılığı yok; palet yalnızca uygulamaları listeler |
| Spotify Canvas videoları | Resmî API'de yok; Spotify'ın web oynatıcısındaki erişim korumasını aşmak gerekiyor. Kullanım koşullarına aykırı, iki sürümde de yapılmaz |
| Görünüm › Harici ekranlarda boyut | Mac'te notch'lu ve notch'suz ekranları ayırır. Windows'ta ada DIP cinsinden çizilir, DPI ile her ekranda aynı fiziksel boyuttadır; ayrıca seçilecek bir ölçek yok |

---

### Windows'a özgü: güncellemeler (2026-10-30)

Mac sürümünde güncelleyici yok. Windows'ta kullanıcı isteğiyle: **Ayarlar › Genel › Güncellemeler** ve bildirim alanı
menüsü. Günde bir kez (açılıştan 30 sn sonra, sonra saatte bir "vakti geldi mi" bakılarak) `api.github.com/repos/Mericsdc/ISLe8-Windows/releases/latest`
sorulur; taslak ve ön sürümler sayılmaz. İndirme yalnızca düğmeyle, yalnızca projenin kendi Release adreslerinden; GitHub'ın
verdiği SHA-256 denetlenir. Sonra küçük bir PowerShell betiği ISLe8'in kapanmasını bekler, eski klasörü `-old` adıyla
kenara alır, yenisini koyar, kurulu ISLe8'de paketi aynı klasörden yeniden kaydeder (yerinde güncelleme: ayarlar, bildirim izni
ve başlangıç görevi kalır) ve ISLe8'i açar. Bir şey ters giderse eski klasör geri konur, neden `update.log`'a yazılır ve ISLe8
açılınca ayarlarda gösterilir. Kurulu ISLe8'in alt süreçleri paket kimliğini taşır ve paket yeniden kaydedilirken Windows onları da kapatır; 0.2.1'de betik
dosyaları yerine koyduktan sonra böyle yarıda öldü (`PROC_THREAD_ATTRIBUTE_DESKTOP_APP_POLICY` ile "paket dışı" başlatma kimliği
kaldırmadı). 0.2.2'den beri kurulu ISLe8'de betiği Görev Zamanlayıcı başlatır (tek seferlik "ISLe8 update" görevi, kullanıcı
olarak, sınırlı yetkiyle; betik görevi siler). Betik her adımı `updates\apply.log`'a yazar. Kurulu ISLe8 için
Release'te `ISLe8-<sürüm>-<rid>-package.zip` (scripts/package.ps1). İmzalı .msix ve App Installer W6'da.

**0.2.8'de bulunan iki hata (ikisinde de ISLe8 güncellemede "kendini kapatıyordu"):**
- Ayarlar'daki Güncelle düğmesi, güncelleme varken `AccentButtonStyle`'ı `Application.Current.Resources`'tan alıp
  `Style`'a çeviriyordu. Native AOT'ta XAML'in yarattığı bir nesne `object` olarak dönünce çıplak bir COM sarmalayıcısı
  oluyor ve dönüşüm `InvalidCastException` (E_NOINTERFACE) atıyor. Bu, yayınlanan sürümlerde Ayarlar açılınca çökme demek
  (0xc000027b, CoreMessagingXP); AOT'suz yerel derlemede görünmüyordu. Düğme artık sistemin vurgu rengiyle elle boyanıyor
  (`SettingsUi.Accent`); kaynak sözlüğünden nesne alınmıyor.
- WinUI 3'te `DispatcherQueue.TryEnqueue` geri çağrısında, `DispatcherQueueTimer.Tick`'te ya da bir `async void`'un await
  sonrasında atılan hata `Application.UnhandledException`'a hiç uğramıyor, süreci kapatıyor. Hepsi artık `UI/Guarded.cs`
  üzerinden geçiyor (`Post`, `OnTick`, `GuardedSynchronizationContext`); hata günlüğe yazılıyor, ada açık kalıyor.
- Zip güncellemesi hiç tamamlanamıyordu: Gezgin ISLe8'i kendi klasöründe çalışır başlatıyor, betiği başlatan PowerShell bu
  çalışma klasörünü miras alıyordu, betik de o klasörü yeniden adlandıramıyordu ("in use"). Eski sürüm geri açılıyordu.
  Betikler artık `updates` klasöründe çalışıyor; zip güncellemesini ise artık betik değil yeni ISLe8 bitiriyor (aşağıda).
- **Microsoft Defender:** güncelleyicinin indirip açtığı 0.2.7 `ISLe8.exe`'yi "Trojan:Win32/Bearfoos.A!ml" diye karantinaya
  aldı (makine öğrenmesi tahmini, yanlış alarm). Güncelleme ISLe8'i kapatıyor, yeni exe silindiği için hiçbir şey açılmıyordu.
  Sonra bulut kararı dosyanın özetine bağlandı: yayınlanan 0.2.7 exe'si artık kendi başına da işaretli. 0.2.6 ve yerel
  derlemeler temiz. İmzasız bir programın exe indirip gizli bir PowerShell'le (`-ExecutionPolicy Bypass -WindowStyle Hidden`)
  yerine koyması tipik bir zararlı davranışına benziyor. 0.2.8'den beri zip güncellemesinde PowerShell yok: indirilen yeni
  `ISLe8.exe` `--finish-update <plan>` ile başlıyor (`Shell/UpdateFinisher.cs`), eskisinin kapanmasını bekliyor, eski klasörü
  kenara alıp yeni dosyaları kopyalıyor, ISLe8'i açıyor; hata olursa eskisini geri koyuyor. İndirilen dosyalar 30 sn sonra
  siliniyor. Kurulu (paketli) sürüm `Add-AppxPackage` için hâlâ betiği kullanıyor. Kalıcı çözüm imzalı yayın (ör. Azure
  Trusted Signing, açık kaynak için SignPath) ve dosyaların Microsoft'a yanlış alarm olarak bildirilmesi.
- 0.2.7 ve öncesi zip sürümleri Gezgin'den açıldıysa kendini 0.2.8'e güncelleyemez (Ayarlar'dan denenirse çöker,
  bildirim alanı menüsünden denenirse betik klasörü yeniden adlandıramaz). Yeni zip elle indirilip eski klasörün yerine
  konmalı; ayarlar veri klasöründe olduğu için kalır.

### Windows'a özgü: oyunlar, performans göstergesi, kapalı ada (0.2.7, 2026-10-30)

Bir kullanıcının geri bildirimiyle eklendi; Mac'te karşılıkları yok.

- **Oyun öndeyken:**
  - Öndeki pencerenin programı, Windows'un oyun saydığı programlardansa (Game Bar'ın
    `HKCU\System\GameConfigStore\Children\*\MatchedExeFullPath` listesi) ya da Windows bir Direct3D programının ekranı tek
    başına tuttuğunu söylüyorsa (`SHQueryUserNotificationState` = `QUNS_RUNNING_D3D_FULL_SCREEN`), oyun öndedir.
  - Öndeki pencerenin değişimi `EVENT_SYSTEM_FOREGROUND` ile gelir; sorgu yok. Listeye yeni yazılan bir oyun 4 sn sonra
    bir kez daha aranır.
  - Oyun öndeyken adada hiçbir şey hareket etmez: müzik ve ajan çubukları, nabız ve kayan başlık durur. Ölçüm: bu döngüler
    ISLe8'in kompozisyon iş parçacığında (`dwmcorei.dll`) ekranın yenileme hızında kare çizdiriyor, tek çekirdeğin ~%12'si.
  - **Genel › Oyun oynarken gizle** (varsayılan açık): ada, oyunun ekranında tam ekrandaki gibi gizlenir ve uyarılar bekletilmez.
- **Performans göstergesi** (Genel; Kapalı / Oyun oynarken / Her zaman): geniş pill'de FPS, CPU ve GPU kullanımı ve sıcaklıkları.
  - Açıkken ada oyunun üstünde kalır; değerler saniyede bir, yalnızca gösterge görünürken alınır.
  - **Gösterilecekler** (0.2.9): FPS, CPU, GPU, Sıcaklık, RAM, Saat düğmeleri; varsayılan ilk dördü. Pill seçilenlerin en geniş
    hâline göre genişler (`PerformanceMath.Widest` ölçülür, değerler değişince oynamaz), boş pill'den dar olmaz. Saat en başta,
    kullanıcının kısa saat biçiminde. RAM: `GlobalMemoryStatusEx`, Görev Yöneticisi'nin kullanılan payı.
  - Oyun öndeyken pill'in üzerine gelmek ya da tıklamak adayı açmaz, büyütmez (0.2.9, kullanıcı: "oyundayken açılmamalı").
  - **CPU:** `GetSystemTimes`.
  - **GPU:** `GPU Engine(*engtype_3D)\Utilization Percentage` (PDH, İngilizce adıyla). Her bağdaştırıcının süreçleri
    toplanır; Görev Yöneticisi gibi en meşgul bağdaştırıcı gösterilir.
  - **GPU sıcaklığı:** o bağdaştırıcının sürücüsünden (`D3DKMTQueryAdapterInfo`, `KMTQAITYPE_ADAPTERPERFDATA`).
    RTX 4080 Laptop'ta 72,6°C ölçüldü, nvidia-smi ile aynı.
  - **CPU sıcaklığı:** Windows'un kendi sensörü yok. LibreHardwareMonitor ya da OpenHardwareMonitor açıksa onların WMI
    sınıfından okunur, yoksa dizüstünün entegre GPU'sundan (`HybridIntegrated`; işlemciyle aynı yonga, 83°C).
  - **FPS:** ISLe8'in kendi ETW oturumu (`ISLe8 frames`). Yalnızca DXGI `Present_Start` (42) ve D3D9 `Present_Start` (1)
    olayları, öndeki oyunun süreci için sayılır. Oturumu yalnızca bir yönetici ya da "Performance Log Users" üyesi
    açabilir; değilse FPS gösterilmez. Doğrulama: bir Direct3D programında 370–530 FPS.
  - 0.2.9: oyun yoksa öndeki programın kareleri sayılır (pencerede, Game Bar'ın tanımadığı oyunlar da), yalnızca çiziyorsa
    gösterilir. ETW'ye izin yoksa ve RivaTuner Statistics Server açıksa onun paylaşılan belleğinden (`RTSSSharedMemoryV2`,
    yönetici gerekmez; eşlenen bölgenin dışı okunmaz). İzin yoksa Ayarlar'da grubun Windows dilindeki adıyla
    `net localgroup "…" "…" /add` komutu ve Kopyala düğmesi; ISLe8 kendisi yükseltilmiş komut çalıştırmaz (Defender'ın
    0.2.7'deki yanlış alarmından sonra bu kalıptan kaçınıldı).
- **Ekran görüntüsü** (0.2.9, kullanıcı: "ss moduna geçince kendini direkt görünmez yapıyor"): Win+Shift+S'nin ve ekran
  görüntüsü araçlarının (Ekran Alıntısı Aracı, ShareX, Lightshot, Greenshot, Snagit, Game Bar, PowerToys …) ekranı kaplayan
  penceresi tam ekran uygulama ya da oyunun gidişi sayılmaz; ada görüntüde kalır (`CaptureTools`). Kabuk bildirimi aracın
  penceresi odak almadan gelebildiği için ekranı kaplayan en üstteki pencereye de bakılır.
- **Kapalı ada** (Görünüm › Ada; Siyah / Temaya uysun / Saydam):
  - Mac'te kapalı ada çentikle bir olsun diye hep siyah. "Temaya uysun" açık adanın rengini, gradyanını ve camını kullanır.
  - "Saydam" siyahın %30'unu bırakır; beyaz içerik okunur, görev çubuğu ya da masaüstü arkadan görünür.
- **Boştayken ince çubuk** (Görünüm › Ada): boş pill 140×6 bir çubuk olarak çizilir. Üstte pill'in üst kenarında, görev
  çubuğunda ortasında durur. İmleç pill'in tamamında algılanır; saat çubukta gösterilmez.
- **Banner'larda boş pill satırı yok:** Mac'te banner'ın üst satırı çentiğin altında boş kalıyor. Windows'ta bu satır siyah
  bir bant oluyordu; görev çubuğunda en alta düştüğünden müzik bildiriminin altında fazla boşluk görünüyordu. Artık banner,
  içeriği ve üstte/altta 5'er DIP'ten ibaret (`IslandSizes.BannerInset`).
- **FluentFlyout** (Sistem › FluentFlyout açıkken ona bırak, varsayılan açık): FluentFlyout ses ve medya tuşlarında kendi
  penceresini gösteriyor. FluentFlyout.exe çalışırken ada ses göstergesini ve yeni şarkı bildirimini göstermez.
- **Ajanlar › Kullanım limitleri:** her CLI'ın kartı bulunup bulunmadığını gösterir: yolu; "bulunamadı" (yalnızca masaüstü
  uygulaması yetmez, 0.2.7; 0.2.9'dan beri Claude ve Codex uygulamalarının kendi kopyası da bulunur, §3.13); ya da başka
  kullanıcıların yazabildiği bir klasörde olduğu için çalıştırılmadığını. Adada yalnızca bulunanların limitleri vardı; neden
  diğerlerinin görünmediği söylenmiyordu.

## 4. DESIGN.md §4 ile farklar

- **Plana eklenmesi gerekenler:** ses/parlaklık göstergesi (§3.2, yapıldı), kamera/mikrofon göstergesi ve uyanık tut (§3.4), komut paleti (§3.16), ayarlar penceresi (§3.17), takvim hızlı ekleme (§3.6), `isle8.exe` konsol yardımcısı (§1), Notlar (§3.19), Sistem sekmesi ve LAN (§3.20, yapıldı), klavye temizliği (§3.21), özel günler (§3.22), iPhone/iPad pilleri (§3.14), durum kulakları (§2, yapıldı).
- **Öne alınan:** MSIX paket kimliği. Bildirim aynası, App Execution Alias ve `StartupTask` paket kimliği istiyor; takvim yalnızca ek kaynağı (AppointmentStore) için istiyor, asıl yolu ICS (§3.6). **Karar (2026-10-29): tam MSIX**, dosya ve kayıt defteri sanallaştırması kapalı (ayarlar zip sürümüyle aynı klasörde). Yerel kurulum `scripts/install.ps1` (Geliştirici Modu, imzasız, klasöründen kayıt); imzalı .msix ve Releases'e konması W6'da. Zip sürümü bildirim aynası olmadan sürüyor.
- **Varsayılan değişiklikleri:** Tasarruf modu varsayılan açık. Açılma gecikmesi 0. Bütün sekmeler aynı yükseklikte.

## 5. Önerilen aşama sırası

| Aşama | İçerik |
|---|---|
| W1 · Temel + Home | Paket kimliği kararı, ayarlar penceresi, tray, §2'deki ada kuralları (sabit çerçeve, durum kulakları), dock, Geniş/Kompakt/Minimal şeritleri, Home sütunları (takvim, medya + ses kaydırıcısı + varsayılan çalar + Spotify sesi, hava, zamanlayıcı), pil + W, medya düzeltmeleri |
| W2 · Pano + Raf + Palet + Notlar | Pano geçmişi, raf ve paylaşım, Ctrl+Alt+K paleti, Notlar sekmesi |
| W3 · Bildirim + Sistem | Bildirim aynası, ses/parlaklık göstergesi (yüzde, yalnızca tuşla parlaklık), kamera/mikrofon, uyanık tut, Sistem sekmesi, LAN çipi ve kulakları, klavye temizliği |
| W4 · AI | `isle8.exe` + named pipe, hook kurucu, çalışma kartı, onaylar, kullanım limitleri |
| W5 · Cihazlar + Tema | Bluetooth pilleri ve Cihazlar sekmesi, iPhone/iPad pilleri (varsayılan kapalı), Mica/Acrylic, tema düzenleyici ve JSON, özel günler |
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
[mac-shape]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Views/IslandShape.swift
[mac-notes]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/Notes/NotesModule.swift
[mac-notesviews]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/Notes/NotesViews.swift
[mac-stats]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/System/SystemStats.swift
[mac-statsviews]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/System/SystemStatsViews.swift
[mac-keyboardlock]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/System/KeyboardLock.swift
[mac-keyboardwindow]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Window/KeyboardLockWindow.swift
[mac-seasonal]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Core/Seasonal.swift
[mac-seasonaldecor]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Views/SeasonalDecor.swift
[mac-appledevices]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/ISLe8/Modules/Devices/AppleDeviceSource.swift
[mac-batteries-doc]: https://github.com/Mericsdc/ISLe8-macOS/blob/main/docs/apple-device-batteries.md
