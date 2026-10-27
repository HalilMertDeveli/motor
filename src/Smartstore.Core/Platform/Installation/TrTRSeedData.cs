using Smartstore.Core.Catalog;
using Smartstore.Core.Catalog.Brands;
using Smartstore.Core.Catalog.Categories;
using Smartstore.Core.Catalog.Pricing;
using Smartstore.Core.Catalog.Products;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Checkout.Shipping;
using Smartstore.Core.Checkout.Tax;
using Smartstore.Core.Common;
using Smartstore.Core.Common.Configuration;
using Smartstore.Core.Configuration;
using Smartstore.Core.Data.Migrations;
using Smartstore.Core.Content.Menus;
using Smartstore.Core.Content.Topics;
using Smartstore.Core.Identity;
using Smartstore.Core.Localization;
using Smartstore.Core.Logging;
using Smartstore.Core.Messaging;
using Smartstore.Core.Seo;
using Smartstore.Core.Stores;
using Smartstore.Scheduling;

namespace Smartstore.Core.Installation;

/// <summary>
/// MOTOR: seed data for a fresh installation with Turkish (tr-TR) as primary language.
/// Turkish lira is the primary currency, prices are entered and shown including KDV,
/// and Turkey (with its 81 provinces) is the default and only published country.
/// Sample catalog data stays English (it is demo content only).
/// </summary>
public class TrTRSeedData : InvariantSeedData
{
    protected override string TaxNameBooks => TurkishStoreDefaults.TaxCategories[1].Name;
    protected override string TaxNameDigitalGoods => TurkishStoreDefaults.TaxCategories[0].Name;
    protected override string TaxNameJewelry => TurkishStoreDefaults.TaxCategories[0].Name;
    protected override string TaxNameApparel => TurkishStoreDefaults.TaxCategories[0].Name;
    protected override string TaxNameFood => TurkishStoreDefaults.TaxCategories[2].Name;
    protected override string TaxNameElectronics => TurkishStoreDefaults.TaxCategories[0].Name;
    protected override string TaxNameTaxFree => TurkishStoreDefaults.TaxCategories[3].Name;

    // Applied to the fixed-rate tax provider in tax category display order.
    public override decimal[] FixedTaxRates => TurkishStoreDefaults.TaxCategories.Select(x => x.Rate).ToArray();

    /// <summary>
    /// Category tree of the 2026 catalog. Published, without products (products are imported unpublished).
    /// </summary>
    public override Task SeedStoreCatalogAsync()
        => ProsevStoreDefaults.EnsureCategoriesAsync(DbContext);

    protected override void Alter(IList<TaxCategory> entities)
    {
        base.Alter(entities);

        entities.Clear();

        var i = 0;
        foreach (var (name, _) in TurkishStoreDefaults.TaxCategories)
        {
            entities.Add(new TaxCategory { Name = name, DisplayOrder = i++ });
        }
    }

    protected override void Alter(IList<Currency> entities)
    {
        base.Alter(entities);

        var lira = entities.FirstOrDefault(x => x.CurrencyCode == TurkishStoreDefaults.CurrencyCode);
        if (lira == null)
        {
            return;
        }

        // The seeded rates are relative to USD. Rebase them on the lira, so that it can be the
        // primary exchange rate currency (rate 1). Other currencies stay unpublished; their rates
        // are outdated sample values until an exchange rate provider updates them.
        var liraRate = lira.Rate;
        foreach (var currency in entities)
        {
            currency.Published = false;
            currency.Rate = Math.Round(currency.Rate / liraRate, 6);

            if (TurkishSystemNames.Currencies.TryGetValue(currency.CurrencyCode, out var name))
            {
                currency.Name = name.Tr;
            }
        }

        lira.Name = TurkishStoreDefaults.CurrencyName;
        lira.Rate = 1M;
        lira.Published = true;
        lira.DisplayOrder = -10;
        lira.CustomFormatting = TurkishStoreDefaults.CurrencyFormatting;
        lira.RoundNumDecimals = 2;
        lira.MidpointRounding = CurrencyMidpointRounding.AwayFromZero;
    }

    protected override void Alter(IList<Store> entities)
    {
        base.Alter(entities);

        var liraId = DbContext.Currencies.FirstOrDefault(x => x.CurrencyCode == TurkishStoreDefaults.CurrencyCode)?.Id;

        entities.WithKey(x => x.DisplayOrder)
            .Alter(1, x =>
            {
                x.Name = ProsevStoreDefaults.StoreName;
                if (liraId.HasValue)
                {
                    x.DefaultCurrencyId = liraId.Value;
                    x.PrimaryExchangeRateCurrencyId = liraId.Value;
                }
            });
    }

