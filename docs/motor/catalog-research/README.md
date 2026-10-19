# MOTOR katalog araştırması — 2026-10

Bu klasör, MOTOR için yapılan motosiklet ve yedek parça kataloğu araştırmasının kaynak raporudur. Veriler
`MotorShop.CatalogImport` modülüne paketlenmiştir (`src/Smartstore.Modules/MotorShop.CatalogImport/Research/2026-10/`) ve
admin panelinden **Motor Catalog Import → Paketli araştırma veri seti → Veri setini yükle** ile ara alana alınır.
Yükleme hiçbir motosikleti seçiciye eklemez ve hiçbir ürün oluşturmaz; her adım yönetici incelemesinden geçer.

| Dosya | İçerik |
|---|---|
| [kaynaklar.md](kaynaklar.md) | 43 kaynağın kaydı: tür, veri türleri, erişim yöntemi, izin durumu, görsel hakları, kısıtlar |
| [motosikletler.md](motosikletler.md) | 92 motosiklet referans kaydı, marka özeti ve başlangıç listesinin denetimi |
| [parcalar.md](parcalar.md) | 78 yedek parça araştırma adayı ve üreticinin belirttiği uyumluluk |
| [izin-talebi-sablonu.md](izin-talebi-sablonu.md) | Veri, görsel ve fiyat/stok izni için Türkçe e-posta şablonu |
| [sources-notes.md](sources-notes.md), [vehicles-notes.md](vehicles-notes.md), [parts-notes.md](parts-notes.md) | Araştırma günlükleri (İngilizce): denenen sayfalar, engellenen siteler, karar gerekçeleri |

## Yöntem

