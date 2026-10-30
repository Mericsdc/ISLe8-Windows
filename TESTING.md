# Test listesi

Her maddeyi işaretle; olmayan varsa günlükle birlikte not et (README › Günlük ve ayarlar). W1'in
adımları en üstte, W0'ın listesi altta.

## AI sekmesi ve isle8 komutu (2026-10-29)

Paketli Release JIT (`install.ps1 -NoAot`). Hook olayları, plan, adım süreleri, onay/zaman aşımı/vazgeçme, betik
aktivitesi, kulak önceliği, açılış sekmesi ve hook kurulumu Core testlerinde (`AgentTests`; 270'ten 289'a).

- [x] `isle8 ping` takma adla her terminalde "ISLe8 çalışıyor"; ISLe8 kapalıyken "ISLe8 çalışmıyor" (çıkış 1).
- [x] Sahte bir Claude Code turu (`SessionStart`, `UserPromptSubmit`, `TodoWrite` 4 madde, `Read`, `Edit`, `Bash`):
      kulakta Claude simgesi ve turuncu çubuklar; `isle8 open agents` sekmeyi açıyor: "$ dotnet test …", 0:04,
      "Claude Code · ISLe8-Windows-main", "Adım 2/4", dört parçalı çubuk, süreleriyle adımlar, "Oturuma git".
- [x] `Stop` sonrası "Claude Code bitti · ISLe8-Windows-main" banner'ı ve son mesaj.
- [x] İzin isteği: "Claude Code onay istiyor · …" banner'ı, "$ git push origin main", Onayla / Reddet / Terminalde sor.
      Onaylanınca hook `{"hookSpecificOutput":…"behavior":"allow"}` yazdı.