    protected override void Alter(IList<Country> entities)
    {
        base.Alter(entities);

        // Domestic shop: only Turkey is published. Further countries can be published in the admin.
        entities.Each(x => x.Published = false);

        // Turkish country names, so admin lists and address forms show no English names.
        entities.Each(x =>
        {
            if (x.TwoLetterIsoCode != null && TurkishCountryNames.ByTwoLetterIsoCode.TryGetValue(x.TwoLetterIsoCode, out var name))
            {
                x.Name = name;
            }
        });

        entities.WithKey(x => x.NumericIsoCode)
            .Alter(792, x =>
            {
                x.Name = "Türkiye";
                x.DisplayOrder = -10;
                x.Published = true;
                x.AllowsBilling = true;
                x.AllowsShipping = true;
                x.AddressFormat = TurkishStoreDefaults.AddressFormat;
                x.StateProvinces.AddRange(TurkishStoreDefaults.CreateProvinces());
            });
    }

    protected override void Alter(Address entity)
    {
        base.Alter(entity);

        var country = DbContext.Set<Country>().FirstOrDefault(x => x.ThreeLetterIsoCode == "TUR");
        var istanbul = country == null
            ? null
            : DbContext.Set<StateProvince>().FirstOrDefault(x => x.CountryId == country.Id && x.Abbreviation == "34");

        entity.FirstName = "Mağaza";
        entity.LastName = "Yöneticisi";
        // Legal company title is not known yet: placeholder until the owner enters it in the admin.
        entity.Company = ProsevStoreDefaults.StoreName + " [firma unvanı]";
        entity.Address1 = ProsevStoreDefaults.Street;
        entity.Address2 = ProsevStoreDefaults.Street2;
        entity.City = ProsevStoreDefaults.District;
        entity.ZipPostalCode = null;
        entity.PhoneNumber = ProsevStoreDefaults.Phone;
        entity.Country = country ?? entity.Country;
        entity.StateProvince = istanbul;
    }

    protected override void Alter(IList<MeasureDimension> entities)
    {
        base.Alter(entities);

        entities.WithKey(x => x.SystemKeyword)
            .Alter("mm", x => x.Name = "milimetre")
            .Alter("cm", x => x.Name = "santimetre")
            .Alter("m", x => x.Name = "metre")
            .Alter("inch", x => x.Name = "inç")
            .Alter("ft", x => x.Name = "fit");

        ApplyRatios(entities, x => x.SystemKeyword, (x, r) => x.Ratio = r, TurkishStoreDefaults.DimensionRatios);
    }

    protected override void Alter(IList<MeasureWeight> entities)
    {
        base.Alter(entities);

        entities.WithKey(x => x.SystemKeyword)
            .Alter("oz", x => x.Name = "ons")
            .Alter("lb", x => x.Name = "libre")
            .Alter("kg", x => x.Name = "kilogram")
            .Alter("g", x => x.Name = "gram")
            .Alter("l", x => x.Name = "litre")
            .Alter("ml", x => x.Name = "mililitre");

        ApplyRatios(entities, x => x.SystemKeyword, (x, r) => x.Ratio = r, TurkishStoreDefaults.WeightRatios);
    }

    // Base units kilogram and centimetre need ratio 1 (see TurkishStoreDefaults.WeightRatios).
    private static void ApplyRatios<T>(IList<T> entities, Func<T, string> keyword, Action<T, decimal> set, (string Keyword, decimal Ratio)[] ratios)
    {
        foreach (var (key, ratio) in ratios)
        {
            var entity = entities.FirstOrDefault(x => keyword(x) == key);
            if (entity != null)
            {
                set(entity, ratio);
            }
        }
    }

    protected override void Alter(IList<ShippingMethod> entities)
    {
        base.Alter(entities);

        entities.WithKey(x => x.DisplayOrder)
            .Alter(0, x =>
            {
                x.Name = "Mağazadan teslim alma";
                x.Description = "Siparişinizi mağazamızdan kendiniz teslim alın.";
            })
            .Alter(1, x =>
            {
                x.Name = "Kargo";
                x.Description = "Siparişiniz anlaşmalı kargo firmamız ile adresinize gönderilir.";
            })
            .Alter(2, x => x.Name = "Ücretsiz kargo");
    }