- Yalnızca herkese açık sayfalar, tek tek açılarak okundu. Toplu kazıma yapılmadı; giriş, CAPTCHA, ödeme duvarı veya bot
  korumasının aşılması denenmedi. Engellenen siteler (ör. Yuasa'nın Cloudflare kontrolü) atlandı ve günlüklere yazıldı.
- Yalnızca **olgular** kaydedildi: marka, model, nesil, model yılı, motor hacmi, parça numarası ve parça üreticisinin
  belirttiği uyumluluk. Görsel, teknik çizim, açıklama metni, fiyat ve stok kopyalanmadı.
- Her kayıt, alanlarını destekleyen kaynak URL'sini ve erişim tarihini taşır. Resmî kaynağı olmayan bir kayıt HIGH olamaz.
- Yaklaşık yıl kullanılmadı. Üretim sonu resmî kaynakta yoksa `yearTo` boş bırakıldı; seçici aralığı yalnızca resmî olarak
  doğrulanmış model yıllarını kapsar.
- Uyumluluk motor hacmine veya benzer isme göre tahmin edilmedi. Her uyumluluk satırı, parça üreticisinin o model ve
  yıllar için kendi kataloğunda yazdığı bilgidir.

## Sonuçlar

**Motosikletler:** 92 kayıt (56 HIGH, 31 MEDIUM, 5 LOW), 14 marka. 71 kayıt seçiciye eklenebilir; kalanlar ya doğrulanmış
yılı olmayan aday kayıtlardır (çoğu Türkiye marka sayfalarında model yılı yazmayan CFMOTO, Kymco, Mondial, RKS, Benelli)
ya da başka pazara aittir (Hindistan'a özel modeller ve motor sürümleri).

MotorShop.Fitment'in başlangıç listesindeki 35 motosikletten 7'si doğrulandı, 27'si düzeltme gerektirdi, 1'i
doğrulanamadı. En sık hatalar: birden çok neslin tek kayıtta birleştirilmesi (ör. MT-07 2014/2018/2021/2025), yanlış
motor hacmi (ör. CBR250R 249,6 cc; Pulsar NS200 199,5 cc) ve aralık içinde motor değişimi (KTM 390 Duke 2024'te 373'ten
398,7 cc'ye). Ayrıntı: [motosikletler.md](motosikletler.md).

**Yedek parçalar:** 78 araştırma adayı (54 doğrulanmış, 24 kısmi): Hiflofiltro yağ ve hava filtresi (16), EBC fren
balatası, disk, kampana ve debriyaj (32), NGK buji (20), JT dişli (10). Zincir ve akü kaydı yok (JT zincir parça numarası
vermiyor, Yuasa bot koruması nedeniyle okunamadı). Hiçbir kayıtta GTIN yok; hiçbir kaynakta görülmedi.

**İzinler:** 43 kaynağın hiçbiri AUTHORIZED değil; MOTOR'un bilinen yazılı bir anlaşması yok.
PUBLIC_FACTUAL_REFERENCE_ONLY 11, PERMISSION_REQUIRED 22, RESTRICTED 4, REJECTED 6. Parçaların bağlı olduğu dört kaynak
(Hiflofiltro, EBC, NGK, JT) PERMISSION_REQUIRED olduğu için parçalar araştırma adayı olarak kalır.

## Modülde nasıl kullanılır

1. **Veri setini yükle**: kaynak kaydı eklenir (mevcut kaynakların izin durumu değiştirilmez), motosikletler ve parçalar
   ara alana alınır. Aynı veri seti ikinci kez yüklenirse hiçbir şey değişmez.
2. **Motosikletler → Doğrulanmış kayıtları seçiciye ekle**: 71 motosiklet seçiciye eklenir. Aynı marka, model ve yıllara
   sahip mevcut bir kayıt varsa yenisi oluşturulmaz, mevcut kayda bağlanır.
3. **Denetim**: seçicideki doğrulanmamış motosikletler (ör. başlangıç listesi) listelenir. Ürün eşleşmesi olmayanlar
   kaldırılabilir; eşleşmesi olanlar korunur.
4. Parçalar için bir kaynağın izin durumu ancak yazılı izin alındıktan sonra güncellenmelidir. AUTHORIZED için sözleşme
   referansı zorunludur.

## Sahibin vermesi gereken bilgiler

- **Tedarikçi:** fiyat ve stok verecek yetkili tedarikçi veya distribütör ve onların XML/CSV/API akışı. Kayıtta
  Türkiye'deki adaylar ve önerilen sıra: [sources-notes.md](sources-notes.md).
- **Veri izni:** Hiflofiltro, EBC, NGK/Niterra ve JT'den katalog ve uyumluluk verisinin kullanımı için yazılı izin.
  Şablon: [izin-talebi-sablonu.md](izin-talebi-sablonu.md).
- **Görsel izni:** ürün fotoğrafları için ticari kullanım izni (ör. Niterra MediaBox lisansı) ya da MOTOR'un kendi
  fotoğrafları.
- **Ticari veriler:** alış maliyeti, onaylı satış fiyatı (TRY), vergi sınıfı (KDV) ve doğrulanmış stok.
- **Yayın onayı:** her parça için referans (B) ve satış (C) onayı admin panelinden verilir.
- **Hukuki kontrol:** Honda Türkiye ve KTM koşulları içeriğin ticari kopyalanmasını kısıtlıyor. Seçici için yalnızca
  model adı, yıl ve motor hacmi kullanılıyor; yine de bir hukuk kontrolü önerilir.

## Bilinen kısıtlar

- Avrupa kataloglarına dayalı uyumluluk, Asya ve Hindistan modellerinde zayıf (MT-25, TVS Apache RTR 200, bazı Bajaj modelleri).
- Hiflofiltro ve JT sayfaları metne çeviren bir araçla okundu; uzun OEM çapraz referans listeleri incelemeden önce canlı
  sayfayla karşılaştırılmalı.
- Kontrol edilmesi gereken noktalar [parts-notes.md](parts-notes.md) içinde listelenmiştir (ör. NGK IFR6G-11K / NC750X 2021+,
  EBC FA606HH'nin 2024 nesil değişimini kapsaması, sol/sağ konum bilgisi eksik balatalar).
- Araştırma günlüklerinde geçen ara dosyalar (`scripts/merge.cjs`, marka bazlı JSON dosyaları) çalışma dosyalarıydı ve
  depoya eklenmedi; nihai veriler `Research/2026-10/` altındaki üç dosyadır.
