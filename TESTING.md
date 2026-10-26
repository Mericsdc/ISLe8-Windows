# W0 test listesi

Her maddeyi işaretle; olmayan varsa günlükle birlikte not et (README › Günlük ve ayarlar).

## İlk Windows çalıştırması (2026-10-26)

Windows 11 Pro 26300, tek monitör 3840×2160 %150, Debug (JIT) ve Release (kendi .NET'iyle JIT).
Fare otomasyonla gezdirildi; medya, Windows.Media.Playback ile açılan sessiz bir test parçasıyla
denendi (Spotify ile değil). İşaretli maddeler bu çalıştırmada görüldü.

Bulunup düzeltilenler:

1. **Pill yerine köşeli siyah kutu.** Gölge için kullanılan `LayerVisual`, piksel başına alfalı bu
   pencerede pill'in üstüne opak, ölçeklenmemiş siyah bir kutu çiziyordu. Gölge artık şeklin canlı
   kopyasını maske olarak kullanan bir `DropShadow` (`IslandSurface`).
2. **Pencerenin çevresinde 2 DIP beyaz çerçeve.** `AppWindow.Show()`, presenter'ın `WS_DLGFRAME`
   çerçevesini geri koyuyordu. Çerçeve gösterimden sonra yeniden sökülüyor ve `WM_NCCALCSIZE`
   istemci alanını her zaman pencerenin tamamı yapıyor (`IslandWindow`).
3. Bilgisayarda .NET 10 SDK yoktu (yalnızca .NET 6/8); `scripts\run.ps1` artık bunu söylüyor, `global.json`
   SDK sürümünü sabitliyor. Kullanmak için SDK gerekmesin diye Releases'te hazır zip var.

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
- [ ] Kapak, ilerleme ve süreler doğru.
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