    protected override void Alter(IList<DeliveryTime> entities)
    {
        base.Alter(entities);

        entities.WithKey(x => x.DisplayOrder)
            .Alter(0, x => x.Name = "Hemen kargoya hazır")
            .Alter(1, x => x.Name = "2-5 iş günü")
            .Alter(2, x => x.Name = "7 iş günü");
    }

    protected override void Alter(IList<QuantityUnit> entities)
    {
        base.Alter(entities);

        var names = new (string Name, string Plural)[]
        {
            ("Adet", "Adet"),
            ("Kutu", "Kutu"),
            ("Koli", "Koli"),
            ("Palet", "Palet"),
            ("Birim", "Birim"),
            ("Çuval", "Çuval"),
            ("Poşet", "Poşet"),
            ("Teneke", "Teneke"),
            ("Paket", "Paket"),
            ("Çubuk", "Çubuk"),
            ("Şişe", "Şişe"),
            ("Kavanoz", "Kavanoz"),
            ("Demet", "Demet"),
            ("Rulo", "Rulo"),
            ("Bardak", "Bardak"),
            ("Deste", "Deste"),
            ("Varil", "Varil"),
            ("Set", "Set"),
            ("Kova", "Kova")
        };

        foreach (var unit in entities)
        {
            if (unit.DisplayOrder >= 0 && unit.DisplayOrder < names.Length)
            {
                var (name, plural) = names[unit.DisplayOrder];
                unit.Name = name;
                unit.NamePlural = plural;
                unit.Description = name;
            }
        }
    }

    protected override void Alter(IList<CustomerRole> entities)
    {
        base.Alter(entities);

        entities.WithKey(x => x.Name)
           .Alter("Administrators", x => x.Name = "Yöneticiler")
           .Alter("Forum Moderators", x => x.Name = "Forum moderatörleri")
           .Alter("Registered", x => x.Name = "Kayıtlı")
           .Alter("Guests", x => x.Name = "Misafirler")
           .Alter("Inactive new customers", x => x.Name = "Aktif olmayan yeni müşteriler");
    }

    protected override void Alter(IList<ActivityLogType> entities)
    {
        base.Alter(entities);

        foreach (var x in entities)
        {
            if (TurkishSystemNames.ActivityLogTypes.TryGetValue(x.SystemKeyword, out var name))
            {
                x.Name = name.Tr;
            }
        }
    }

    protected override void Alter(IList<TaskDescriptor> entities)
    {
        base.Alter(entities);

        foreach (var x in entities)
        {
            if (TurkishSystemNames.ScheduleTasks.TryGetValue(x.Type, out var name))
            {
                x.Name = name.Tr;
            }
        }
    }

    protected override void Alter(IList<ProductTemplate> entities)
    {
        base.Alter(entities);
        entities.Each(x => x.Name = TurkishSystemNames.Templates.TryGetValue(x.ViewPath, out var name) ? name.Tr : x.Name);
    }

    protected override void Alter(IList<CategoryTemplate> entities)
    {
        base.Alter(entities);
        entities.Each(x => x.Name = TurkishSystemNames.Templates.TryGetValue(x.ViewPath, out var name) ? name.Tr : x.Name);
    }

    protected override void Alter(IList<ManufacturerTemplate> entities)
    {
        base.Alter(entities);
        entities.Each(x => x.Name = TurkishSystemNames.Templates.TryGetValue(x.ViewPath, out var name) ? name.Tr : x.Name);
    }

    protected override void Alter(IList<EmailAccount> entities)
    {
        base.Alter(entities);

        entities.WithKey(x => x.DisplayName)
            .Alter("Store name", x => x.DisplayName = ProsevStoreDefaults.StoreName);
    }

    protected override void Alter(IList<PriceLabel> entities)
    {
        base.Alter(entities);

        entities.WithKey(x => x.ShortName)
            .Alter("MSRP", x =>
            {
                x.ShortName = "TSF";
                x.Name = "Tavsiye edilen satış fiyatı";
                x.Description = "Üreticinin önerdiği satış fiyatıdır.";
            })
            .Alter("Lowest", x =>
            {
                x.ShortName = "En düşük";
                x.Name = "Son 30 günün en düşük fiyatı";
                x.Description = "İndirim uygulanmadan önceki son 30 gün içindeki en düşük satış fiyatıdır.";
            })
            .Alter("Regular", x =>
            {
                x.ShortName = "Normal";
                x.Name = "Normal fiyat";
                x.Description = "Kampanya fiyatları hariç, ürünün mağazamızdaki olağan satış fiyatıdır.";
            });
    }

