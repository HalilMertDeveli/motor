using FluentMigrator;
using Smartstore.Core.Content.Topics;
using Smartstore.Core.Installation;
using Smartstore.Core.Localization;
using Smartstore.Core.Seo;
using Smartstore.Data.Migrations;

namespace Smartstore.Core.Data.Migrations;

/// <summary>
/// MOTOR: store brand (first Prosev, now MRT Motors) and the category tree of the 2026 catalog for EXISTING databases.
/// Fresh installs get the same data from <see cref="TrTRSeedData"/> (this seeder does not run on fresh installs,
/// only its locale resources do).
///
/// Additive: a store name, sender name, meta title or payment info text is only replaced while it still has a
/// placeholder value, and existing categories are kept as they are.
/// </summary>
[MigrationVersion("2026-10-20 16:30:00", "MOTOR: Prosev store")]
internal class ProsevStore : Migration, ILocaleResourcesProvider, IDataSeeder<SmartDbContext>
{
    public override void Up()
    {
    }

    public override void Down()
    {
    }

    public DataSeederStage Stage => DataSeederStage.Early;
    public bool AbortOnFailure => false;

    public async Task SeedAsync(SmartDbContext context, CancellationToken cancelToken = default)
    {
        var placeholders = ProsevStoreDefaults.PlaceholderStoreNames;

        var stores = await context.Stores.ToListAsync(cancelToken);
        stores.Where(x => placeholders.Any(p => p.EqualsNoCase(x.Name?.Trim()))).Each(x => x.Name = ProsevStoreDefaults.StoreName);

        var accounts = await context.EmailAccounts.ToListAsync(cancelToken);
        accounts.Where(x => placeholders.Any(p => p.EqualsNoCase(x.DisplayName?.Trim()))).Each(x => x.DisplayName = ProsevStoreDefaults.StoreName);

        var metaTitles = await context.Settings.Where(x => x.Name == "SeoSettings.MetaTitle").ToListAsync(cancelToken);
        metaTitles.Where(x => x.Value.IsEmpty() || placeholders.Any(p => p.EqualsNoCase(x.Value.Trim()))).Each(x => x.Value = ProsevStoreDefaults.StoreName);

        await context.SaveChangesAsync(cancelToken);

        await UpdatePaymentInfoTopicAsync(context, cancelToken);
        await ProsevStoreDefaults.EnsureCategoriesAsync(context, cancelToken);
    }

    private static async Task UpdatePaymentInfoTopicAsync(SmartDbContext context, CancellationToken cancelToken)
    {
        var topic = await context.Topics.FirstOrDefaultAsync(x => x.SystemName == "PaymentInfo", cancelToken);
        if (topic == null)
        {
            return;
        }

        if (IsPlaceholder(topic.Body))
        {
            topic.Title = ProsevStoreDefaults.PaymentInfoTitle;
            topic.Body = ProsevStoreDefaults.PaymentInfoBody;
        }

        // A Turkish localized body that is still a placeholder would hide the new text.
        var localized = await context.LocalizedProperties
            .Where(x => x.LocaleKeyGroup == nameof(Topic) && x.EntityId == topic.Id && x.LocaleKey == nameof(Topic.Body))
            .ToListAsync(cancelToken);

        localized.Where(x => IsPlaceholder(x.LocaleValue)).Each(x => x.LocaleValue = ProsevStoreDefaults.PaymentInfoBody);

        await context.SaveChangesAsync(cancelToken);

        static bool IsPlaceholder(string body)
            => body.IsEmpty()
            || body.Contains("buraya ekleyin", StringComparison.OrdinalIgnoreCase)
            || body.Contains("Put your payment information here", StringComparison.OrdinalIgnoreCase);
    }

