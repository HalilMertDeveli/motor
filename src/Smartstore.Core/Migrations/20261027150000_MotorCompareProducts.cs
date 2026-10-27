using FluentMigrator;
using Smartstore.Core.Configuration;
using Smartstore.Core.Installation;
using Smartstore.Core.Localization;
using Smartstore.Data.Migrations;

namespace Smartstore.Core.Data.Migrations;

/// <summary>
/// MOTOR: richer compare page. The MotorDark compare view builds its feature matrix from the bulleted full
/// descriptions of the 2026 catalog, which Smartstore only maps when
/// <c>CatalogSettings.IncludeFullDescriptionInCompareProducts</c> is on. Turns that setting on in EXISTING databases
/// while it still has Smartstore's default value (fresh Turkish installs get it from <see cref="TrTRSeedData"/>)
/// and adds the texts of the page.
/// </summary>
[MigrationVersion("2026-10-27 15:00:00", "MOTOR: compare products")]
internal class MotorCompareProducts : Migration, ILocaleResourcesProvider, IDataSeeder<SmartDbContext>
{
    const string SettingName = "CatalogSettings.IncludeFullDescriptionInCompareProducts";

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
        var rows = await context.Settings
            .Where(x => x.Name == SettingName)
            .ToListAsync(cancelToken);

        if (rows.Count == 0)
        {
            // Never saved: the class default applies to all stores.
            context.Settings.Add(new Setting { Name = SettingName, Value = "True", StoreId = 0 });
        }
        else
        {
            rows.Where(x => x.Value.IsEmpty() || x.Value.EqualsNoCase("False")).Each(x => x.Value = "True");
        }

        await context.SaveChangesAsync(cancelToken);
        await context.MigrateLocaleResourcesAsync(MigrateLocaleResources);
    }

    public void MigrateLocaleResources(LocaleResourcesBuilder builder)
    {
        void Add(string key, string en, string tr)
            => builder.AddOrUpdate("Themes.MotorDark.Compare." + key).Value(en).Value("tr", tr);

        Add("Count", "Products compared: {0} / {1}", "Karşılaştırılan ürün: {0} / {1}");
        Add("OnlyDifferences", "Show differences only", "Sadece farkları göster");
        Add("NoDifferences",
            "These products do not differ in the details listed here.",
            "Bu ürünler burada listelenen bilgilerde birbirinden farklı değil.");

        Add("Group.General", "Overview", "Genel bakış");
        Add("Group.Specifications", "Specifications", "Teknik özellikler");
        Add("Group.Features", "Features", "Özellikler");
        Add("Highlights", "Highlights", "Öne çıkanlar");
        Add("FeaturesNote",
            "Taken from the product descriptions. A dash means the description does not mention it.",
            "Ürün açıklamalarından alınmıştır. Çizgi, açıklamada belirtilmediğini gösterir.");
        Add("Listed", "Listed", "Var");
        Add("NotListed", "Not listed", "Belirtilmemiş");
        Add("Delivery", "Delivery", "Teslimat");
        Add("Rating", "Rating", "Değerlendirme");

        Add("BestPrice", "Best price", "En uygun fiyat");
        Add("PriceDifference", "{0} more than the best price", "En uygun fiyattan {0} fazla");

        Add("AddProduct", "Add a product", "Ürün ekle");
        Add("AddProductText",
            "Use the compare button on a product page. Room for {0} more.",
            "Ürün sayfasındaki karşılaştır düğmesini kullan. {0} ürün daha ekleyebilirsin.");

        Add("EmptyTitle", "Nothing to compare yet", "Karşılaştırma listen boş");
        Add("EmptyText",
            "Add products with the compare button on a product page to see their features side by side.",
            "Ürün sayfasındaki karşılaştır düğmesiyle ürün ekle; özelliklerini burada yan yana gör.");
    }
}
