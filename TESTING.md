# Test listesi

Her maddeyi işaretle; olmayan varsa günlükle birlikte not et (README › Günlük ve ayarlar). W1'in
adımları en üstte, W0'ın listesi altta.

## W1 · Adım 3: açık ada iskeleti, dock ve düzenler (2026-10-27)

Debug (JIT). Boyutlar, sekme sırası ve gizleme, sekme hafızası, kaydırma ve küçülürken açık kalma Core
testlerinde (`OpenIslandTests`; 123'ten 149'a). Tıklamalar adanın kendi dock'una otomasyonla yapıldı.

**Geniş** (varsayılan)
- [x] Açık ada 820×396 DIP (%150'de 1230 px geniş); sağ üstte sabitle ve ayarlar, altta 5 sekmeli dock.
- [x] Köşe yarıçapı temaya göre (LRX Mono'da 18).
- [x] Dock'tan Raf'a tıklayınca içerik değişiyor, ada aynı boyutta kalıyor; seçili sekme vurgu renginde.
- [x] Raf, Pano, AI, Cihazlar "Windows'a yakında geliyor" diyor; simgeler doğru.
- [x] Beş sekme arasında art arda tıklarken ada açık kalıyor ve boyutu değişmiyor (canlı).
- [x] Başlıktaki sabitle düğmesi adayı açık tutuyor, simgesi dolu iğneye dönüyor; yeniden basınca kalkıyor (canlı).
- [x] Başlıktaki ayarlar düğmesi Ayarlar'ı açıp adayı kapatıyor (canlı).
- [ ] Ada kapanıp 15 sn içinde yeniden açılınca son sekmede, daha sonra ilk sekmede açılıyor.

**Minimal** (`"homeStyle": "minimal"` ile)
- [x] Home ince bir şerit: 600×156; kapak, başlık, "Kanal · Uygulama", oynat ve sonraki, altında dock.
- [x] Raf'a geçince ada 288'e büyüyor.
- [x] Dock'tan Home'a dönünce ada kısalıyor; altında kalan imleçle 1,5 sn boyunca açık kalıyor.
- [x] Yatay tekerlek (+120) bir sonraki sekmeye geçiriyor; imleç gidince ada kapanıyor.
- [x] Dokunmatik yüzeyde iki parmakla sola/sağa kaydırma yönü doğru (sola kaydırınca sonraki sekme; canlı).

**Ayarlar**
- [x] Görünüm: tema, Home düzeni ve açıklaması, açık ada genişliği "820 px", animasyon hızı "1,0×", ekran
      görüntüsünde gizle, iki parmakla kaydırma. Düzen: her sekme simgesiyle, açma/kapama, yukarı/aşağı.
- [x] Menüde Görünüm ve Düzen'e **gerçek fareyle** tıklamak sayfayı değiştiriyor (canlı). (Otomasyonun sentetik
      tıklaması menüye ulaşmadı; UI Automation ile seçince sayfalar açıldı.)
- [x] Home düzeni Kompakt/Minimal'e geçince açık ada hemen yeni boyutta; Geniş'te genişlik kaydırıcısı
      adayı genişletiyor (canlı).
- [ ] Tema değişince ada yeni temada.
- [ ] Ekran görüntüsünde gizle açıkken ada ekran kesme aracında görünmüyor.
- [x] Düzen'de bir sekmeyi kapatınca dock'tan kalkıyor, sırası değişince dock'ta da değişiyor; son açık
      sekme kapatılamıyor (canlı).

**Ölçüm** (Release JIT, ada ve Ayarlar kapalı, 60 sn, `media-state.ps1` önce ve sonra)

| Tarih | Yapı | Durum | CPU | Özel bellek | Çalışma kümesi |
|---|---|---|---|---|---|
| 2026-10-27 | W1 adım 3, Release JIT x64 | Chrome'da video **çalıyor** (önce ve sonra) | %0,275 (müzik hedefi < %1) | 39,2 MB | 142,5 MB |
| 2026-10-27 | W1 adım 3, Release JIT x64 | **Boşta:** medya oturumu yok (önce ve sonra); Ayarlar bir kez açılıp kapatılmış | %0,004 (hedef < %0,1) | 44,3 MB (hedef < 80 MB) | 152,4 MB |

- [x] Boşta (medya yok) CPU < %0,1.

## W1 · Adım 2: tray ve Ayarlar penceresi (2026-10-27)

Debug ve Release (JIT). Ekran seçimi, girişte başlatma ve yedekleme mantığı Core testlerinde
(`GeneralSettingsTests`; 104'ten 123'e).

**Tray**
- [x] Simge Windows'a kaydoluyor; Windows 11 onu taşma menüsüne (**^**) koyuyor (beklenen davranış).
- [ ] Koyu görev çubuğunda beyaz, açıkta siyah kapsül; temayı değiştirince simge de değişiyor.
- [x] Simgeye tıklayınca Ayarlar açılıyor (canlı).
- [x] Sağ tık › **Ayarlar…** ve **ISLe8'den çık** çalışıyor; menü dışarı tıklayınca kapanıyor (canlı).
- [x] ISLe8'den çıkınca simge bildirim alanından hemen kalkıyor (canlı).

**Ayarlar penceresi**
- [x] ISLe8 çalışırken yeniden açmak Ayarlar'ı açıyor (ikinci süreç 96 ms'de çıkıyor).
- [x] Pencere 880×680 DIP, ekranın ortasında; Mica, solda Genel, Türkçe metinler Mac'teki gibi.
- [x] W0'dan kalan gecikmeler göçten sonra "0,00 sn" ve "0,35 sn" görünüyor.
- [ ] Windows açık moddayken pencere açık, koyu moddayken koyu; açıkken değiştirince de geçiyor.
- [ ] Sağ tık › **Ayarlar…** pencereyi açıyor; açıkken yeniden istemek onu öne getiriyor.