- [x] Bekleyen hook öldürülünce banner kalkıyor, çıktı boş; kulak turuncu ünleme geçiyor (oturum seni bekliyor).
- [x] `isle8 activity start --id derleme --title "Derleme" --progress 0.4`: kulakta terminal simgesi ve %40; `end` kaldırıyor.
- [x] `isle8 devices`: "Pil bildiren bağlı kablosuz cihaz yok".
- [x] Ayarlar › Ajanlar: Claude Code ve Codex için "Bağla", notlar, onay ayarları (90 sn), komut satırı örnekleri.
- [x] Hook isteği ve yanıtı 110 ms (JIT; AOT'ta daha kısa).
- [ ] Ayarlar › Ajanlar › Claude Code › Bağla; gerçek bir Claude Code oturumunda kart canlı ilerliyor, onay adadan veriliyor.
- [ ] Codex'te `/hooks` ile güvenince aynısı.
- [ ] "Oturuma git" Windows Terminal'i / VS Code'u / Claude uygulamasını öne getiriyor.
- [ ] Onaya 90 sn cevap verilmezse ajan kendi penceresinde soruyor.
- [ ] ISLe8 kapalıyken Claude Code hook'lar yokmuş gibi devam ediyor.

**Ölçüm** (ada kapalı)

| Tarih | Yapı | Durum | CPU | Özel bellek | Çalışma kümesi |
|---|---|---|---|---|---|
| 2026-10-29 | AI sekmesi, paketli Release JIT x64 | Yeni açılmış (35 sn), pipe dinleniyor, medya oturumu yok, bilgisayar kullanılıyordu, 60 sn | %0,011 (hedef < %0,1) | 44,6 MB (hedef < 80 MB) | 164,2 MB |
| 2026-10-29 | AI sekmesi, paketli Release JIT x64 | Ayarlar iki kez açılıp kapatılmış, sekmeler ve banner'lar denenmiş, 60 sn | %0,064 | **82,2 MB** | 236,9 MB |

- [ ] İkinci satır: kullanımdan sonra bellek yine 80 MB'ın üstünde (Raf ölçümündeki açık madde). W6'nın ölçüm turunda bakılacak.

## Denetimden düzeltmeler (0.2.4, 2026-10-30)

Salt-okunur kod denetiminin kritik ve yüksek bulguları. Core testleri 345'ten 347'ye (raf silme sınırı, onayın tam metni).

- [x] **Girişte başlamıyordu:** görev açıktı (State 2) ama Windows açılışta ISLe8'i hiç başlatmadı (olay günlüklerinde iz yok); aynı
      açılışta ChatGPT'nin görevi çalıştı. Fark: `windows.startupTask` uzantısında `Executable`/`EntryPoint` yoktu. Eklendi; kurulu
      bildirimde görünüyor. [ ] Yeniden başlatınca ISLe8'in kendiliğinden açıldığı kullanıcıda doğrulanacak.
- [x] Raftan kaldırma yalnızca `FilesFolder<guid>` klasörünü silebilir; dışarı taşınan kopya artık kopya sayılmıyor (test).
- [x] Onay kartında isteğin tamamı (komutun her satırı, tam yol, diğer araçlarda bütün alanlar; 4000 karaktere kadar, kaydırılabilir);
      banner kesilen isteği "Devamı var (N satır)" diye belirtiyor (test).
- [x] Banner ya da bırakma hedefinden çıkan katman o anda tıklama almıyor ve şekil oturunca gizleniyor (açık adaya geçişte de).
- [x] Tam ekranda gizliyken Ctrl+Alt+V ve `isle8 open` yok sayılıyor.
- [x] Medya: aynı uygulamanın iki oturumu (iki tarayıcı sekmesi) artık oturum kimliğiyle ayrılıyor; kapanan sekmede takılmıyor.
- [ ] Medya: iki sekmede oynatıp birini kapatma (canlı).

## Büyütülen pencereler ve şerit (0.2.3, 2026-10-30)

- [ ] Görünüm › Ada › Büyütülen pencereler: üç seçenek; "hemen altından" büyütülmüş tarayıcının sekmeleri pill'in altına yapışıyor,
      "en üstünden" bant hiç yok.

## Güncellemeler (2026-10-30)

Sürüm 0.2.1 (0.2.0 etiketinin yayını CI'da isle8-cli geri yüklenmediği için çıkmadı). Release okuma, sürüm karşılaştırma, dosya seçimi, güvenilen adresler ve günlük denetim Core testlerinde
(`UpdateTests`; 338'den 344'e).

- [ ] Ayarlar › Genel › Güncellemeler: "Güncellemeleri denetle" → "ISLe8 güncel" ya da "0.2.0 sürümüne güncelle".
- [x] Denetle: "ISLe8 0.2.1 çıktı", düğme "0.2.1 sürümüne güncelle"; indirme ve SHA-256 tamam.
- [x] **Hata (bulundu, 0.2.2'de düzeltildi):** kurulu ISLe8 0.1.9 → 0.2.1'de betik paket kimliğiyle çalıştı (apply.log: "package identity: True")
      ve paket kaydedilirken Windows onu da kapattı; dosyalar yerindeydi, kayıt yoktu. Elle kaydedilip düzeltildi.
- [x] Kurulu ISLe8 (0.2.1) → 0.2.2 Görev Zamanlayıcı ile: betik paket dışında ("package identity: False"), dosyalar yerinde,
      kayıt tamam, ISLe8 0.2.2 (CI'ın Native AOT yapısı) kendiliğinden açıldı; görev ve eski klasör silindi. Görev Yöneticisi'nde 9,1 MB.
- [ ] Zip'ten açılan ISLe8 → 0.2.1: klasör yerinde güncelleniyor.
- [ ] Bildirim alanı menüsünde "Güncelle" öğesi yalnızca yeni sürüm varken.

## Bellek, raf, ses tuşları ve ada seçenekleri (2026-10-30)

Paketli Release JIT. Konumlar, üste bitişik yerleşim, ses adımları ve rafa kopyalanan dosyalar Core testlerinde
(`PlacementTests`; 328'den 337'ye).

- [x] **Bellek:** Görev Yöneticisi'ndeki "Bellek" (etkin özel çalışma kümesi) 92 MB'tı. Ada kapandıktan 5 sn sonra kullanılmayan
      sayfalar Windows'a veriliyor: açılıştan sonra 8,4 MB, ada açıkken 32 MB, kapandıktan 5 sn sonra 6,0 MB. Yeniden açılış gecikmesiz.
- [x] Yönetilen yığın yalnızca ~10 MB; özel bayt (commit) ~115 MB'ın çoğu JIT yapısının yerel yığınları ve DLL'leri. Sürüm yapısı
      Native AOT (CI'da); bu bilgisayarda C++ araçları olmadığı için AOT ölçülemedi.
- [x] **Disk:** Codex çalışırken her yazmada oturum kaydının son 256 KB'ı yeniden okunuyordu (~0,1 MB/sn); artık yalnızca eklenen
      satırlar okunuyor.
- [x] Pano okuması: pano meşgulse 150 ve 300 ms sonra iki kez daha deneniyor (önce COMException ile düşüyordu).
- [x] Görev çubuğu ortada: pencere çalışma alanının altında (Bottom = 2088), ada yukarı açılıyor, dock altta; saat ortada.
- [x] Üste bitişik: üst köşeler düz, alt köşeler Mac'teki gibi (boşta 9, göstergede 20), saat ortada.
- [ ] Üste bitişik: içbükey omuzlar (gösterge 8, açık ada 10 DIP) ekranın üst kenarına akıyor; halka çizimiyle, canlı görülmedi.
- [x] Sabit sesli kulaklıkta ses tuşları devralınınca Windows'a bırakılmıyor; gösterge seviyeyi gösteriyor (kod; tuşla canlı denenmedi).
- [x] Ses tuşlarını devralma: kanca kuruluyor ("taken over from Windows"); sessiz tuşunda adada "%0" göstergesi, ikinci basışta ses
      geri açılıyor. Kulaklık sabit sesli olduğu için yukarı/aşağı tuşları Windows'a bırakılıyor (doğru).
- [ ] Müzik çalarken şarkı adı ortada; uzun adlar kayıyor, ada açılınca duruyor.
- [ ] Görev çubuğunda solda ve sağda; ekran kenarından 16 DIP içeride, o tarafa yaslanarak açılıyor.
- [ ] Başka bir paketli uygulamadan rafa dosya sürükleme: kopyası rafta, çift tıklayınca açılıyor.
- [ ] Chrome bildiriminde simge net (256 piksel istenip ekrandaki boyutta çözülüyor).
- [ ] Laptop hoparlöründe ses tuşları devralınınca Windows'un göstergesi çıkmıyor, ISLe8'inki çıkıyor.

**Ölçüm** (`scripts/measure.ps1`; "özel" Görev Yöneticisi'ndeki bellek, etkin özel çalışma kümesi)

| Tarih | Yapı | Durum | CPU | Özel bellek | Çalışma kümesi |
|---|---|---|---|---|---|
| 2026-10-30 | Kırpma ve Codex düzeltmesi, paketli Release JIT x64 | Taze açılış, ada kapalı, müzik yok, 60 sn | %0,008 (hedef < %0,1) | 10,7 MB (hedef < 80 MB; önce 59,9–94,6) | 30,0 MB |

## Pil, LAN ve ses/parlaklık göstergesi (2026-10-29)

Paketli Release JIT. Pil olayları, kablo, gösterge değerleri, boyutlar ve kapanma sırası Core testlerinde (`StatusTests`;
303'ten 328'e). Test bilgisayarı kapağı kapalı, harici monitöre bağlı bir dizüstü; çıkış aygıtı sesi kendi ayarlayan bir
kulaklık (Arctis Nova Pro Omni, Windows'ta 0–0 dB).

- [x] Başlıkta pil çipi "%80" (prizde, yeşil), "LAN" çipi (Ethernet), cihaz çipi; ada Kompakt'ta açıkken.
- [x] Gösterge (geçici bir yapıda tetiklenerek): "%62" sonra "%30", aynı göstergede çubuk yerinde güncelleniyor; 1,5 sn sonra pill.
      Simge solda, çubuk ve yüzde sağda; ölçüler Mac'teki gibi (396 × 32).
- [x] Şarj kulakları: yeşil şimşekli pil ve "%61" ortadan yaylanarak açılıyor, süre bitince içeri kayıp ada küçülüyor (420 × 32).
- [x] Kablo çıktı kulakları: beyaz "Wi-Fi". Düşük pil banner'ı: turuncu, "Pil azalıyor · %19 kaldı · şarja tak".
- [x] Parlaklık: günlükte "waiting for the built-in display (lid closed)"; WMI olay sorgusu kabul ediliyor, bekleme olay gelene
      kadar uyuyor (deneme programıyla doğrulandı).
- [x] Sesi sabit aygıt: seviye hep %100; programla değiştirilemiyor, gösterge çıkmıyor (Mac'teki "ayarlanamaz" gibi).
- [ ] Laptop hoparlöründe ses tuşları: gösterge çıkıyor, açık adada çıkmıyor; sessize alınca üstü çizili hoparlör ve boş çubuk.
- [ ] Şarj aletini çıkarıp takma: beyaz "Pilde %NN" ve yeşil "Şarj oluyor %NN" kulakları (gerçek olay).
- [ ] Ethernet kablosunu çıkarıp takma: kulaklar ve LAN çipi.
- [ ] Kapak açıkken parlaklık tuşları: gösterge; otomatik parlaklık ve ekranın kararması göstermiyor.
- [ ] Geniş (Dashboard) düzende medya kartının altında ses kaydırıcısı; sürükleyince ses değişiyor.
- [ ] Ayarlar › Sistem › Göstergeler ve Pil; Görünüm › Başlık çubuğu › Pil ve LAN anahtarları.

**Ölçüm**

| Tarih | Yapı | Durum | CPU | Özel bellek | Çalışma kümesi |
|---|---|---|---|---|---|
| 2026-10-29 | Pil ve ses, paketli Release JIT x64 | Taze açılış, ada kapalı, müzik çalıyor (ses çubukları), 60 sn | %0,369 (müzik hedefi < %1) | 59,9 MB (hedef < 80 MB) | 194,8 MB |
| 2026-10-29 | Aynı | Ada iki kez açılıp kapanmış, müzik çalıyor, 60 sn | %0,330 | 94,6 MB | 338,3 MB |

CPU'nun neredeyse tamamı müzik çalarken ses çubuklarını süren kompozisyon iş parçacığında (önceki ölçümlerle aynı, %0,30–0,43).
Yeni parçalarda zamanlayıcı yok; müziksiz boşta ölçüm bir sonraki turda.

## Kullanım limitleri (2026-10-29)

Paketli Release JIT. Claude /usage satırları, maliyet özeti (`NoLimits`), hesap, Antigravity grupları, Codex sunucusu ve oturum
kaydı, yüzde yuvarlama, tempo ve %90 uyarısı Core testlerinde (`UsageTests`; 293'ten 303'e), test bilgisayarındaki gerçek
yanıtlarla.

- [x] Codex (npm'den, yerel `codex.exe` doğrudan): app-server 1,5 sn'de; 5 saat %10, haftalık %68, plan "ChatGPT Team".
- [x] Antigravity (`agy`): 6,4 sn; Gemini 4 Argon haftalık %99 (kırmızı halka, turuncu yazı), Gemini Models %5.
- [x] Claude (`claude.exe`, izin listeli ortamla): giriş var, abonelik türü boş; `/usage` maliyet özeti döndürüyor → "Kota sınırı yok".
- [x] AI sekmesi (Kompakt): sağda "Limitler" sütunu, "şimdi", yenile düğmesi; tooltip'te ürün ve plan.
- [x] ISLe8'den kalan CLI süreci yok (görünen `codex.exe`'ler ChatGPT uygulamasının).
- [x] **Hata (bulundu, düzeltildi):** ada kapalıyken sekme değişince (`isle8 open agents`, Ctrl+Alt+V) sekme boş görünüyordu;
      daha önce geçişle soldurulan sekmenin saydamlığı 0'da kalıyordu. Kapalı geçişte saydamlık 1'e dönüyor.
- [ ] Yenile düğmesi: Codex ve Claude en fazla 30 sn'de bir canlı sorulur; dönen halka görünüyor.
- [ ] Bir pencere %90'ı geçince "Limit dolmak üzere · sıfırlanma …" banner'ı bir kez çıkıyor.
- [ ] Codex'te bir istek gönderilince 2 sn içinde Codex satırı güncelleniyor (oturum kaydından, CLI çalışmadan).
- [ ] Bilgisayar uyuyup uyanınca sorgular durup yeniden başlıyor.
- [ ] Ayarlar › Ajanlar › Kullanım limitleri: anahtar kapatılınca kart kalkıyor, servis anahtarları ve tasarruf modu çalışıyor.
- [ ] Geniş (Dashboard) düzende kart çalışma kartlarının altında, servisler yan yana.

**Ölçüm** (ada kapalı)

| Tarih | Yapı | Durum | CPU | Özel bellek | Çalışma kümesi |
|---|---|---|---|---|---|
| 2026-10-29 | Limitler, paketli Release JIT x64 | Açılıştaki sorgulardan sonra, tasarruf modu açık, ada iki kez açılmış, medya yok, 60 sn | %0,004 (hedef < %0,1) | 55,9 MB (hedef < 80 MB) | 188,9 MB |

## Cihazlar: alıcıyla bağlanan cihazlar (2026-10-29)

Canlı geri bildirim: "takılı ekipmanlarımın şarjı görünmüyor (SteelSeries kulaklık, Razer fare)". İkisi de Bluetooth değil,
kendi alıcısıyla bağlı; Windows pillerini bilmiyor. Paket protokolleri Core testlerinde, cihazların verdiği gerçek yanıtlarla
(`VendorProtocolTests`; 289'dan 293'e).

- [x] Razer Viper V3 Pro (1532:00C1, Synapse açık): pil sorgusu durum 0x02, 0x2C/255 → %17, şarj olmuyor.
- [x] SteelSeries Arctis Nova Pro Omni (1038:2290): `01 B0` sorusuna `01 B0 00 00 01 00 5E 64 08 … 08`: kulaklık %94,
      yedek pil %100, bağlı.
- [x] Cihazlar sekmesi: Bu bilgisayar %80 · Arctis Nova Pro Omni (kulaklık %94, Yedek pil %100) · Razer Viper V3 Pro %17
      (turuncu). Başlıkta fare %17. `isle8 devices` aynı satırları veriyor.
- [ ] Yüzdeler Synapse'te ve baz istasyonunun ekranında / GG'de gösterilenle aynı (hangisinin yedek pil olduğu dahil).
- [ ] Fare kapatılıp açılınca (alıcı takılıyken) 10 dk içinde ya da ada açılınca kart gidiyor / geliyor.
- [ ] Kulaklık şarja takılınca ⚡ ve "Şarj oluyor"; fare kablosuyla şarj olurken ⚡.
- [ ] Alıcı çıkarılıp takılınca 2–10 sn içinde kart gidiyor / geliyor.

## Cihazlar sekmesi (2026-10-29)

Paketli Release JIT (`install.ps1 -NoAot`). Kısa ad, tür (ad, Apple ürün kimliği, Class of Device, LE görünümü),
birleştirme, halka renkleri, satırlar ve sığdırma, bağlanma/düşük pil kuralı ve AirPods yayını Core testlerinde
(`DeviceTests`; 257'den 270'e). Test bilgisayarında pil bildiren Bluetooth cihazı yok (eşleşmiş iki LG hoparlör
pil bildirmiyor); banner'lar, çip ve kartlar geçici bir deneme listesiyle görüldü, deneme kodu silindi.

- [x] Cihazlar sekmesi: "Bu bilgisayar" kartı, yeşil halkada dizüstü, %80, "Prize takılı"; altında boş durum metni.
- [x] Deneme listesiyle: fare %12 (kırmızı yay), AirPods Pro'da Sol/Sağ/Kutu ayrı halkalar, kumanda; 5 halkadan sonra
      ikinci satır. Kompakt düzende iki satır 196 DIP'e sığıyor (halkalar 30'a küçülüyor).
- [x] Başlıkta pili en az olan cihazın çipi (fare, kırmızı %12).
- [x] "Pil azaldı" banner'ı (kırmızı) ve "Bağlandı" banner'ı (Sol/Sağ/Kutu rozetleri), 420 × (pill + 56).
- [x] Ayarlar › Sistem › Kablosuz cihazlar: üç anahtar ve açıklama.
- [ ] Gerçek bir BLE fare/klavye: yüzde Windows Ayarları › Bluetooth ve cihazlar'dakiyle aynı.
- [ ] Klasik bir kulaklık (HFP pil göstergesi olan): yüzde Windows Ayarları'ndakiyle aynı; bağlanınca 2 sn sonra banner.
- [ ] AirPods: bağlanınca tek yüzde; Cihazlar sekmesi açıkken birkaç saniyede Sol, Sağ ve Kutu ayrı halkalara geçiyor;
      yakındaki başka birinin AirPods'u karışmıyor.
- [ ] Pil %15'e inince bir kez uyarı; %30'un üstüne şarj olana kadar tekrar yok.
- [ ] Cihaz kopunca kart ve çip gidiyor.
- [ ] Dizüstü şarja takılınca/çıkarılınca kartın alt satırı "Şarj oluyor" / "Pilde" oluyor (⚡ şarjdayken).

**Ölçüm** (ada kapalı)

| Tarih | Yapı | Durum | CPU | Özel bellek | Çalışma kümesi |
|---|---|---|---|---|---|
| 2026-10-29 | Cihazlar, paketli Release JIT x64 | Yeni açılmış, eşleşmiş cihaz izleyicileri açık, LG hoparlör bağlı (pilsiz), medya oturumu yok, 60 sn | %0,001 (hedef < %0,1) | 43,9 MB (hedef < 80 MB) | 159,1 MB |

## Paket (MSIX) ve bildirim aynası (2026-10-29)

Paketli Debug, sonra `scripts\install.ps1 -NoAot` ile Release JIT. Bildirim kuyruğu, kilit, susturma listesi,
süre ve banner boyutu Core testlerinde (`NotificationTests`; 246'dan 257'ye).

- [x] `install.ps1` paketi `artifacts\package-win-x64`'ten kaydediyor ve açıyor; ayarlar, pano geçmişi, raf ve
      notlar zip sürümüyle aynı klasörde (`%LOCALAPPDATA%\LRXe8\ISLe8`, paketin sanallaştırması kapalı).
- [x] Bildirim erişimi: Windows izin vermiş, günlükte "notifications: listening"; `NotificationChanged` olayı
      paketli masaüstü uygulamasında geliyor (DESIGN.md §6 madde 8'deki risk çıkmadı).
- [x] Rahatsız etmeyin açıkken ada bildirim göstermiyor (Ayarlar › Bildirimler'deki anahtar açıkken); kapatınca
      PowerShell'in deneme bildirimi adada çıktı: "Windows PowerShell", başlık, gövde.
- [x] Üç bildirim art arda: ilki "+2" ile, 4 sn sonra ikincisi "+1" ile.
- [x] Ayarlar › Bildirimler: "Okunuyor", açıklama, Rahatsız etmeyin anahtarı, 4,0 sn kaydırıcısı, boş uygulama listesi.
- [x] Ayarlar › Genel › Girişte başlat: başlangıç görevinin durumunu okuyor (kapalı).
- [ ] Gmail (Chrome) bildirimi adada Chrome'un simgesiyle çıkıyor; tıklayınca açık Chrome penceresi öne geliyor,
      yeni pencere açılmıyor.
- [ ] Teams/Outlook bildirimi çıkıyor; banner'a tıklayınca uygulama açılıyor.
- [ ] Ayarlar › Bildirimler › Uygulamalar'da gönderen uygulama listeleniyor; anahtarı kapatılan uygulamanın bildirimi çıkmıyor.
- [ ] Banner'ın üstüne gelince süre duruyor; imleç çıkınca 1,2 sn sonra kapanıyor.
- [ ] Ekran kilitlenip açılınca kilitliyken gelenler adada birikmemiş oluyor.
- [ ] Girişte başlat açılınca Windows Ayarları › Uygulamalar › Başlangıç'ta ISLe8 görünüyor; oturum açılınca ada geliyor.

**Ölçüm** (ada kapalı)

| Tarih | Yapı | Durum | CPU | Özel bellek | Çalışma kümesi |
|---|---|---|---|---|---|
| 2026-10-29 | Paketli, Release JIT x64 (`install.ps1 -NoAot`) | Yeni açılmış, bildirim dinleniyor, Chrome'da medya duraklatılmış, 60 sn | %0,002 (hedef < %0,1) | 49,6 MB (hedef < 80 MB) | 177,9 MB |

## Üstte ayrılan şerit (2026-10-29)

- [x] Ada açılınca Windows'un çalışma alanı y=66'dan başlıyor (%150'de 44 DIP); masaüstü simgeleri şeridin altında.
- [x] **Hata (canlı):** ada üzerine gelince açılıyor ama tam ekran tarayıcının altında kalıyordu; yalnızca şerit içindeki
      sabitle/ayarlar düğmeleri görünüyordu. Pencere "her zaman üstte" özelliğini kaybetmişti. Düzeltme: alta indiren her
      pencere hareketi engelleniyor, açılışta yeniden üste alınıyor. Günlük her açılışta "kept on top (asked to go after …,
      Chrome_WidgetWin_1)" diyor: adayı bir Chrome penceresinin arkasına koyma isteği açılışta geliyor.
- [ ] Tam ekran tarayıcının sekmeleri pill'in altında kalmıyor (canlı).
- [ ] Ayarlar › Genel › "Pencereler adanın altında başlasın" kapatılınca şerit kalkıyor; ISLe8 kapanınca da kalkıyor.

## Raf sekmesi (2026-10-29)

Debug (JIT). Raf deposu (öne alma, kopya, taşınan dosyayı bulma, silinenin düşmesi), benzersiz adlar ve
bırakma hedefi durumu Core testlerinde (`ShelfTests`; 240'tan 245'e). Sürükleme canlı denenecek.

- [ ] Explorer'dan bir dosyayı pill'e sürükleyince ada "Rafa bırak" ve "Paylaş" bölgelerine açılıyor; üzerine
      gelinen bölge renkleniyor; dışarı çıkınca ada pill'e dönüyor.
- [ ] "Rafa bırak"a bırakınca ada Raf sekmesinde açılıyor; dosya kutucukta küçük resmiyle, "1 öğe".
- [ ] "Paylaş"a bırakınca Windows'un paylaşım penceresi açılıyor (Yakındaki Paylaşım dahil).
- [ ] Kutucuk dışarı (masaüstü, e-posta) sürüklenebiliyor; çift tıklama açıyor; üzerine gelince ✕ kaldırıyor.
- [ ] Sağ tık: Aç, Klasörde göster, Paylaş…, Zip olarak sıkıştır (yanına .zip, rafa ekleniyor), görselde
      Dönüştür › PNG/JPEG, Raftan kaldır.
- [ ] Dosya başka klasöre taşınınca ya da adı değişince uygulama yeniden açıldığında rafta yine görünüyor.
- [ ] Ayarlar › Raf › "Rafa bırakılan dosyaların kopyasını al" açıkken kopya ISLe8 klasöründe, kaldırılınca siliniyor.

**Ölçüm** (Release JIT, ada kapalı)

| Tarih | Yapı | Durum | CPU | Özel bellek | Çalışma kümesi |
|---|---|---|---|---|---|
| 2026-10-29 | Raf, Release JIT x64 | Yeni açılmış, Chrome'da medya duraklatılmış, 45 sn | %0,007 (hedef < %0,1) | 41,8 MB (hedef < 80 MB) | 152,9 MB |
| 2026-10-29 | Raf, Release JIT x64 | Ölçüm sırasında bilgisayar kullanılıyordu (son giriş 0,4 sn önce) | %0,051 | **84,1 MB** | 208,9 MB |

- [ ] Kullanımdan sonra bellek 80 MB'ın üstünde kalabiliyor (ikinci satır). Hangi görünümün (renkli emoji
      yazı tipi, Ayarlar penceresi, sekmeler) tuttuğu W6'nın tam ölçüm turunda bulunmalı; boşta, yeni açılmışken 41,8 MB.

## Pano sekmesi (2026-10-29)

Debug (JIT). Geçmiş, arama (büyük/küçük harf, aksan, Türkçe ı/I, kelime başı), aynı içeriğin üste çıkması,
200/30 gün sınırı, sabitlenenler ve Ctrl+Alt+V'nin aç/kapa kuralı Core testlerinde (`ClipboardTests`;
231'den 239'a). Senin panona bir şey yazmamak için kayıt canlı denenecek.

- [x] Kopyalanan metin geçmişe kaydediliyor; kaynak uygulama öndeki pencerenin programı (otomasyonla: pano kısa
      bir an test metnine çevrilip geri kondu, test kaydı geçmişten silindi).
- [x] Bozuk `clipboard.json` `.broken` olarak kenara alınıyor, geçmiş baştan başlıyor (önce "açılamadı" kalıyordu).
- [ ] Bir metin, bir bağlantı, bir görsel (Ekran Alıntısı) ve Explorer'da dosya kopyalanınca Pano sekmesinde
      üstte; türüne göre simge, görsel küçük resmi, #FF9F0A gibi renkler renk örneğiyle.
- [ ] Aynı şey yeniden kopyalanınca yeni satır açılmıyor, mevcut satır en üste çıkıyor.
- [ ] Bir parola yöneticisinden (Bitwarden, 1Password, KeePassXC) kopyalanan kaydedilmiyor.
- [ ] Satıra tıklayınca panoya geri kopyalanıyor (yeşil onay); Alt ile tıklayınca önceki pencereye yapıştırılıyor.
- [ ] "toplanti" araması "Toplantı"yı buluyor; arama kutusuna tıklayınca yazılabiliyor.
- [ ] Üzerine gelince sabitle ve sil; sağ tık: Kopyala, Yapıştır, Sabitle, Tarayıcıda aç, E-posta yaz, Klasörde göster, Sil.
- [ ] Ctrl+Alt+V adayı Pano'da açıyor ve arama hazır; yeniden basınca kapanıyor.
- [ ] Ayarlar › Pano: kaydetmeyi kapatma, en fazla öğe ve gün, geçmişi temizle (sabitlenenler kalır),
      kaydedilmeyen uygulama ekleme (.exe seçerek).

**Ölçüm** (Release JIT, ada kapalı)

| Tarih | Yapı | Durum | CPU | Özel bellek | Çalışma kümesi |
|---|---|---|---|---|---|
| 2026-10-29 | Pano, Release JIT x64 | Chrome'da medya **duraklatılmış**, 45 sn | %0,003 (hedef < %0,1) | 40,8 MB (hedef < 80 MB) | 149,0 MB |

- [x] Yayın zip'inde `Assets` klasörü var (tepsi ve pencere simgeleri). Önceden `Content` öğeleri yayına kopyalanmıyordu;
      Release'de tepsi simgesi yüklenemiyordu.

## Notlar ve Sistem sekmeleri (2026-10-29)

Debug (JIT). Dock'ta artık Mac'teki yedi sekme var: Home, Raf, Pano, Notlar, AI, Cihazlar, Sistem
(kaydedilmiş sıraya yeni ikisi sona eklenir). Not deposu, göreli tarih ve sistem hesapları Core
testlerinde (`NotesTests`, `SystemTests`; 213'ten 231'e). Görünüm maddeleri canlı denenecek (fare o
sırada kullanılıyordu).

**Notlar**
- [ ] Yazma satırına tıklayınca yazılabiliyor (ada klavyeyi alıyor); Enter ekliyor, Esc vazgeçiyor.
- [ ] Notlar kart olarak, genişliğe göre 2–4 sütun; karta tıklayınca kopyalanıyor ve 1,2 sn "Kopyalandı".
- [ ] Üzerine gelince düzenle ve sil; sağ tık menüsünde Kopyala, Düzenle, Sil; düzenlenen kart vurgu çerçeveli.
- [ ] Not boşaltılıp kaydedilince siliniyor; uygulama yeniden açılınca notlar duruyor.

**Sistem**
- [ ] İşlemci, bellek, disk halkaları (yeşil, %60'tan turuncu, %85'ten kırmızı), "8 çekirdek", "12 GB / 32 GB",
      "200 GB boş"; ağ kartında LAN/Wi-Fi ve ↓/↑ hızları.
- [ ] Sağda en çok işlemci kullanan 5 süreç (yüzde ve bellek); ↗ Görev Yöneticisi'ni açıyor.
- [ ] Sekme kapalıyken örnekleme yok (ölçümle): boştaki CPU Sistem sekmesi açılıp kapandıktan sonra da < %0,1.

**Ölçüm** (Release JIT, ada kapalı, `media-state.ps1` önce ve sonra)

| Tarih | Yapı | Durum | CPU | Özel bellek | Çalışma kümesi |
|---|---|---|---|---|---|
| 2026-10-29 | Notlar ve Sistem, Release JIT x64 | Chrome'da medya **duraklatılmış**, 45 sn | %0,002 (hedef < %0,1) | 40,5 MB (hedef < 80 MB) | 147,8 MB |

## W1 · Zamanlayıcı, hava durumu ve takvim (2026-10-29)

Debug (JIT). Kurallar Core testlerinde: `TimerTests` (geri sayım, duraklatma, Pomodoro turları,
kronometre, kulak önceliği), `WeatherTests` (Open-Meteo yanıtı, Türkçe ayarda noktalı koordinat, saat
diliminden şehir, 30 dk kuralı), `CalendarTests` (ICS: Windows ve IANA saat dilimleri, tüm gün,
tekrarlayan etkinlik, EXDATE ve taşınan tekrar, toplantı linki, 5 dk uyarısı, hızlı ekleme); 171'den 213'e.
Ada otomasyonla açılamadı (o sırada fare kullanılıyordu); görünüm maddeleri canlı denenecek.

**Zamanlayıcı**
- [ ] Home'a zamanlayıcı sütunu eklenince (Ayarlar › Düzen) 5 dk / 15 dk, Pomodoro ve kronometre düğmeleri.
- [ ] Çalışırken etiket ("Odaklan · 1. tur"), süre, ilerleme çubuğu, duraklat/sürdür ve durdur.
- [ ] Ada kapalıyken müzik yoksa kulakta zamanlayıcı simgesi ve süre; müzik çalınca kulak medyaya geçiyor,
      durunca 8 sn sonra zamanlayıcıya dönüyor.
- [ ] Süre dolunca ses (Ayarlar › Widget'lar) ve 4 sn'lik "Süre doldu" banner'ı.
- [ ] Minimal'de çalışmıyorken 5/15 dk düğmeleri, çalışırken süre ve durdur.

**Hava durumu**
- [x] Open-Meteo bu bilgisayardan yanıt veriyor; saat diliminden "İstanbul" bulunuyor.
- [ ] Geniş: şehir, gökyüzü, sıcaklık, hissedilen, Y/D, nem, rüzgar, yön, UV, Şimdi + 5 saat, 5 gün.
- [ ] Kompakt: şimdi + beşer gün, › ile sonraki beş gün; güne dokununca altında ayrıntı satırı.
- [ ] Minimal: şehir, simge + sıcaklık, gökyüzü ve Y/D.
- [ ] Ayarlar › Widget'lar: şehir arama, seçme, saat dilimine dönme, °C/°F.
- [ ] Ağ yokken "Hava durumu alınamadı" ve "Tekrar dene".

**Takvim**
- [ ] Bağlantı yokken de sade takvim: Geniş'te ay görünümü, bugün dolu daire, seçili gün halka, oklar ayı
      değiştiriyor, ay başlığına tıklamak bugüne dönüyor; altında "Bu gün için etkinlik yok".
- [ ] Bir ICS bağlantısı eklenince etkinlikli günlerde nokta, altında günün etkinlikleri ve toplantı linkinde
      katıl düğmesi.
- [ ] Kompakt: bu hafta, oklar haftayı değiştiriyor, altında "Per 29 · 14:00 Toplantı +1".
- [ ] Minimal: sıradaki etkinlik ("5 dk sonra · 15:00"), yoksa bugünün tarihi ve "Etkinlik yok".
- [ ] "+" ile "14:30 Toplantı" yazıp Enter: varsayılan takvim uygulaması etkinliği eklemeyi öneriyor.
- [ ] Toplantıdan 5 dk önce 10 sn'lik banner, linkliyse "Katıl".

**Ölçüm** (Release JIT, ada kapalı, `media-state.ps1` önce ve sonra)

| Tarih | Yapı | Durum | CPU | Özel bellek | Çalışma kümesi |
|---|---|---|---|---|---|
| 2026-10-29 | Zamanlayıcı/hava/takvim, Release JIT x64 | Chrome'da medya **duraklatılmış**, 30 sn | %0,005 (hedef < %0,1) | 40,6 MB (hedef < 80 MB) | 145,8 MB |

- [x] Boşta CPU < %0,1, bellek < 80 MB.
- İlk iki ölçüm (%0,095 ve %0,288, 50 MB) sırasında Chrome'da medya kullanılıyordu (çal/duraklat, çubuklar).
  Aynı koşulda adım 4 (69e9f47) ile sırayla ölçünce fark çıkmadı: ikisi de çalarken %0,01–0,4, duraklatılmışken
  %0,005. Yeni kod boştayken iş yapmıyor (takvim bağlantısızken bekleyen bir şey yok, hava yalnızca Home açılınca).

## W1 · Adım 4: banner'lar, Home sütunları ve medya (2026-10-27, şeritler 2026-10-29)

Debug (JIT). Banner süreleri ve bekleme, sütun genişlikleri ve şeritler, sütun ayarının okunması, oturum
seçimi, devam zamanı, yeniden deneme ve şarkı değişimi kuralı Core testlerinde (`BannerAndHomeTests`;
149'dan 171'e). Test oturumu: `Windows.Media.Playback` ile sessiz bir WAV, başlık ve sanatçıyla.

29 Ekim'de PORTING.md ed73b8a'ya göre güncellendi: Kompakt ve Minimal Home artık kartsız şeritler
(Mac dab77e8), tema siyahın üstüne açılıyor. Aşağıdaki Kompakt ve Minimal maddeleri bu hale göre.

**Banner**
- [x] Çalarken şarkı değişince 340×78 banner: üst satır boş, kapak, başlık, sanatçı, sağda çubuklar;
      2,5 sn sonra pill'e dönüyor.
- [x] Başlığı çalmadan önce gelen parça (duraklatılmışken seçilip çalınan) banner almıyor (Mac kuralı).
- [ ] Spotify'da sonraki şarkıya geçince banner çıkıyor (canlı).
- [ ] İmleç banner'ın üstündeyken bekliyor, çıkınca 1,2 sn sonra kapanıyor; tıklayınca ada açılıyor (canlı).
- [ ] Ayarlar › Medya › "Şarkı değişince kısaca göster" kapalıyken banner çıkmıyor (canlı).

**Home**
- [x] Geniş: Takvim · Çalan şarkı · Hava durumu. Medya sütununda kapak 104, başlık, sanatçı, ilerleme,
      beş düğme; oynatıcının desteklemediği düğmeler soluk. Takvim ve hava "Windows'a yakında geliyor".
- [x] Kompakt: ada 870 × 192 (%150'de 1305 px), kartsız şerit; sütunlar ince çizgilerle ayrılıyor,
      oynatıcı tam ortada: kapak 64, başlık, sanatçı, beş düğme, oynat halkası vurgu renginde.
- [x] Minimal: ada 720 × 156, tek satır: kapak 38, başlık, önceki · oynat · sonraki; oynatıcı ortada.
- [x] Ember temasında kapalı pill siyah, açık ada ve banner temanın renginde.
- [ ] Kompakt'ta Raf'a geçince ada 288'e büyüyor, Home'a dönünce kısalıyor ve imleç dock'tayken açık
      kalıyor (canlı).
- [ ] Ada kapanırken tema rengi siyaha sönüyor, arada saydamlaşmıyor (canlı, Ember).
- [ ] İlerleme çubuğunu sürükleyip bırakınca şarkı o yere atlıyor, sürüklerken çubuk kalınlaşıyor (canlı).
- [ ] Chrome'da sonraki videoya geçince kapak yenileniyor (canlı).
- [ ] Spotify duraklatılıp 1 dk sonra sürdürülünce süre sıçramıyor (canlı).
- [ ] YouTube duraklatılmışken Spotify çalınca ada Spotify'ı gösteriyor (canlı).

**Ayarlar**
- [x] Düzen › Home sütunları: her sütun simgesi ve adıyla, yukarı/aşağı, kaldır; "Sütun ekle" yalnızca
      Home'da olmayanları sunuyor; altında Mac'in açıklaması.
- [x] Medya: "Şarkı değişince kısaca göster", "Kulakta ses çubukları".
- [ ] Sütun eklenip kaldırılınca ya da sırası değişince Home hemen değişiyor; hepsi kaldırılınca
      "Home boş. Ayarlar > Düzen'den sütun ekleyebilirsin." (canlı).
- [ ] "Kulakta ses çubukları" kapatılınca pill'deki çubuklar duruyor (canlı).

**Ölçüm** (Release JIT, ada ve Ayarlar kapalı, `media-state.ps1` önce ve sonra)

| Tarih | Yapı | Durum | CPU | Özel bellek | Çalışma kümesi |
|---|---|---|---|---|---|
| 2026-10-27 | W1 adım 4, Release JIT x64 | **Boşta:** medya oturumu yok (önce ve sonra), 60 sn | %0,003 (hedef < %0,1) | 36,7 MB (hedef < 80 MB) | 135,2 MB |
| 2026-10-27 | W1 adım 4, Release JIT x64 | Test oturumu **çalıyor** (önce ve sonra), çubuklar açık, 60 sn | %0,260 (müzik hedefi < %1) | 38,8 MB | 140,2 MB |
| 2026-10-27 | W1 adım 4, Release JIT x64 | Test oturumu **çalıyor**, çubuklar kapalı, 50 sn | %0,008 | 38,7 MB | 140,1 MB |
| 2026-10-29 | W1 adım 4 (şeritler, tema katmanı), Release JIT x64 | **Boşta:** medya oturumu yok (önce ve sonra), 60 sn | %0,002 (hedef < %0,1) | 37,1 MB (hedef < 80 MB) | 135,4 MB |

- [x] Boşta (medya yok) CPU < %0,1.
- [x] Kapalı adada çalarken çubuk animasyonundan başka bir şey çalışmıyor (PORTING.md §3.1 kabulü):
      çubuklar kapalıyken %0,008.

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
