using Smartstore.Core.Common.Configuration;
using Smartstore.Core.Configuration;
using Smartstore.Data.Migrations;
using Smartstore.Utilities;

namespace Smartstore.Core.Data.Migrations;

public class SmartDbContextDataSeeder : IDataSeeder<SmartDbContext>
{
    public DataSeederStage Stage => DataSeederStage.Early;
    public bool AbortOnFailure => false;

    public async Task SeedAsync(SmartDbContext context, CancellationToken cancelToken = default)
    {
        await context.MigrateLocaleResourcesAsync(MigrateLocaleResources);
        await MigrateSettingsAsync(context, cancelToken);
    }

    public async Task MigrateSettingsAsync(SmartDbContext context, CancellationToken cancelToken = default)
    {

    }

    public void MigrateLocaleResources(LocaleResourcesBuilder builder)
    {
        builder.AddOrUpdate("Enums.QuantizationMethod.Octree", "Hexadecatree (formerly Octree)", "Hexadecatree (vormals Octree)");
        builder.AddOrUpdate("Enums.JpegColorType.YCbCrRatio422", "YCbCr 4:2:2", "YCbCr 4:2:2");
        builder.AddOrUpdate("Enums.JpegColorType.YCbCrRatio411", "YCbCr 4:1:1", "YCbCr 4:1:1");
        builder.AddOrUpdate("Enums.JpegColorType.YCbCrRatio410", "YCbCr 4:1:0", "YCbCr 4:1:0");
        builder.AddOrUpdate("Admin.Configuration.Settings.Media.GifQuantizationMethod", "GIF palette quantization", "GIF-Palettenquantisierung");
        builder.AddOrUpdate("Admin.Configuration.Settings.Media.PngInterlaced", "Interlaced PNG (Adam7)", "PNG mit Zeilensprung (Adam7)");

        builder.AddOrUpdate("Admin.Configuration.Settings.Media.JpegColorType",
            "JPEG color type",
            "JPEG-Farbtyp",
            "Leave empty for automatic selection.",
            "Leer lassen für automatische Auswahl.");
        builder.AddOrUpdate("Admin.Configuration.Settings.Media.PngQuantizationMethod",
            "PNG palette quantization",
            "PNG-Palettenquantisierung",
            "Only affects palette-based PNG images. Has no effect on RGB or RGBA images.",
            "Wirkt nur auf PNG-Bilder mit Farbpalette. Hat bei RGB- und RGBA-Bildern keine Wirkung.");

        MigrateMotorResources(builder);
    }