**Genel**
- [x] **Girişte başlat** açılınca `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` › `ISLe8` yazılıyor,
      kapatınca siliniyor; oturumu kapatıp açınca ISLe8 başlıyor (canlı).
- [x] **Ekran** › Ana ekran / Etkin ekran / bir monitör: ada hemen oraya geçiyor; açıklama değişiyor (canlı, tek monitör).
- [ ] Etkin ekran: başka monitördeki bir pencereyi öne getirince ada o monitöre geçiyor.
- [x] **Dil** değişince ISLe8 yeniden başlıyor ve her şey yeni dilde (canlı).
- [x] **Açılma** › Tıklayınca: ada üzerine gelince yalnızca büyüyor, tıklayınca açılıyor; Açılma gecikmesi
      kartı gizleniyor (canlı).
- [x] Gecikme kaydırıcıları adanın davranışını hemen değiştiriyor (canlı).
- [x] **Dışa aktar…** bir JSON yazıyor; **İçe aktar…** onu okuyup yeniden başlıyor; başka bir dosya
      "Ayarlar içe aktarılamadı" diyor (canlı).

**Ölçüm** (`measure.ps1 -Seconds 60`, açıldıktan 30 sn sonra, ada ve Ayarlar kapalı, Release JIT). Medya durumu
artık ölçümle birlikte SMTC'den okunuyor.

| Tarih | Yapı | Durum | CPU | Özel bellek | Çalışma kümesi |
|---|---|---|---|---|---|
| 2026-10-27 | W1 adım 2, Release JIT x64 | Chrome'da video **çalıyor** (geniş pill, ses çubukları) | %0,299 (müzik hedefi < %1) | 42,0 MB | 148,1 MB |
| 2026-10-27 | W1 adım 2, Release JIT x64 | **Boşta:** Chrome duraklatılmış (ölçümden önce ve sonra `media-state.ps1`), 11 dk çalıştıktan sonra | %0,004 (hedef < %0,1) | 41,9 MB | 148,2 MB |