    protected override void Alter(IList<MenuEntity> entities)
    {
        base.Alter(entities);

        foreach (var item in entities.SelectMany(x => x.Items).Where(x => x.Title == TurkishStoreDefaults.CookieManagerMenuTitleEn))
        {
            item.Title = TurkishStoreDefaults.CookieManagerMenuTitle;
        }
    }

    protected override void Alter(IList<Topic> entities)
    {
        base.Alter(entities);

        const string editHint = " Bu metni yönetim panelinden düzenleyebilirsiniz.</p>";

        entities.WithKey(x => x.SystemName)
            .Alter("AboutUs", x =>
            {
                x.Title = "Hakkımızda";
                x.Body = ProsevStoreDefaults.AboutUsBody;
            })
            .Alter("CheckoutAsGuestOrRegister", x =>
            {
                x.Title = "";
                x.Body = "<p><strong>Giriş yapın, zaman kazanın!</strong><br />Hesabınızla şu avantajlardan yararlanın:</p><ul><li>Hızlı ve kolay alışveriş</li><li>Sipariş durumuna ve sipariş geçmişine her zaman erişim</li></ul>";
            })
            .Alter("ConditionsOfUse", x =>
            {
                x.Title = "Kullanım koşulları";
                x.Body = "<p>Kullanım koşullarınızı buraya ekleyin." + editHint;
            })
            .Alter("ContactUs", x =>
            {
                x.Title = "İletişim";
                x.Body = ProsevStoreDefaults.ContactUsBody;
            })
            .Alter("HomePageText", x =>
            {
                x.Title = "Hoş geldiniz";
                x.Body = "<p>Mağazanız için bir karşılama metnini buraya ekleyin." + editHint;
            })
            .Alter("LoginRegistrationInfo", x =>
            {
                x.Title = "Giriş / Üyelik";
                x.Body = "<p><strong>Henüz üye değil misiniz?</strong></p><p>Hemen hesabınızı oluşturun. Hesabınızla daha hızlı sipariş verebilir, güncel ve geçmiş siparişlerinizi her zaman takip edebilirsiniz.</p>";
            })
            .Alter("PrivacyInfo", x =>
            {
                x.ShortTitle = "Gizlilik";
                x.Title = "Gizlilik politikası";
                x.Body = "<p>Gizlilik politikanızı buraya ekleyin." + editHint;
            })
            .Alter("ShippingInfo", x =>
            {
                x.ShortTitle = "Kargo bilgileri";
                x.Title = "Kargo ve iade";
                x.Body = "<p>Kargo ve iade koşullarınızı buraya ekleyin." + editHint;
            })
            .Alter("Imprint", x =>
            {
                x.Title = "Künye";
                x.Body = ProsevStoreDefaults.ImprintBody;
            })
            .Alter("Disclaimer", x =>
            {
                x.Title = "Cayma hakkı";
                x.Body = "<p>Cayma hakkı bilgilendirmenizi buraya ekleyin." + editHint;
            })
            .Alter("PaymentInfo", x =>
            {
                // Terms as printed in the Prosev/Motosev 2026 catalog.
                x.Title = ProsevStoreDefaults.PaymentInfoTitle;
                x.Body = ProsevStoreDefaults.PaymentInfoBody;
            });
    }

    protected override void Alter(UrlRecord entity)
    {
        base.Alter(entity);

        if (entity.EntityName == "Topic")
        {
            entity.Slug = entity.Slug switch
            {
                "aboutus" => "hakkimizda",
                "conditionsofuse" => "kullanim-kosullari",
                "contactus" => "iletisim",
                "privacyinfo" => "gizlilik-politikasi",
                "shippinginfo" => "kargo-ve-iade",
                "imprint" => "kunye",
                "disclaimer" => "cayma-bilgilendirmesi", // "cayma-hakki" belongs to MotorShop.Legal's withdrawal topic
                "paymentinfo" => "odeme-secenekleri",
                _ => entity.Slug,
            };
        }
    }