    /// <summary>
    /// MOTOR: labels of the image processing and response caching media settings and of enums, which had none,
    /// and Turkish values of the media resources above. Entries with a language win over the
    /// language-neutral (English) entries, so the Turkish admin keeps its texts on every run.
    /// </summary>
    private static void MigrateMotorResources(LocaleResourcesBuilder builder)
    {
        const string prefix = "Admin.Configuration.Settings.Media.";

        void Add(string key, string en, string de, string tr, string enHint = null, string deHint = null, string trHint = null)
        {
            builder.AddOrUpdate(key).Value(en).Value("de", de).Value("tr", tr);
            if (enHint != null)
            {
                builder.AddOrUpdate(key + ".Hint").Value(enHint).Value("de", deHint).Value("tr", trHint);
            }
        }

        void Tr(string key, string tr)
            => builder.AddOrUpdate(key).Value("tr", tr);

        Tr("Enums.QuantizationMethod.Octree", "Hexadecatree (eski adıyla Octree)");
        Tr("Enums.JpegColorType.YCbCrRatio422", "YCbCr 4:2:2");
        Tr("Enums.JpegColorType.YCbCrRatio411", "YCbCr 4:1:1");
        Tr("Enums.JpegColorType.YCbCrRatio410", "YCbCr 4:1:0");
        Tr(prefix + "GifQuantizationMethod", "GIF palet indirgeme");
        Tr(prefix + "PngInterlaced", "Geçmeli PNG (Adam7)");
        Tr(prefix + "JpegColorType", "JPEG renk türü");
        Tr(prefix + "JpegColorType.Hint", "Otomatik seçim için boş bırakın.");
        Tr(prefix + "PngQuantizationMethod", "PNG palet indirgeme");
        Tr(prefix + "PngQuantizationMethod.Hint", "Yalnızca paletli PNG resimlerini etkiler. RGB ve RGBA resimlerinde etkisi yoktur.");

        Add(prefix + "ResponseCaching", "Response caching", "Antwort-Caching", "Yanıt önbelleği");
        Add(prefix + "DefaultResamplingMode",
            "Resampling mode", "Resampling-Modus", "Yeniden örnekleme yöntemi",
            "Algorithm used when images are resized.",
            "Algorithmus, der beim Skalieren von Bildern verwendet wird.",
            "Resimler yeniden boyutlandırılırken kullanılan algoritma.");
        Add(prefix + "DefaultImageQuality",
            "Image quality", "Bildqualität", "Resim kalitesi",
            "JPEG quality (0-100) used when images are saved.",
            "JPEG-Qualität (0-100) beim Speichern von Bildern.",
            "Resimler kaydedilirken kullanılan JPEG kalitesi (0-100).");
        Add(prefix + "PngCompressionLevel",
            "PNG compression level", "PNG-Komprimierungsstufe", "PNG sıkıştırma seviyesi",
            "Higher levels create smaller files but take longer.",
            "Höhere Stufen erzeugen kleinere Dateien, dauern aber länger.",
            "Yüksek seviyeler daha küçük dosya oluşturur ancak daha uzun sürer.");
        Add(prefix + "PngIgnoreMetadata",
            "Remove PNG metadata", "PNG-Metadaten entfernen", "PNG meta verilerini kaldır",
            "Metadata of PNG images is not saved.",
            "Metadaten von PNG-Bildern werden nicht gespeichert.",
            "PNG resimlerinin meta verileri kaydedilmez.");
        Add(prefix + "ResponseCacheDuration",
            "Cache duration (seconds)", "Cache-Dauer (Sekunden)", "Önbellek süresi (saniye)",
            "How long browsers and proxies may cache media files (max-age). Default: 604800 (7 days).",
            "Wie lange Browser und Proxys Mediendateien zwischenspeichern dürfen (max-age). Standard: 604800 (7 Tage).",
            "Tarayıcıların ve proxy sunucularının medya dosyalarını ne kadar süre önbellekte tutabileceği (max-age). Varsayılan: 604800 (7 gün).");
        Add(prefix + "ResponseCacheLocation",
            "Cache location", "Cache-Ort", "Önbellek konumu",
            "Where media files may be cached: anywhere (browser and proxies), only in the browser, or nowhere.",
            "Wo Mediendateien zwischengespeichert werden dürfen: überall (Browser und Proxys), nur im Browser oder nirgends.",
            "Medya dosyalarının nerede önbelleğe alınabileceği: her yerde (tarayıcı ve proxy), yalnızca tarayıcıda veya hiçbir yerde.");
        Add(prefix + "ResponseCacheNoStore",
            "Do not store in cache (no-store)", "Nicht im Cache speichern (no-store)", "Önbellekte saklama (no-store)",
            "Clients must not cache media files at all. Duration and location are then ignored.",
            "Clients dürfen Mediendateien überhaupt nicht zwischenspeichern. Dauer und Ort werden dann ignoriert.",
            "İstemciler medya dosyalarını hiç önbelleğe almaz. Bu durumda süre ve konum dikkate alınmaz.");
        Add(prefix + "AppendFileVersionToUrl",
            "Append file version to URL", "Dateiversion an URL anhängen", "Dosya sürümünü URL'ye ekle",
            "Adds a version hash to media URLs so that browsers load a file again after it has changed.",
            "Hängt einen Versions-Hash an Medien-URLs an, damit Browser eine geänderte Datei neu laden.",
            "Tarayıcıların değişen bir dosyayı yeniden yüklemesi için medya URL'lerine bir sürüm özeti ekler.");

        Add("Enums.ResponseCacheLocation.Any", "Anywhere (browser and proxies)", "Überall (Browser und Proxys)", "Her yerde (tarayıcı ve proxy)");
        Add("Enums.ResponseCacheLocation.Client", "Browser only", "Nur Browser", "Yalnızca tarayıcı");
        Add("Enums.ResponseCacheLocation.None", "Nowhere", "Nirgends", "Hiçbir yerde");

        Add("Enums.PngCompressionLevel.NoCompression", "No compression", "Keine Komprimierung", "Sıkıştırma yok");
        Add("Enums.PngCompressionLevel.BestSpeed", "Fastest", "Am schnellsten", "En hızlı");
        Add("Enums.PngCompressionLevel.DefaultCompression", "Default compression", "Standardkomprimierung", "Varsayılan sıkıştırma");
        Add("Enums.PngCompressionLevel.BestCompression", "Best compression", "Beste Komprimierung", "En iyi sıkıştırma");
        foreach (var level in new[] { 2, 3, 4, 5, 7, 8 })
        {
            Add("Enums.PngCompressionLevel.Level" + level, "Level " + level, "Stufe " + level, "Seviye " + level);
        }

        Add("Enums.QuantizationMethod.WebSafePalette", "Web-safe palette", "Websichere Palette", "Web uyumlu palet");
        Add("Enums.QuantizationMethod.WernerPalette", "Werner palette", "Werner-Palette", "Werner paleti");
        Add("Enums.QuantizationMethod.Wu", "Wu", "Wu", "Wu");

        Add("Enums.JpegColorType.YCbCrRatio420", "YCbCr 4:2:0", "YCbCr 4:2:0", "YCbCr 4:2:0");
        Add("Enums.JpegColorType.YCbCrRatio444", "YCbCr 4:4:4", "YCbCr 4:4:4", "YCbCr 4:4:4");
        Add("Enums.JpegColorType.Luminance", "Grayscale (luminance)", "Graustufen (Luminanz)", "Gri tonlama (parlaklık)");
        Add("Enums.JpegColorType.Rgb", "RGB", "RGB", "RGB");

        // Enums the admin shows in select lists without any resource (the member name was displayed).
        Add("Enums.CookieType.Required", "Required", "Erforderlich", "Zorunlu");
        Add("Enums.CookieType.Analytics", "Analytics", "Analyse", "Analiz");
        Add("Enums.CookieType.ThirdParty", "Third party", "Drittanbieter", "Üçüncü taraf");
        Add("Enums.CookieType.ConsentAdUserData", "Ad user data consent", "Einwilligung Werbe-Nutzerdaten", "Reklam kullanıcı verisi onayı");
        Add("Enums.CookieType.ConsentAdPersonalization", "Ad personalization consent", "Einwilligung personalisierte Werbung", "Kişiselleştirilmiş reklam onayı");

        Add("Enums.BadgeStyle.Secondary", "Secondary", "Sekundär", "İkincil (gri)");
        Add("Enums.BadgeStyle.Primary", "Primary", "Primär", "Birincil (tema rengi)");
        Add("Enums.BadgeStyle.Success", "Success", "Erfolg", "Başarı (yeşil)");
        Add("Enums.BadgeStyle.Info", "Info", "Info", "Bilgi (mavi)");
        Add("Enums.BadgeStyle.Warning", "Warning", "Warnung", "Uyarı (sarı)");
        Add("Enums.BadgeStyle.Danger", "Danger", "Gefahr", "Tehlike (kırmızı)");
        Add("Enums.BadgeStyle.Light", "Light", "Hell", "Açık");
        Add("Enums.BadgeStyle.Dark", "Dark", "Dunkel", "Koyu");
    }
}