- [x] Boşta (medya duraklatılmış) CPU < %0,1. Adım 1'in %0,010'luk ölçümünde medya durumu
      kaydedilmemişti; bundan sonra her ölçüm `scripts\media-state.ps1` ile birlikte. Çalarken harcananın
      bir kısmı, ada kapalıyken de her SMTC olayında oynatıcının yeniden çizilmesinden (PORTING.md §3.1;
      4. adımda düzelecek).

## W1 · Adım 1: ada kuralları ve ayar modeli (2026-10-26)

Windows 11 Pro 26300, 3840×2160 %150, Debug ve Release (JIT). Fare otomasyonla gezdirildi (`SendInput`);
açık olup olmadığı, pill'in altındaki bir noktanın adanın penceresine düşüp düşmediğiyle ölçüldü. "Canlı"
işaretli maddeleri kullanıcı kendisi denedi.
Kuralların kendisi yeni Core testleriyle (71'den 104'e) elle ilerletilen bir saatte sınanıyor (`IslandModelTests`).

**Üzerine gelince (varsayılan)**
- [x] İmleç pill'e varınca ada 57 ms'de açılıyor (gecikme 0, en az 50 ms süzme).
- [x] Pill'in üstünde 20 ms kalan imleç açmıyor (iki deneme); 80 ms kalan açıyor.
- [x] İmleç çıktıktan 250 ms sonra hâlâ açık; 350 ms'de kapanmaya başlıyor.
- [x] Açılış ve kapanışta içerik %94'ten büyüyerek beliriyor, %90'a küçülerek siliniyor; kabuk pill'e
      katlanırken boş kutu kalmıyor (canlı, gözle).
- [x] Açılış Mac'teki ISLe8'e göre belirgin yavaş ya da hızlı değil (canlı).
- [x] Üst ortadaki tarayıcı sekmelerine giderken ada istenmeden açılmıyor (canlı, gecikme 0 + 50 ms).

**Tıklayınca modu** (`settings.json` › `"openMode": "click"`; ayar penceresi 2. adımda)
- [x] İmleç pill'de 1,5 sn durunca ada açılmıyor; pill 298×48'den 324×57 px'e büyüyor (+18 × 6 DIP).
- [x] Pill'e tıklayınca açılıyor; imleç çıkınca kapanıyor.

**Sabitleme, menü, dışarı tıklama**
- [x] Kapanma gecikmesi 1,5 sn iken: tıklamadan 2,1 sn'de, imleç çıkıp masaüstüne tıklayınca 0,9 sn'de
      kapanıyor (kalan süre yayın oturması).
- [x] Sağ tık › **Açık tut**: imleç uzaktayken ve dışarı tıklayınca açık kalıyor; menüde işaretli.
- [x] Açık tut kaldırılınca imleç dışarıdaysa kapanma gecikmesiyle kapanıyor (canlı).
- [x] Sağ tık menüsü dışarı tıklayınca kapanıyor (canlı).

**Diğer**
- [ ] Ayarlar › Erişilebilirlik › Görsel efektler › **Animasyon efektleri** kapalıyken yay yerine kısa,
      yumuşak bir geçiş var; içerik yalnızca soluyor.
- [x] W0'dan kalan `settings.json` (sürüm 1, 150/250 ms) yeni varsayılanlara (0 / 0,35 sn) geçiyor,
      eski alanlar yazılmıyor (Core testi; bu bilgisayardaki dosya ilk ayar değişikliğinde yeniden yazılır).
- [x] Mac'ten gelen metinler Mac'teki gibi: `.resw` girdilerinde `mac: <anahtar>` yorumu, test Mac reposu
      yanındayken iki dili karşılaştırıyor. Hizalanan: Keep Open, Screen, Main screen, System/Sistem, Artwork Color.

**Ölçüm** (`scripts\measure.ps1 -Seconds 60`, açıldıktan 30 sn sonra, ada kapalı). Bu bilgisayarda Native AOT
bağlayıcısı yok; Release `publish.ps1 -NoAot` (JIT, kendi .NET'iyle) ölçüldü. AOT yapısı (Release zip'i) ayrıca
ölçülmedi; W0'da AOT'nin çalışma kümesi JIT'inkinden belirgin düşüktü.

