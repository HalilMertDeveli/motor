using Smartstore.Core.Common;

namespace Smartstore.Core.Installation;

/// <summary>
/// MOTOR: Turkey specific default data shared by <see cref="TrTRSeedData"/> (fresh installs)
/// and the MotorTurkishLocalization migration (existing databases).
/// </summary>
public static class TurkishStoreDefaults
{
    public const string Culture = "tr-TR";
    public const string LanguageName = "Türkçe";
    public const string UniqueSeoCode = "tr";
    public const string FlagImageFileName = "tr.png";

    public const string CurrencyCode = "TRY";
    public const string CurrencyName = "Türk Lirası";

    /// <summary>
    /// Renders amounts as "1.234,56 ₺" (number separators come from the tr-TR display locale).
    /// </summary>
    public const string CurrencyFormatting = "#,##0.00 ₺";

    public const string TimeZoneId = "Europe/Istanbul";

    // Menu titles the invariant seed hardcodes in English.
    public const string ServiceMenuTitleEn = "Service";
    public const string ServiceMenuTitle = "Yardım";
    public const string CookieManagerMenuTitleEn = "Cookie Manager";
    public const string CookieManagerMenuTitle = "Çerez ayarları";

    /// <summary>
    /// Default KDV categories and rates for fresh installs. Rates are stored as editable settings
    /// of the fixed-rate tax provider (Admin > Configuration > Tax > Providers > Fixed rate).
    /// NEEDS OWNER/ACCOUNTANT CONFIRMATION before go-live.
    /// </summary>
    public static readonly (string Name, decimal Rate)[] TaxCategories =
    [
        ("Genel KDV", 20M),
        ("İndirimli KDV I", 10M),
        ("İndirimli KDV II", 1M),
        ("KDV'den muaf", 0M)
    ];

    /// <summary>
    /// Measure ratios for the Turkish base units (kilogram, centimetre). Smartstore stores how many of a unit make one
    /// base unit, so the base unit must be 1; the invariant seed is based on pound and inch, which made
    /// Admin > System > Warnings report "Varsayılan ağırlık/boyut birimi. Oran 1 olarak ayarlanmalıdır."
    /// </summary>
    public static readonly (string Keyword, decimal Ratio)[] WeightRatios =
    [
        ("kg", 1M),
        ("g", 1000M),
        ("lb", 2.20462262M),
        ("oz", 35.27396195M),
        ("l", 1M),
        ("ml", 1000M)
    ];

    /// <inheritdoc cref="WeightRatios"/>
    public static readonly (string Keyword, decimal Ratio)[] DimensionRatios =
    [
        ("cm", 1M),
        ("mm", 10M),
        ("m", 0.01M),
        ("inch", 0.39370079M),
        ("ft", 0.0328084M)
    ];

    /// <summary>
    /// The 81 provinces (il) of Turkey with their official plate code, in Turkish alphabetical order.
    /// </summary>
    public static readonly (string Code, string Name)[] Provinces =
    [
        ("01", "Adana"),
        ("02", "Adıyaman"),
        ("03", "Afyonkarahisar"),
        ("04", "Ağrı"),
        ("68", "Aksaray"),
        ("05", "Amasya"),
        ("06", "Ankara"),
        ("07", "Antalya"),
        ("75", "Ardahan"),
        ("08", "Artvin"),
        ("09", "Aydın"),
        ("10", "Balıkesir"),
        ("74", "Bartın"),
        ("72", "Batman"),
        ("69", "Bayburt"),
        ("11", "Bilecik"),
        ("12", "Bingöl"),
        ("13", "Bitlis"),
        ("14", "Bolu"),
        ("15", "Burdur"),
        ("16", "Bursa"),
        ("17", "Çanakkale"),
        ("18", "Çankırı"),
        ("19", "Çorum"),
        ("20", "Denizli"),
        ("21", "Diyarbakır"),
        ("81", "Düzce"),
        ("22", "Edirne"),
        ("23", "Elazığ"),
        ("24", "Erzincan"),
        ("25", "Erzurum"),
        ("26", "Eskişehir"),
        ("27", "Gaziantep"),
        ("28", "Giresun"),
        ("29", "Gümüşhane"),
        ("30", "Hakkâri"),
        ("31", "Hatay"),
        ("76", "Iğdır"),
        ("32", "Isparta"),
        ("34", "İstanbul"),
        ("35", "İzmir"),
        ("46", "Kahramanmaraş"),
        ("78", "Karabük"),
        ("70", "Karaman"),
        ("36", "Kars"),
        ("37", "Kastamonu"),
        ("38", "Kayseri"),
        ("71", "Kırıkkale"),
        ("39", "Kırklareli"),
        ("40", "Kırşehir"),
        ("79", "Kilis"),
        ("41", "Kocaeli"),
        ("42", "Konya"),
        ("43", "Kütahya"),
        ("44", "Malatya"),
        ("45", "Manisa"),
        ("47", "Mardin"),
        ("33", "Mersin"),
        ("48", "Muğla"),
        ("49", "Muş"),
        ("50", "Nevşehir"),
        ("51", "Niğde"),
        ("52", "Ordu"),
        ("80", "Osmaniye"),
        ("53", "Rize"),
        ("54", "Sakarya"),
        ("55", "Samsun"),
        ("56", "Siirt"),
        ("57", "Sinop"),
        ("58", "Sivas"),
        ("63", "Şanlıurfa"),
        ("73", "Şırnak"),
        ("59", "Tekirdağ"),
        ("60", "Tokat"),
        ("61", "Trabzon"),
        ("62", "Tunceli"),
        ("64", "Uşak"),
        ("65", "Van"),
        ("77", "Yalova"),
        ("66", "Yozgat"),
        ("67", "Zonguldak")
    ];

    /// <summary>
    /// Turkish postal address layout: district (City) and province (StateProvince) on one line.
    /// </summary>
    public const string AddressFormat = @"{{ Company }}
{{ FirstName }} {{ LastName }}
{{ Street1 }}
{{ Street2 }}
{{ ZipCode }} {{ City }}{% if State %} / {{ State }}{% endif %}
{{ Country | Upcase }}";

    /// <summary>
    /// Creates the province entities for Turkey. Display order follows the Turkish alphabet,
    /// because database collations usually do not sort Ç, Ğ, İ, Ö, Ş, Ü correctly.
    /// </summary>
    public static List<StateProvince> CreateProvinces()
    {
        return Provinces
            .Select((x, i) => new StateProvince
            {
                Name = x.Name,
                Abbreviation = x.Code,
                Published = true,
                DisplayOrder = i
            })
            .ToList();
    }
}
