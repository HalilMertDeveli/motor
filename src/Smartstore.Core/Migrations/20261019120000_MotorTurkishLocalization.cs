using System.Xml;
using System.Xml.Linq;
using FluentMigrator;
using Smartstore.Caching;
using Smartstore.Core.Common;
using Smartstore.Core.Configuration;
using Smartstore.Core.Content.Menus;
using Smartstore.Core.DataExchange;
using Smartstore.Core.Installation;
using Smartstore.Core.Localization;
using Smartstore.Core.Messaging;
using Smartstore.Data.Migrations;
using Smartstore.Engine.Modularity;
using Smartstore.IO;

namespace Smartstore.Core.Data.Migrations;

/// <summary>
/// MOTOR: Turkish localization for EXISTING databases. Fresh installs get the same data from
/// <see cref="TrTRSeedData"/> (this seeder does not run on fresh installs, only its locale resources do).
///
/// Additive and idempotent: it never overwrites existing or admin-edited data. It adds the tr-TR language
/// and its resources, Turkish message template translations, Turkey's provinces and the lira display format.
/// It deliberately does NOT change the primary currency, tax settings or tax rates, because that would
/// change the meaning of stored prices. See docs/motor/turkish-localization.md for the admin steps.
/// </summary>
[MigrationVersion("2026-10-19 12:00:00", "MOTOR: Turkish localization")]
internal class MotorTurkishLocalization : Migration, ILocaleResourcesProvider, IDataSeeder<SmartDbContext>
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
        var appContext = EngineContext.Current.Application;

        var (language, created) = await EnsureLanguageAsync(context, cancelToken);
        await ImportResourcesAsync(context, appContext, language, cancelToken);
        await context.MigrateLocaleResourcesAsync(MigrateLocaleResources);
        await AddMessageTemplateTranslationsAsync(context, appContext, language, cancelToken);
        await AddTurkeyDataAsync(context, language, cancelToken);
        await AddMenuTranslationsAsync(context, language, cancelToken);
        await ActivateMotorDarkThemeAsync(context, appContext, cancelToken);

        if (created)
        {
            Logger.Info($"Added language '{TurkishStoreDefaults.Culture}' as the first storefront language.");
        }
    }

    private static ILogger Logger => EngineContext.Current.Application.Logger;

    private static readonly HashSet<string> TrackingOwnedTemplates = new(StringComparer.OrdinalIgnoreCase)
    {
        "ShipmentSent.CustomerNotification",
        "ShipmentDelivered.CustomerNotification"
    };

    private static async Task<(Language Language, bool Created)> EnsureLanguageAsync(SmartDbContext db, CancellationToken cancelToken)
    {
        var language = await db.Languages
            .Include(x => x.LocaleStringResources)
            .FirstOrDefaultAsync(x => x.LanguageCulture == TurkishStoreDefaults.Culture, cancelToken);

        if (language != null)
        {
            return (language, false);
        }

        // A new Turkish language becomes the first one, i.e. the default storefront language of stores
        // without an explicit default language. Admin users keep their admin language (DefaultAdminLanguageId).
        var minDisplayOrder = await db.Languages.MinAsync(x => (int?)x.DisplayOrder, cancelToken) ?? 0;

        language = new Language
        {
            Name = TurkishStoreDefaults.LanguageName,
            LanguageCulture = TurkishStoreDefaults.Culture,
            UniqueSeoCode = TurkishStoreDefaults.UniqueSeoCode,
            FlagImageFileName = TurkishStoreDefaults.FlagImageFileName,
            Published = true,
            DisplayOrder = minDisplayOrder - 1
        };

        db.Languages.Add(language);
        await db.SaveChangesAsync(cancelToken);

        return (language, true);
    }

    private static async Task ImportResourcesAsync(SmartDbContext db, IApplicationContext appContext, Language language, CancellationToken cancelToken)
    {
        // Same construction as the installation seeder: no caches or services are needed here.
        var resourceManager = new XmlResourceManager(db, NullRequestCache.Instance, NullCache.Instance, null, null, Logger, null, null, appContext, null);

        // Application resources. Insert only: existing (possibly edited) Turkish resources are kept.
        var locDir = appContext.AppDataRoot.GetDirectory("Localization/App/" + TurkishStoreDefaults.UniqueSeoCode);
        if (locDir.Exists)
        {
            foreach (var file in locDir.EnumerateFiles("*.smres.xml"))
            {
                cancelToken.ThrowIfCancellationRequested();

                var doc = new XmlDocument();
                doc.Load(file.PhysicalPath);
                doc = resourceManager.FlattenResourceFile(doc);

                await resourceManager.ImportResourcesFromXmlAsync(language, doc, mode: ImportModeFlags.Insert);
            }
        }

        // Module resources (resources.tr-tr.xml), or the English fallback the module system uses otherwise.
        var filterLanguages = new List<Language> { language };
        foreach (var module in appContext.ModuleCatalog.GetInstalledModules())
        {
            cancelToken.ThrowIfCancellationRequested();
            await resourceManager.ImportModuleResourcesFromXmlAsync(module, null, false, filterLanguages);
        }

        db.DetachEntities<LocaleStringResource>();
    }

    private static async Task AddMessageTemplateTranslationsAsync(SmartDbContext db, IApplicationContext appContext, Language language, CancellationToken cancelToken)
    {
        var dir = appContext.ContentRoot.GetDirectory("App_Data/EmailTemplates/" + TurkishStoreDefaults.UniqueSeoCode);
        if (!dir.Exists)
        {
            return;
        }

        var templates = await db.MessageTemplates.ToListAsync(cancelToken);
        if (templates.Count == 0)
        {
            return;
        }

        var templateIds = templates.Select(x => x.Id).ToArray();
        var existingKeys = (await db.LocalizedProperties
            .Where(x => x.LocaleKeyGroup == nameof(MessageTemplate) && x.LanguageId == language.Id && templateIds.Contains(x.EntityId))
            .Select(x => new { x.EntityId, x.LocaleKey })
            .ToListAsync(cancelToken))
            .Select(x => $"{x.EntityId}:{x.LocaleKey}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var file in dir.EnumerateFiles("*.xml"))
        {
            // The MotorShop.Tracking module owns the Turkish shipment notifications (same "only if empty" rule).
            if (TrackingOwnedTemplates.Contains(file.NameWithoutExtension))
            {
                continue;
            }

            XDocument doc;
            using (var stream = file.OpenRead())
            {
                doc = XDocument.Load(stream);
            }

            var subject = doc.Root?.Element("Subject")?.Value;
            var body = doc.Root?.Element("Body")?.Value;

            foreach (var template in templates.Where(x => x.Name.EqualsNoCase(file.NameWithoutExtension)))
            {
                AddProperty(template.Id, nameof(MessageTemplate.Subject), subject);
                AddProperty(template.Id, nameof(MessageTemplate.Body), body);
            }
        }

        await db.SaveChangesAsync(cancelToken);

        void AddProperty(int entityId, string key, string value)
        {
            if (value.HasValue() && existingKeys.Add($"{entityId}:{key}"))
            {
                db.LocalizedProperties.Add(new LocalizedProperty
                {
                    EntityId = entityId,
                    LanguageId = language.Id,
                    LocaleKeyGroup = nameof(MessageTemplate),
                    LocaleKey = key,
                    LocaleValue = value
                });
            }
        }
    }

    /// <summary>
    /// The invariant seed stores two menu titles in English. Adds Turkish translations where none exist.
    /// </summary>
    private static async Task AddMenuTranslationsAsync(SmartDbContext db, Language language, CancellationToken cancelToken)
    {
        var serviceMenus = await db.Menus
            .Where(x => x.SystemName == "HelpAndService" && x.Title == TurkishStoreDefaults.ServiceMenuTitleEn)
            .Select(x => x.Id)
            .ToListAsync(cancelToken);

        var cookieItems = await db.MenuItems
            .Where(x => x.Title == TurkishStoreDefaults.CookieManagerMenuTitleEn)
            .Select(x => x.Id)
            .ToListAsync(cancelToken);

        await AddTitlesAsync("MenuRecord", serviceMenus, TurkishStoreDefaults.ServiceMenuTitle);
        await AddTitlesAsync("MenuItemRecord", cookieItems, TurkishStoreDefaults.CookieManagerMenuTitle);
        await db.SaveChangesAsync(cancelToken);

        async Task AddTitlesAsync(string keyGroup, List<int> ids, string title)
        {
            var existing = await db.LocalizedProperties
                .Where(x => x.LocaleKeyGroup == keyGroup && x.LocaleKey == "Title" && x.LanguageId == language.Id && ids.Contains(x.EntityId))
                .Select(x => x.EntityId)
                .ToListAsync(cancelToken);

            foreach (var id in ids.Except(existing))
            {
                db.LocalizedProperties.Add(new LocalizedProperty
                {
                    EntityId = id,
                    LanguageId = language.Id,
                    LocaleKeyGroup = keyGroup,
                    LocaleKey = "Title",
                    LocaleValue = title
                });
            }
        }
    }

    private static async Task AddTurkeyDataAsync(SmartDbContext db, Language language, CancellationToken cancelToken)
    {
        var turkey = await db.Countries
            .Include(x => x.StateProvinces)
            .FirstOrDefaultAsync(x => x.TwoLetterIsoCode == "TR", cancelToken);

        if (turkey != null)
        {
            // Provinces only if none exist, so admin-maintained lists are never touched.
            if (turkey.StateProvinces.Count == 0)
            {
                turkey.StateProvinces.AddRange(TurkishStoreDefaults.CreateProvinces());
            }

            if (turkey.AddressFormat.IsEmpty())
            {
                turkey.AddressFormat = TurkishStoreDefaults.AddressFormat;
            }

            var hasLocalizedName = await db.LocalizedProperties.AnyAsync(x =>
                x.LocaleKeyGroup == nameof(Country) &&
                x.LocaleKey == nameof(Country.Name) &&
                x.EntityId == turkey.Id &&
                x.LanguageId == language.Id, cancelToken);

            if (!hasLocalizedName)
            {
                db.LocalizedProperties.Add(new LocalizedProperty
                {
                    EntityId = turkey.Id,
                    LanguageId = language.Id,
                    LocaleKeyGroup = nameof(Country),
                    LocaleKey = nameof(Country.Name),
                    LocaleValue = "Türkiye"
                });
            }
        }

        // Display format only. Rate, publishing and the primary currency stay untouched.
        var lira = await db.Currencies.FirstOrDefaultAsync(x => x.CurrencyCode == TurkishStoreDefaults.CurrencyCode, cancelToken);
        if (lira != null && lira.CustomFormatting.IsEmpty())
        {
            lira.CustomFormatting = TurkishStoreDefaults.CurrencyFormatting;
        }

        await db.SaveChangesAsync(cancelToken);
    }

    /// <summary>
    /// Makes MotorDark the storefront theme where the theme is still Smartstore's default "Flex".
    /// A theme an admin chose on purpose (any other value, globally or per store) is kept.
    /// </summary>
    private static async Task ActivateMotorDarkThemeAsync(SmartDbContext db, IApplicationContext appContext, CancellationToken cancelToken)
    {
        const string settingName = "ThemeSettings.DefaultTheme";
        const string smartstoreDefaultTheme = "Flex";
        var theme = InvariantSeedData.MotorDefaultTheme;

        if (!appContext.ContentRoot.FileExists($"Themes/{theme}/theme.config"))
        {
            return;
        }

        var settings = await db.Settings
            .Where(x => x.Name == settingName)
            .ToListAsync(cancelToken);

        foreach (var setting in settings.Where(x => x.Value.IsEmpty() || x.Value.EqualsNoCase(smartstoreDefaultTheme)))
        {
            setting.Value = theme;
        }

        if (settings.Count == 0)
        {
            // Never saved: the class default "Flex" applies to all stores.
            db.Settings.Add(new Setting { Name = settingName, Value = theme, StoreId = 0 });
        }

        await db.SaveChangesAsync(cancelToken);
    }

    public void MigrateLocaleResources(LocaleResourcesBuilder builder)
    {
        // MotorDark theme texts. English is the fallback for every other language.
        void Add(string key, string en, string tr)
            => builder.AddOrUpdate("Themes.MotorDark." + key).Value(en).Value("tr", tr);

        Add("Hero.Eyebrow", "Parts · Maintenance · Gear", "Parça · Bakım · Ekipman");
        Add("Hero.TitleLine1", "Own", "Yola");
        Add("Hero.TitleLine2", "the", "hakim");
        Add("Hero.TitleAccent", "road.", "ol.");
        Add("Hero.Subtitle", "Parts and gear that complete your motorcycle's performance.", "Motorunun performansını tamamlayan parçalar ve ekipmanlar.");
        Add("Hero.ExploreProducts", "Explore Products", "Ürünleri Keşfet");
        Add("Hero.BrowseCategories", "Browse Categories", "Kategorilere Göz At");
        Add("Hero.ScrollToCategories", "Scroll down to the categories", "Kategorilere in");
        Add("Hero.Scroll", "Scroll", "Kaydır");

        Add("Categories.Eyebrow", "Categories", "Kategoriler");
        Add("Categories.Title", "Choose by your ride", "Sürüşüne göre seç");
        Add("Categories.Lead", "From engine parts to rider gear: everything you need in one garage.", "Motor parçasından sürücü ekipmanına, ihtiyacın olan her şey tek bir garajda.");
        Add("Categories.EmptyTitle", "Categories are being prepared", "Kategoriler hazırlanıyor");
        Add("Categories.EmptyText", "Categories will appear here once the catalog is published.", "Katalog yayına alındığında kategoriler burada görünecek.");
        Add("Categories.Explore", "Explore", "İncele");

        Add("Products.EmptyTitle", "Products coming soon", "Ürünler çok yakında");
        Add("Products.EmptyText", "The showcase is being prepared. Meanwhile, browse the categories or ask us about the part you need.", "Vitrin şu an hazırlanıyor. Bu sırada kategorilere göz atabilir ya da aradığın parçayı bize sorabilirsin.");
        Add("Products.BrowseCategories", "Browse categories", "Kategorilere göz at");
        Add("ContactUs", "Contact us", "Bize ulaş");

        Add("Featured.Eyebrow", "Featured", "Öne çıkanlar");
        Add("Featured.Title", "Popular products", "Popüler ürünler");
        Add("Bestsellers.Eyebrow", "Bestsellers", "Çok satanlar");
        Add("Bestsellers.Title", "Riders' choice", "Sürücülerin tercihi");
        Add("Brands.Eyebrow", "Brands", "Markalar");
        Add("Brands.Title", "Shop by brand", "Markana göre alışveriş");
        Add("NewArrivals.Eyebrow", "New arrivals", "Yeni gelenler");
        Add("NewArrivals.Title", "New in the garage", "Garaja yeni girenler");
        Add("NewArrivals.ViewAll", "View all", "Tümünü gör");

        Add("Story.Eyebrow", "From garage to road", "Garajdan yola");
        // Rendered as "{TitleStart} <em>{TitleAccent}</em> {TitleEnd}".
        Add("Story.TitleStart", "Every part is part of the", "Her parça, bir sonraki");
        Add("Story.TitleAccent", "ride", "virajın");
        Add("Story.TitleEnd", "ahead.", "parçası.");
        Add("Story.Lead",
            "A good ride starts with the right part. From routine maintenance to performance upgrades, from rider safety to touring accessories, we have sorted what you need into categories.",
            "İyi bir sürüş, doğru parçayla başlar. Bakım rutininden performans yükseltmesine, sürücü güvenliğinden yol aksesuarlarına kadar ihtiyacını kategorilere ayırdık.");
        Add("Story.ExploreCategories", "Explore Categories", "Kategorileri Keşfet");
        Add("Story.AskForPart", "Ask About a Part", "Parça Danış");
        Add("Story.Maintenance.Title", "Maintenance", "Bakım");
        Add("Story.Maintenance.Text", "Periodic maintenance parts such as oil, filters, spark plugs and brake pads.", "Yağ, filtre, buji ve balata gibi periyodik bakım parçaları.");
        Add("Story.Performance.Title", "Performance", "Performans");
        Add("Story.Performance.Text", "Exhaust, air and fuel systems, drivetrain and suspension parts.", "Egzoz, hava ve yakıt sistemleri, aktarma ve süspansiyon parçaları.");
        Add("Story.Rider.Title", "Rider", "Sürücü");
        Add("Story.Rider.Text", "Helmet, jacket, gloves and boots to stay visible and protected on the road.", "Kask, mont, eldiven ve bot ile yolda görünür ve korunaklı kal.");

        Add("Assurance.Label", "Shopping assurance", "Alışveriş güvencesi");
        Add("Assurance.Shipping.Title", "Shipping & Returns", "Kargo & İade");
        Add("Assurance.Shipping.Text", "Check the shipping, delivery and return terms before you order.", "Gönderim, teslimat ve iade koşullarını önceden öğren.");
        Add("Assurance.Shipping.Link", "View terms", "Koşulları incele");
        Add("Assurance.Payment.Title", "Payment Options", "Ödeme Seçenekleri");
        Add("Assurance.Payment.Text", "See the available payment methods and how payment works.", "Kullanılabilir ödeme yöntemlerini ve ödeme sürecini gör.");
        Add("Assurance.Payment.Link", "Payment info", "Ödeme bilgileri");
        Add("Assurance.Orders.Title", "Order Tracking", "Sipariş Takibi");
        Add("Assurance.Orders.Text", "Track the status of your orders in your account.", "Siparişlerinin durumunu hesabından takip et.");
        Add("Assurance.Orders.Link", "My orders", "Siparişlerim");
        Add("Assurance.Advice.Title", "Parts Advice", "Parça Danışmanlığı");
        Add("Assurance.Advice.Text", "Not sure which part fits? Write to us.", "Doğru parçadan emin değil misin? Bize yaz.");
        Add("Assurance.Advice.Link", "Get in touch", "İletişime geç");

        Add("Footer.ClaimLine1", "One step ahead", "Her virajda");
        Add("Footer.ClaimLine2", "in every corner.", "bir adım önde.");
        Add("Footer.Note",
            "Parts, maintenance and rider gear for your motorcycle. Can't find what you are looking for? Write to us and we will find the right part together.",
            "Motosikletin için parça, bakım ve sürücü ekipmanı. Aradığını bulamazsan bize yaz; doğru parçayı birlikte bulalım.");

        Add("Category.NoResultsTitle", "No results", "Sonuç bulunamadı");
        Add("Category.NoResultsText", "No products match the selected filters. Try changing or removing the filters.", "Seçili filtrelere uygun ürün yok. Filtreleri değiştirmeyi ya da kaldırmayı dene.");
        Add("Category.EmptyTitle", "No products in this category yet", "Bu kategoride henüz ürün yok");
        Add("Category.EmptyTextWithSubcategories", "Browse the subcategories or ask us about the part you need.", "Alt kategorilere göz atabilir ya da aradığın parçayı bize sorabilirsin.");
        Add("Category.EmptyText", "Products will be listed here once they are published. Ask us about the part you need.", "Ürünler yayına alındığında burada listelenecek. Aradığın parçayı bize sorabilirsin.");
        Add("Category.Homepage", "Home", "Ana sayfa");
        Add("Category.AskForPart", "Ask about a part", "Parça sor");

        // Read by the menu seed (InvariantSeedData.Menus) on fresh installs; it falls back to "Service" otherwise.
        builder.AddOrUpdate("Menu.ServiceMenu")
            .Value(TurkishStoreDefaults.ServiceMenuTitleEn)
            .Value("tr", TurkishStoreDefaults.ServiceMenuTitle);

        // Lenient phone number validation (FluentValidatorRuleExtensions.PhoneNumber).
        builder.AddOrUpdate("Address.Fields.PhoneNumber.Invalid")
            .Value("Please enter a valid phone number, e.g. +90 532 123 45 67.")
            .Value("de", "Bitte geben Sie eine gültige Telefonnummer ein, z. B. +49 30 1234567.")
            .Value("tr", "Lütfen geçerli bir telefon numarası girin, örn. +90 532 123 45 67 veya 0532 123 45 67.");
    }
}