    protected override void Alter(IList<ISettings> settings)
    {
        base.Alter(settings);

        var defaultDimensionId = DbContext.MeasureDimensions.FirstOrDefault(x => x.SystemKeyword == "cm")?.Id;
        var defaultWeightId = DbContext.MeasureWeights.FirstOrDefault(x => x.SystemKeyword == "kg")?.Id;
        var liraId = DbContext.Currencies.FirstOrDefault(x => x.CurrencyCode == TurkishStoreDefaults.CurrencyCode)?.Id;
        var standardTaxCategoryName = TurkishStoreDefaults.TaxCategories[0].Name;
        var standardTaxCategoryId = DbContext.TaxCategories.FirstOrDefault(x => x.Name == standardTaxCategoryName)?.Id;
        var lowestPriceLabelId = DbContext.PriceLabels.FirstOrDefault(x => x.ShortName == "En düşük")?.Id;
        var turkeyId = DbContext.Set<Country>().FirstOrDefault(x => x.ThreeLetterIsoCode == "TUR")?.Id;

        settings
            .Alter<MeasureSettings>(x =>
            {
                x.BaseDimensionId = defaultDimensionId ?? x.BaseDimensionId;
                x.BaseWeightId = defaultWeightId ?? x.BaseWeightId;
            })
            .Alter<CurrencySettings>(x =>
            {
                if (liraId.HasValue)
                {
                    x.PrimaryCurrencyId = liraId.Value;
                    x.PrimaryExchangeCurrencyId = liraId.Value;
                }
            })
            .Alter<DateTimeSettings>(x => x.DefaultStoreTimeZoneId = TurkishStoreDefaults.TimeZoneId)
            .Alter<SeoSettings>(x => x.MetaTitle = ProsevStoreDefaults.StoreName)
            .Alter<CompanyInformationSettings>(x =>
            {
                // Legal title, tax number and commercial register stay empty until the owner enters them.
                x.Street = ProsevStoreDefaults.Street;
                x.Street2 = ProsevStoreDefaults.Street2;
                x.City = ProsevStoreDefaults.City;
                x.StateName = ProsevStoreDefaults.StateName;
                x.CountryId = turkeyId ?? x.CountryId;
            })
            .Alter<ContactDataSettings>(x => x.CompanyTelephoneNumber = ProsevStoreDefaults.Phone)
            .Alter<SocialSettings>(x =>
            {
                // Only Instagram exists; "#" placeholders would render dead icons in the footer (null falls back to "#").
                x.InstagramLink = ProsevStoreDefaults.InstagramUrl;
                x.FacebookLink = string.Empty;
                x.TwitterLink = string.Empty;
                x.YoutubeLink = string.Empty;
                x.TikTokLink = string.Empty;
            })
            .Alter<OrderSettings>(x =>
            {
                x.ReturnRequestActions = "Ödeme yöntemine iade,Ürün değişimi,Başka bir ürünle değişim,Onarım,Hediye kartı / kupon,Diğer";
                x.ReturnRequestReasons = "Yanlış ürün gönderildi,Yanlış beden veya model sipariş edildi,Ürünü beğenmedim,Ürün açıklamaya uymuyor,Ürün kusurlu veya arızalı,Ürün hasarlı geldi,Teslimat eksik,Yanlışlıkla sipariş verildi,Diğer";
                // Statutory withdrawal period for distance sales in Turkey. NEEDS OWNER CONFIRMATION.
                x.NumberOfDaysReturnRequestAvailable = 14;
            })
            // The compare page builds its feature rows from the bulleted full descriptions.
            .Alter<CatalogSettings>(x => x.IncludeFullDescriptionInCompareProducts = true)
            .Alter<ShippingSettings>(x => x.EstimateShippingEnabled = false)
            .Alter<TaxSettings>(x =>
            {
                // Consumer prices are entered and displayed including KDV. NEEDS OWNER/ACCOUNTANT CONFIRMATION.
                x.PricesIncludeTax = true;
                x.TaxDisplayType = TaxDisplayType.IncludingTax;
                x.DisplayTaxSuffix = true;
                x.TaxBasedOn = TaxBasedOn.ShippingAddress;
                x.ShippingIsTaxable = true;
                x.ShippingPriceIncludesTax = true;
                x.ShippingTaxClassId = standardTaxCategoryId ?? x.ShippingTaxClassId;
                x.EuVatEnabled = false;
            })
            .Alter<PriceSettings>(x =>
            {
                x.OfferBadgeLabel = "Fırsat";
                x.LimitedOfferBadgeLabel = "Sınırlı süre";
                x.DefaultRegularPriceLabelId = lowestPriceLabelId ?? x.DefaultRegularPriceLabelId;
            });
    }
}