| Tarih | Yapı | Durum | CPU | Özel bellek | Çalışma kümesi |
|---|---|---|---|---|---|
| 2026-10-26 | W1 adım 1, Release JIT x64 | Ada kapalı, 60 sn; Chrome açıktı, medya durumu kaydedilmedi | %0,010 (hedef < %0,1) | 37,3 MB (hedef < 80 MB) | 138,5 MB |

# W0 test listesi

## İlk Windows çalıştırması (2026-10-26)

Windows 11 Pro 26300, tek monitör 3840×2160 %150. Önce Debug ve Release (kendi .NET'iyle JIT), sonra
v0.1.0 Release'inin Native AOT x64 zip'i. Fare otomasyonla gezdirildi; medya önce
Windows.Media.Playback ile açılan sessiz bir test parçasıyla, sonra Spotify ile denendi. İşaretli
maddeler bu çalıştırmalarda görüldü. ARM64 zip'i derlendi ama ARM bir bilgisayarda denenmedi.

Bulunup düzeltilenler:

1. **Pill yerine köşeli siyah kutu.** Gölge için kullanılan `LayerVisual`, piksel başına alfalı bu
   pencerede pill'in üstüne opak, ölçeklenmemiş siyah bir kutu çiziyordu. Gölge artık şeklin canlı
   kopyasını maske olarak kullanan bir `DropShadow` (`IslandSurface`).
2. **Pencerenin çevresinde 2 DIP beyaz çerçeve.** `AppWindow.Show()`, presenter'ın `WS_DLGFRAME`
   çerçevesini geri koyuyordu. Çerçeve gösterimden sonra yeniden sökülüyor ve `WM_NCCALCSIZE`
   istemci alanını her zaman pencerenin tamamı yapıyor (`IslandWindow`).
3. Bilgisayarda .NET 10 SDK yoktu (yalnızca .NET 6/8); `scripts\run.ps1` artık bunu söylüyor, `global.json`
   SDK sürümünü sabitliyor. Kullanmak için SDK gerekmesin diye Releases'te hazır zip var.
4. **Zip'in yarısı kullanılmayan kütüphaneydi** (0.1.1). `Microsoft.WindowsAppSDK` üst paketi AI, ML
   (`onnxruntime.dll`, `DirectML.dll`), Search, Widgets ve DWriteCore'u da getiriyordu. Uygulama artık
   yalnızca WinUI, InteractiveExperiences, Foundation ve Runtime paketlerine başvuruyor.

## Pencere

- [x] Ada, ana ekranın üst ortasında Ember renkli bir pill olarak duruyor; çevresinde siyah ya da beyaz
      bir kutu **yok**.
- [ ] Görev çubuğunda ve Alt+Tab'da ISLe8 görünmüyor.
- [x] ISLe8 açılınca o an kullandığın pencere odağı kaybetmiyor.
- [x] Pill'in **yanındaki** nokta alttaki pencereye gidiyor (tıklama adaya takılmıyor; `WindowFromPoint` ile).
- [x] İkinci kez açmak yeni bir ada açmıyor, sessizce çıkıyor.

## Hover ve animasyon

- [x] İmleç pill'de ~150 ms durunca ada aşağı doğru, hafif esneyerek açılıyor.
- [ ] Pill'in üstünden hızla geçince açılmıyor.
- [ ] Pill'e tıklayınca hemen açılıyor.
- [x] İmleç çıkınca ~250 ms sonra yumuşakça pill'e dönüyor.
- [ ] Açılış Mac'teki ISLe8'e göre belirgin yavaş ya da hızlı değil (değilse: DESIGN.md › Riskler 4).
- [x] Pill'den açık adanın içine geçmek (yazıların üstünden) adayı kapatmıyor.
- [ ] Sağ tık → **Açık tut** işaretliyken imleç çıkınca kapanmıyor.
- [x] Sağ tık menüsü açılıyor; Esc ile kapanınca odak önceki pencereye dönüyor.
- [ ] Sağ tık menüsü dışarı tıklayınca kapanıyor.
- [x] Sağ tık → **ISLe8'den çık** uygulamayı kapatıyor.
- [ ] Dokunmatik ekran varsa: dokununca açılıyor, ~4 sn sonra kapanıyor.

## Medya (Spotify, tarayıcıda YouTube, Medya Oynatıcı, Apple Music)

- [x] Müzik çalınca pill genişliyor; solda kapak, sağda hareket eden ses çubukları.
- [ ] Duraklatınca çubuklar hemen duruyor, pill ~1,5 sn sonra daralıyor.
- [ ] Şarkı değişirken pill daralıp yeniden genişlemiyor.
- [x] Çalan uygulamayı tamamen kapatınca ada çökmeden boş duruma geçiyor.
- [x] Açık adada başlık ve "Sanatçı · Uygulama" doğru.
- [x] Kapak, ilerleme ve süreler doğru (Spotify).
- [x] Spotify duraklatılmışken pill dar, açık adada oynat simgesi var.
- [ ] Oynat/duraklat, sonraki, önceki çalışıyor.
- [ ] Kaydırıcıyla şarkıda ileri/geri gidilebiliyor (destekleyen uygulamalarda).
- [ ] Karışık çal / tekrarla destekleyen uygulamada çalışıyor, desteklemeyende soluk.
- [x] Hiçbir şey çalmıyorken "Şu an bir şey çalmıyor" yazıyor.

## DPI ve monitörler (Ayarlar › Sistem › Ekran › Ölçek)

| Ekran | Ölçek | Pill (px) | Açık ada (px) |
|---|---|---|---|
| 1080p | %100 | 200×32 | 560×160 |
| 1080p | %125 | 250×40 | 700×200 |
| 2K (1440p) | %100 / %125 / %150 | 200×32 / 250×40 / 300×48 | 560×160 / 700×200 / 840×240 |
| 4K | %150 / %175 / %200 | 300×48 / 350×56 / 400×64 | 840×240 / 980×280 / 1120×320 |

- [x] 4K %150: pill 300×48, açık ada 840×240, net.
- [ ] Diğer ölçeklerde pill net (bulanık değil) ve gözle aynı fiziksel boyutta.
- [ ] ISLe8 açıkken ölçeği değiştirince pill birkaç saniye içinde yeni boyuta geçiyor.
- [ ] Sağ tık → **Ekran** → başka monitör: ada oraya geçiyor ve o monitörün ölçeğinde doğru boyutta.
- [ ] Farklı ölçekli iki monitör arasında gidip gelince boyut her seferinde doğru.
- [ ] Görev çubuğu üstteyse ada görev çubuğunun altında duruyor.
- [ ] Ekran kesme aracıyla bir ekran görüntüsü alıp pill'i ölçmek, tabloyu doğrulamanın en kolay yolu.

## Tam ekran

- [ ] YouTube'u tam ekran yapınca (F11 ya da video tam ekranı) ada kayboluyor, çıkınca geri geliyor.
- [ ] Masaüstünü göster (Win+D) adayı gizlemiyor.

## Tema ve dil

- [x] Sağ tık → **Tema**: 7 tema listeleniyor; Midnight ve Ember uygulanıyor, seçim `settings.json`'a yazılıyor.
- [ ] Geri kalan 5 tema uygulanıyor.
- [ ] Sağ tık → **Dil**: Türkçe ↔ English anında değişiyor.
- [ ] ISLe8'i kapatıp açınca tema, dil ve ekran seçimi korunuyor.

## Ölçüm

README › Ölçüm. Hedefler DESIGN.md › Performans bütçesi'nde.

| Tarih | Yapı | Durum | CPU | Özel bellek | Çalışma kümesi |
|---|---|---|---|---|---|
| 2026-10-26 | v0.1.0, Native AOT x64 | Spotify çalıyor, ada kapalı, ses çubukları oynuyor, 30 sn | %0,43 (hedef < %1) | 37,3 MB (hedef < 80 MB) | 109,2 MB |

- [ ] Boşta (müzik yok, ada kapalı) CPU < %0,1.
