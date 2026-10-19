# MOTOR CI

`motor` dalının derleme ve test hattı. Kaynak: [`workflows/motor-ci.yml`](workflows/motor-ci.yml).

## Ne yapar

| Adım | Açıklama |
|---|---|
| Solution kontrolü | `Smartstore.Core`, `Smartstore.Web`, `MotorShop.Fitment` ve üç test projesi `Smartstore.sln` içinde değilse hemen başarısız olur. Solution'dan çıkarılan bir proje, aksi halde derleme ve testlerde sessizce atlanırdı. |
| .NET kurulumu | SDK sürümü `global.json` dosyasından alınır (yerel derlemeyle aynı). |
| NuGet önbelleği | Anahtar: `*.csproj`, `Directory.Build.props`, `Directory.Packages.props`, `nuget.config`, `global.json`. |
| Restore → Build | Tek restore, ardından `-c Release --no-restore` ile tek derleme. |
| Modül çıktısı | `src/Smartstore.Web/Modules/MotorShop.Fitment/MotorShop.Fitment.dll` üretilmemişse başarısız olur. |
| Test | `dotnet test --no-build`. Takılan bir test 5 dakika sonra adıyla raporlanır (`--blame-hang`). |
| Özet | Derleme sonuç sayfasında assembly başına test tablosu ve başarısız testlerin listesi. |
| Artifact | `TestResults/` (`.trx` dosyaları) 14 gün saklanır. |

Tetikleyiciler: `motor` dalına açılan PR'lar, `motor` dalına push ve elle çalıştırma (`workflow_dispatch`).
Aynı PR'a yeni commit gelirse eski koşu iptal edilir; `motor` push'larında iptal yoktur.

Yetki: yalnız `contents: read`. Checkout token'ı diske yazılmaz (`persist-credentials: false`).
Paket, imaj, registry veya deploy adımı yoktur.

Yerelde aynı adımlar:

```bash
dotnet restore Smartstore.sln
dotnet build Smartstore.sln -c Release --no-restore
dotnet test Smartstore.sln -c Release --no-build --logger trx --results-directory TestResults
```

## Upstream iş akışları

Smartstore'dan gelen iş akışları referans için yerinde duruyor ama bu repoda hiçbir şey yayımlamaz veya silmez.

| Dosya | Durum |
|---|---|
| `publish-nightly.yml` | Zamanlanmış tetik kapalı. Her iki iş de yalnız `smartstore/Smartstore` reposunda çalışır (`ghcr.io/smartstore` push ve paket silme). İş akışı düzeyinde `permissions: {}`. |
| `publish-release.yml` | Yalnız `smartstore/Smartstore` reposunda çalışır. İş akışı düzeyinde `permissions: {}`. |
| `delete-old-workflow-runs.yml` | Zamanlanmış tetik kaldırıldı, yalnız elle çalışır, `dry_run` varsayılanı `true`. Yalnız `actions: write`. Üçüncü taraf eylem commit SHA'sına sabitlendi. |

Bu dosyalar upstream senkronunda çakışmasın diye silinmedi; değişiklikler dosyaların başında ve iş tanımlarındaki tek satırlık `if:` koşullarında.

## Dal koruma önerileri

Repo özel ve GitHub Free planında olduğu için dal koruması ve rulesets şu an **kullanılamıyor**
(`/branches/motor/protection` ve `/rulesets` API'leri 403 döner). Seçenekler: GitHub Pro'ya geçmek veya repoyu herkese açık yapmak.
Bu PR hiçbir repo ayarını değiştirmez.

Kullanılabilir olduğunda `motor` için önerilen ruleset:

| Ayar | Öneri | Neden |
|---|---|---|
| Merge'den önce PR zorunlu | Açık | Doğrudan push ile CI'ı atlamayı engeller. |
| Gerekli onay sayısı | Tek geliştiricide 0, ekip büyüyünce 1 | GitHub, PR sahibinin kendi PR'ını onaylamasına izin vermez; 1 onay tek kişilik repoda merge'ü kilitler. |
| Yeni push'ta eski onayları düşür | Açık (onay zorunluysa) | Onaydan sonra eklenen kod da incelenir. |
| Gerekli durum kontrolü | `Build and test (Release)` | `motor-ci.yml` içindeki iş adı. Adı değişirse kural da güncellenmeli. |
| Dal güncel olmalı (strict) | Açık | PR, `motor`'un son hâliyle test edilmiş olur. PR sayısı az olduğu için ek CI maliyeti düşük. |
| Force push ve dal silme | Engelli | Geçmişin yeniden yazılmasını önler. |
| Konuşmaların çözülmesi | Açık | İnceleme yorumları açıkken merge edilmez. |
| Bypass | Yalnız repo sahibi, acil durum için | |

Ek öneriler (repo ayarı, sahibinin onayıyla):

- Settings → Actions → General: izinli eylemleri "GitHub tarafından yazılanlar + seçilenler" ile sınırlayın (`Mattraks/delete-workflow-runs`). Varsayılan token yetkisi zaten "read" ve Actions'ın PR onaylaması kapalı; böyle kalmalı.
- "Automatically delete head branches" açılabilir.
- Dal koruması gelene kadar: `motor`'a yalnız yeşil PR'lar üzerinden merge edin. `motor` push'unda CI yine çalışır, ama hatayı ancak merge'den sonra yakalar.

## Bilinen açıklar

- `MotorShop.Fitment` için hedefli test yok. Riskli mantık `FitmentService` içinde: yıl aralığının yıllara açılması (`GetYearsAsync`), marka/model/yıl filtresi (`GetFittingProductIdsAsync`) ve araç çerezinin okunup yazılması (`GetSelection`/`SaveSelection`). Bunlar ayrı bir test projesi gerektiriyor (modül `Smartstore.Web`'e `Private=False` ile bağlı).
- Restore sırasında bilinen güvenlik açığı olan paket uyarıları (NU1902/NU1903) var; CI bunları hata saymıyor.