    public void MigrateLocaleResources(LocaleResourcesBuilder builder)
    {
        // MRT Motors wording for the MotorDark theme (catalog: helmets, rider gear, bags and accessories).
        // The store was renamed from Prosev on 2026-10-22: MrtMotorsRebrand applies these texts to existing databases.
        void Add(string key, string en, string tr)
            => builder.AddOrUpdate("Themes.MotorDark." + key).Value(en).Value("tr", tr);

        Add("Hero.Eyebrow", "2026 Catalog · Helmets · Gear · Bags", "2026 Katalog · Kask · Ekipman · Çanta");
        Add("Hero.TitleLine1", "Gear up", "Yola");
        Add("Hero.TitleLine2", "for the", "hazır");
        Add("Hero.TitleAccent", "road.", "ol.");
        Add("Hero.Subtitle",
            "Helmets, jackets, gloves, bags and accessories from the MRT Motors 2026 catalog.",
            "MRT Motors 2026 kataloğundan kask, mont, eldiven, çanta ve aksesuarlar.");
        // Rendered after a red "3" tag.
        Add("Hero.Installments", "installments on all credit cards", "taksit · tüm kredi kartlarına");

        Add("Categories.Title", "Shop the catalog", "Kataloğu keşfet");
        Add("Categories.Lead",
            "Every section of the 2026 catalog: from helmets and rider gear to bags, locks and accessories.",
            "2026 kataloğunun tüm bölümleri: kasktan sürücü ekipmanına, çantadan kilit ve aksesuara.");

        Add("Story.Eyebrow", "MRT Motors 2026", "MRT Motors 2026");
        Add("Story.TitleStart", "Protection for", "Her sürüşte");
        Add("Story.TitleAccent", "every", "güvenli");
        Add("Story.TitleEnd", "ride.", "koruma.");
        Add("Story.Lead",
            "Helmets with DOT and CE approval, four-season jackets, gloves, boots and protectors, plus bags and fittings for your motorcycle. Sorted by the sections of our catalog.",
            "DOT ve CE onaylı kasklar, dört mevsim montlar, eldiven, bot ve korumalar; motosikletine uygun çanta ve bağlantı kitleri. Hepsi katalog bölümlerine göre sıralı.");
        Add("Story.AskForPart", "Ask Us", "Bize Danış");
        Add("Story.Maintenance.Title", "Helmets", "Kask");
        Add("Story.Maintenance.Text", "Full face, modular, open face, cross and kids' helmets, visors and spare parts.", "Kapalı, çene açılır, açık, cross ve çocuk kaskları; kask camı ve yedek parçalar.");
        Add("Story.Performance.Title", "Bags & Fittings", "Çanta & Bağlantı");
        Add("Story.Performance.Text", "Top cases, side and tank bags, mounting racks and kits per motorcycle model.", "Arka, yan ve depo üstü çantalar; modele özel bağlantı demirleri ve kitler.");
        Add("Story.Rider.Title", "Rider Gear", "Sürücü Ekipmanı");
        Add("Story.Rider.Text", "Jackets, trousers, rain gear, gloves, boots and protectors.", "Mont, pantolon, yağmurluk, eldiven, bot ve koruma ekipmanları.");

        Add("Assurance.Payment.Text", "3 installments on all credit cards, bank transfer or cash on delivery.", "Tüm kredi kartlarına 3 taksit, havale/EFT veya kapıda ödeme.");
        Add("Assurance.Advice.Title", "Product Advice", "Ürün Danışmanlığı");
        Add("Assurance.Advice.Text", "Not sure about size or fitment? Write to us.", "Beden ya da uyumluluk konusunda emin değil misin? Bize yaz.");

        Add("Footer.ClaimLine1", "Ready for", "Her sürüşe");
        Add("Footer.ClaimLine2", "every ride.", "hazır.");
        Add("Footer.Note",
            "MRT Motors: helmets, rider gear, bags and accessories for your motorcycle. Can't find what you are looking for? Write to us.",
            "MRT Motors: motosikletin için kask, sürücü ekipmanı, çanta ve aksesuar. Aradığını bulamazsan bize yaz.");
    }
}
