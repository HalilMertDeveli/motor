using FluentMigrator;
using Smartstore.Core.Configuration;
using Smartstore.Core.Installation;
using Smartstore.Core.Localization;
using Smartstore.Data.Migrations;

namespace Smartstore.Core.Data.Migrations;

/// <summary>
/// MOTOR: the store is renamed from Prosev to MRT Motors (owner decision 2026-10-22) in EXISTING databases,
/// with its address, phone and Instagram account in the company/contact/social settings and the contact pages.
/// Fresh installs get the new name from <see cref="TrTRSeedData"/> and the updated texts of the earlier migrations.
///
/// Additive and idempotent: a value is only replaced while it is still the old Prosev default, so names an admin
/// chose and edited (touched) resources are kept. "Prosev" as the product manufacturer of the Motosev catalog
/// (products, manufacturer, URL slugs) is supplier data and is not touched.
/// </summary>
[MigrationVersion("2026-10-22 12:00:00", "MOTOR: MRT Motors rebrand")]
internal class MrtMotorsRebrand : Migration, ILocaleResourcesProvider, IDataSeeder<SmartDbContext>
{
    const string OldStoreName = "Prosev";
    const string CompanyPlaceholderSuffix = " [firma unvanı]";
    const string PlaceholderStreet = "Örnek Mah. Örnek Sok. No: 1";
    const string PlaceholderPhone = "+90 212 000 00 00";

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
        var newName = ProsevStoreDefaults.StoreName;

        var stores = await context.Stores.ToListAsync(cancelToken);
        stores.Where(x => IsOldName(x.Name)).Each(x => x.Name = newName);

        var accounts = await context.EmailAccounts.ToListAsync(cancelToken);
        accounts.Where(x => IsOldName(x.DisplayName)).Each(x => x.DisplayName = newName);

        var settings = await context.Settings
            .Where(x => x.Name == "SeoSettings.MetaTitle" || x.Name == "CompanyInformationSettings.CompanyName")
            .ToListAsync(cancelToken);
        settings.Where(x => IsOldName(x.Value)).Each(x => x.Value = newName);
        settings.Where(x => IsOldCompany(x.Value)).Each(x => x.Value = newName + CompanyPlaceholderSuffix);

        // Placeholder company and address of the seeded store admin address.
        var oldCompany = OldStoreName + CompanyPlaceholderSuffix;
        var addresses = await context.Addresses.Where(x => x.Company == oldCompany).ToListAsync(cancelToken);
        foreach (var address in addresses)
        {
            address.Company = newName + CompanyPlaceholderSuffix;

            if (address.Address1 == PlaceholderStreet)
            {
                address.Address1 = ProsevStoreDefaults.Street;
                address.Address2 = ProsevStoreDefaults.Street2;
                address.City = ProsevStoreDefaults.District;
                address.ZipPostalCode = null;
            }

            if (address.PhoneNumber == PlaceholderPhone)
            {
                address.PhoneNumber = ProsevStoreDefaults.Phone;
            }
        }

        await context.SaveChangesAsync(cancelToken);

        await UpdateContactSettingsAsync(context, cancelToken);
        await UpdateContactTopicsAsync(context, cancelToken);

        await context.MigrateLocaleResourcesAsync(MigrateLocaleResources);

        static bool IsOldName(string value)
            => OldStoreName.EqualsNoCase(value?.Trim());

        static bool IsOldCompany(string value)
            => (OldStoreName + CompanyPlaceholderSuffix).EqualsNoCase(value?.Trim());
    }

    /// <summary>
    /// Store address, phone and Instagram in the company, contact and social settings. They feed the footer,
    /// the seller block of the legal documents, e-mails, PDF documents and the Organization JSON-LD.
    /// Only empty values (and "#" link placeholders) are filled.
    /// </summary>
    private static async Task UpdateContactSettingsAsync(SmartDbContext context, CancellationToken cancelToken)
    {
        var settings = await context.Settings
            .Where(x => x.StoreId == 0 && (x.Name.StartsWith("CompanyInformationSettings.") || x.Name.StartsWith("ContactDataSettings.") || x.Name.StartsWith("SocialSettings.")))
            .ToListAsync(cancelToken);

        string Get(string name)
            => settings.FirstOrDefault(x => x.Name.EqualsNoCase(name))?.Value;

        void Set(string name, string value, Func<string, bool> replace)
        {
            var setting = settings.FirstOrDefault(x => x.Name.EqualsNoCase(name));
            if (setting == null)
            {
                context.Settings.Add(new Setting { Name = name, Value = value, StoreId = 0 });
            }
            else if (replace(setting.Value))
            {
                setting.Value = value;
            }
        }

        // The address is one unit: fill it only while no street has been entered.
        if (Get("CompanyInformationSettings.Street").IsEmpty())
        {
            Set("CompanyInformationSettings.Street", ProsevStoreDefaults.Street, _ => true);
            Set("CompanyInformationSettings.Street2", ProsevStoreDefaults.Street2, _ => true);
            Set("CompanyInformationSettings.City", ProsevStoreDefaults.City, _ => true);
            Set("CompanyInformationSettings.StateName", ProsevStoreDefaults.StateName, _ => true);

            var turkey = await context.Countries.FirstOrDefaultAsync(x => x.ThreeLetterIsoCode == "TUR", cancelToken);
            if (turkey != null)
            {
                Set("CompanyInformationSettings.CountryId", turkey.Id.ToStringInvariant(), x => x.IsEmpty() || x == "0");
            }
        }

        Set("ContactDataSettings.CompanyTelephoneNumber", ProsevStoreDefaults.Phone, x => x.IsEmpty());
        Set("SocialSettings.InstagramLink", ProsevStoreDefaults.InstagramUrl, IsEmptyLink);

        // MRT Motors has no other social accounts: "#" placeholders render dead icons in the footer.
        // Empty, not null: a null value falls back to the "#" default of SocialSettings.
        foreach (var name in new[] { "SocialSettings.FacebookLink", "SocialSettings.TwitterLink", "SocialSettings.YoutubeLink", "SocialSettings.TikTokLink" })
        {
            settings.Where(x => x.Name.EqualsNoCase(name) && x.Value?.Trim() == "#").Each(x => x.Value = string.Empty);
        }

        await context.SaveChangesAsync(cancelToken);

        static bool IsEmptyLink(string value)
            => value.IsEmpty() || value.Trim() == "#";
    }

    /// <summary>
    /// Contact, about us and imprint pages get the MRT Motors contact data while they still have the install text.
    /// </summary>
    private static async Task UpdateContactTopicsAsync(SmartDbContext context, CancellationToken cancelToken)
    {
        var bodies = new Dictionary<string, string>
        {
            ["ContactUs"] = ProsevStoreDefaults.ContactUsBody,
            ["AboutUs"] = ProsevStoreDefaults.AboutUsBody,
            ["Imprint"] = ProsevStoreDefaults.ImprintBody
        };

        var systemNames = bodies.Keys.ToArray();
        var topics = await context.Topics.Where(x => systemNames.Contains(x.SystemName)).ToListAsync(cancelToken);

        topics
            .Where(x => ProsevStoreDefaults.IsPlaceholderTopicBody(x.Body))
            .Each(x => x.Body = bodies[x.SystemName]);

        await context.SaveChangesAsync(cancelToken);
    }

    public void MigrateLocaleResources(LocaleResourcesBuilder builder)
    {
        // Storefront and admin texts that contain the store name; untouched resources get the new wording.
        new ProsevStore().MigrateLocaleResources(builder);
        new ProsevAdminBranding().MigrateLocaleResources(builder);

        // Module resources are only imported on module install, so existing databases keep the old name otherwise.
        builder.AddOrUpdate("Plugins.FriendlyName.MotorShop.PaymentTerms")
            .Value("MRT Motors payment terms (dealer term payment)")
            .Value("tr", "MRT Motors ödeme koşulları (bayi vadeli ödeme)");

        builder.AddOrUpdate("Plugins.Description.MotorShop.PaymentTerms")
            .Value("Payment terms of the MRT Motors 2026 catalog: dealer customer role, dealer term payment (cash within 10 days or 30/60/90 days), installment and dealer terms box in checkout, bank transfer and cash on delivery descriptions.")
            .Value("tr", "MRT Motors 2026 kataloğunun ödeme koşulları: Bayi müşteri rolü, bayiler için vadeli ödeme (peşin 10 gün veya 30/60/90 gün), ödeme adımında taksit ve bayi koşulları kutusu, havale/EFT ve kapıda ödeme açıklamaları.");
    }
}
