using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MotorShop.AdminPanel.Configuration;
using MotorShop.AdminPanel.Models;
using Smartstore.Core;
using Smartstore.Core.Catalog.Categories;
using Smartstore.Core.Catalog.Discounts;
using Smartstore.Core.Catalog.Products;
using Smartstore.Core.Checkout.Payment;
using Smartstore.Core.Checkout.Shipping;
using Smartstore.Core.Checkout.Tax;
using Smartstore.Core.Common.Configuration;
using Smartstore.Core.Configuration;
using Smartstore.Core.Content.Topics;
using Smartstore.Core.Data;
using Smartstore.Core.Logging;
using Smartstore.Core.Messaging;
using Smartstore.Core.Seo;
using Smartstore.Engine;
using Smartstore.Engine.Modularity;

namespace MotorShop.AdminPanel.Services;

/// <summary>
/// CMS pages (topics). Blog posts live in WordPress (MotorShop.WordPress) and are only linked.
/// </summary>
public class ContentPanelService
{
    const string WordPressModule = "MotorShop.WordPress";

    private readonly SmartDbContext _db;
    private readonly ICommonServices _services;
    private readonly IUrlService _urlService;
    private readonly IApplicationContext _appContext;

    public ContentPanelService(SmartDbContext db, ICommonServices services, IUrlService urlService, IApplicationContext appContext)
    {
        _db = db;
        _services = services;
        _urlService = urlService;
        _appContext = appContext;
    }

    public Localizer T { get; set; } = NullLocalizer.Instance;

    public bool BlogInstalled => _appContext.ModuleCatalog.GetModuleByName(WordPressModule) != null;

    public async Task<List<TopicRowModel>> GetTopicsAsync()
    {
        var topics = await _db.Topics.AsNoTracking().OrderBy(x => x.Title ?? x.SystemName).ToListAsync();
        await _urlService.PrefetchUrlRecordsAsync(nameof(Topic), [0], [.. topics.Select(x => x.Id)]);

        var rows = new List<TopicRowModel>();
        foreach (var t in topics)
        {
            rows.Add(new TopicRowModel
            {
                Id = t.Id,
                Title = t.Title.NullEmpty() ?? t.SystemName,
                SystemName = t.SystemName,
                IsPublished = t.IsPublished,
                IsSystemTopic = t.IsSystemTopic,
                RenderAsWidget = t.RenderAsWidget,
                Slug = t.IsPublished && !t.RenderAsWidget ? (await _urlService.GetActiveSlugAsync(t.Id, nameof(Topic), 0)).NullEmpty() : null
            });
        }

        return rows;
    }

    public async Task<TopicEditorModel> LoadTopicAsync(int id)
    {
        var t = await _db.Topics.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (t == null)
        {
            return null;
        }

        return new TopicEditorModel
        {
            Id = t.Id,
            SystemName = t.SystemName,
            Title = t.Title,
            ShortTitle = t.ShortTitle,
            Intro = t.Intro,
            Body = t.Body,
            IsPublished = t.IsPublished,
            MetaTitle = t.MetaTitle,
            MetaDescription = t.MetaDescription,
            MetaKeywords = t.MetaKeywords,
            SeName = await t.GetActiveSlugAsync(0, true, false),
            IsSystemTopic = t.IsSystemTopic,
            RenderAsWidget = t.RenderAsWidget,
            Slug = t.IsPublished && !t.RenderAsWidget ? await _urlService.GetActiveSlugAsync(t.Id, nameof(Topic), 0) : null
        };
    }

    public async Task<string> SaveTopicAsync(TopicEditorModel model)
    {
        var topic = await _db.Topics.FirstOrDefaultAsync(x => x.Id == model.Id);
        if (topic == null)
        {
            return T("Plugins.MotorShop.AdminPanel.NotFound");
        }

        topic.Title = model.Title.Trim();
        topic.ShortTitle = model.ShortTitle;
        topic.Intro = model.Intro;
        topic.Body = model.Body;
        topic.IsPublished = model.IsPublished;
        topic.MetaTitle = model.MetaTitle;
        topic.MetaDescription = model.MetaDescription;
        topic.MetaKeywords = model.MetaKeywords;

        await _db.SaveChangesAsync();
        await _urlService.SaveSlugAsync(topic, model.SeName, topic.Title.NullEmpty() ?? topic.SystemName, true);

        await _db.SaveChangesAsync();
        return null;
    }

    public async Task<bool> SetPublishedAsync(int id, bool published)
    {
        var topic = await _db.Topics.FirstOrDefaultAsync(x => x.Id == id);
        if (topic == null)
        {
            return false;
        }

        topic.IsPublished = published;
        await _db.SaveChangesAsync();
        return true;
    }
}

/// <summary>
/// Coupons on Smartstore's discount entity. Discount arithmetic stays in Smartstore's pricing pipeline;
/// this service only edits the discount record and its category/product assignments.
/// </summary>
public class CouponPanelService
{
    private readonly SmartDbContext _db;
    private readonly ICommonServices _services;
    private readonly PanelQueryService _queries;

    public CouponPanelService(SmartDbContext db, ICommonServices services, PanelQueryService queries)
    {
        _db = db;
        _services = services;
        _queries = queries;
    }

    public Localizer T { get; set; } = NullLocalizer.Instance;

    private string R(string key, params object[] args)
        => T("Plugins.MotorShop.AdminPanel." + key, args);

    internal static readonly DiscountType[] SupportedTypes =
    [
        DiscountType.AssignedToOrderTotal,
        DiscountType.AssignedToOrderSubTotal,
        DiscountType.AssignedToCategories,
        DiscountType.AssignedToSkus,
        DiscountType.AssignedToShipping
    ];

    internal static CouponState GetState(Discount d, int used, DateTime utcNow)
    {
        if (d.EndDateUtc.HasValue && d.EndDateUtc < utcNow)
        {
            return CouponState.Expired;
        }

        if (d.StartDateUtc.HasValue && d.StartDateUtc > utcNow)
        {
            return CouponState.Scheduled;
        }

        return d.DiscountLimitation == DiscountLimitationType.NTimesOnly && used >= d.LimitationTimes
            ? CouponState.UsedUp
            : CouponState.Active;
    }

    public async Task<List<CouponRowModel>> GetCouponsAsync()
    {
        var discounts = await _db.Discounts.AsNoTracking().OrderByDescending(x => x.Id).ToListAsync();
        var usage = await _db.DiscountUsageHistory
            .AsNoTracking()
            .GroupBy(x => x.DiscountId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        var now = DateTime.UtcNow;
        var currency = _services.CurrencyService.PrimaryCurrency;
        var dt = _services.DateTimeHelper;

        return [.. discounts.Select(d =>
        {
            var used = usage.GetValueOrDefault(d.Id);
            return new CouponRowModel
            {
                Id = d.Id,
                Name = d.Name,
                Type = d.DiscountType,
                Value = d.UsePercentage
                    ? "%" + d.DiscountPercentage.ToString("0.##", CultureInfo.GetCultureInfo("tr-TR"))
                    : _services.CurrencyService.CreateMoney(d.DiscountAmount, currency).ToString(),
                CouponCode = d.CouponCode,
                RequiresCouponCode = d.RequiresCouponCode,
                Start = d.StartDateUtc.HasValue ? dt.ConvertToUserTime(d.StartDateUtc.Value, DateTimeKind.Utc) : null,
                End = d.EndDateUtc.HasValue ? dt.ConvertToUserTime(d.EndDateUtc.Value, DateTimeKind.Utc) : null,
                State = GetState(d, used, now),
                Used = used,
                Limit = d.DiscountLimitation switch
                {
                    DiscountLimitationType.NTimesOnly => R("Coupon.LimitTotal", d.LimitationTimes),
                    DiscountLimitationType.NTimesPerCustomer => R("Coupon.LimitPerCustomer", d.LimitationTimes),
                    _ => R("Coupon.Unlimited")
                }
            };
        })];
    }

    public async Task<CouponEditorModel> LoadAsync(int id)
    {
        var model = new CouponEditorModel();

        if (id > 0)
        {
            var d = await _db.Discounts
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.AppliedToCategories)
                .Include(x => x.AppliedToProducts)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (d == null)
            {
                return null;
            }

            var dt = _services.DateTimeHelper;
            model = new CouponEditorModel
            {
                Id = d.Id,
                Name = d.Name,
                DiscountType = d.DiscountType,
                UsePercentage = d.UsePercentage,
                Percentage = d.DiscountPercentage.ToString("0.##", CultureInfo.GetCultureInfo("tr-TR")),
                Amount = ProductEditService.FormatAmount(d.DiscountAmount),
                RequiresCouponCode = d.RequiresCouponCode,
                CouponCode = d.CouponCode,
                Start = d.StartDateUtc.HasValue ? dt.ConvertToUserTime(d.StartDateUtc.Value, DateTimeKind.Utc) : null,
                End = d.EndDateUtc.HasValue ? dt.ConvertToUserTime(d.EndDateUtc.Value, DateTimeKind.Utc) : null,
                Limitation = d.DiscountLimitation,
                LimitationTimes = Math.Max(1, d.LimitationTimes),
                CategoryIds = [.. d.AppliedToCategories.Select(x => x.Id)],
                Skus = string.Join(Environment.NewLine, d.AppliedToProducts.Where(x => x.Sku.HasValue()).Select(x => x.Sku)),
                Used = await _db.DiscountUsageHistory.CountAsync(x => x.DiscountId == d.Id)
            };
        }

        model.AllCategories = await _queries.GetCategoryOptionsAsync();
        return model;
    }

    /// <returns>Field name to error; empty on success.</returns>
    public async Task<Dictionary<string, string>> SaveAsync(CouponEditorModel model)
    {
        var errors = new Dictionary<string, string>();

        if (!SupportedTypes.Contains(model.DiscountType))
        {
            errors[nameof(model.DiscountType)] = R("InvalidValue");
        }

        decimal percentage = 0, amount = 0;
        if (model.UsePercentage)
        {
            if (!decimal.TryParse(model.Percentage?.Trim().Replace("%", "").Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out percentage)
                || percentage <= 0 || percentage > 100)
            {
                errors[nameof(model.Percentage)] = R("Coupon.PercentRange");
            }
        }
        else if (!AmountParser.TryParse(model.Amount, out amount, out var amountError) || amount <= 0)
        {
            errors[nameof(model.Amount)] = amountError != null ? R("Amount." + amountError) : R("Coupon.AmountPositive");
        }

        var code = model.CouponCode?.Trim();
        if (model.RequiresCouponCode)
        {
            if (code.IsEmpty())
            {
                errors[nameof(model.CouponCode)] = R("Coupon.CodeRequired");
            }
            else if (await _db.Discounts.AnyAsync(x => x.Id != model.Id && x.CouponCode == code))
            {
                errors[nameof(model.CouponCode)] = R("Coupon.CodeTaken");
            }
        }

        if (model.Start.HasValue && model.End.HasValue && model.End < model.Start)
        {
            errors[nameof(model.End)] = R("Rule.SpecialPriceDates");
        }

        if (model.Limitation != DiscountLimitationType.Unlimited && model.LimitationTimes < 1)
        {
            errors[nameof(model.LimitationTimes)] = R("Coupon.LimitRange");
        }

        var categoryIds = model.DiscountType == DiscountType.AssignedToCategories
            ? await _db.Categories.Where(x => model.CategoryIds.Contains(x.Id) && !x.Deleted).Select(x => x.Id).ToListAsync()
            : [];

        var skus = (model.Skus ?? string.Empty)
            .Split(['\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var products = model.DiscountType == DiscountType.AssignedToSkus && skus.Length > 0
            ? await _db.Products.Where(x => skus.Contains(x.Sku) && !x.Deleted && !x.IsSystemProduct).ToListAsync()
            : [];

        if (model.DiscountType == DiscountType.AssignedToCategories && categoryIds.Count == 0)
        {
            errors[nameof(model.CategoryIds)] = R("Coupon.CategoriesRequired");
        }

        if (model.DiscountType == DiscountType.AssignedToSkus)
        {
            var missing = skus.Where(s => !products.Any(p => p.Sku.EqualsNoCase(s))).ToList();
            if (skus.Length == 0)
            {
                errors[nameof(model.Skus)] = R("Coupon.SkusRequired");
            }
            else if (missing.Count > 0)
            {
                errors[nameof(model.Skus)] = R("Coupon.SkusUnknown", string.Join(", ", missing.Take(10)));
            }
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        var isNew = model.Id == 0;
        Discount discount;
        if (isNew)
        {
            discount = new Discount();
            _db.Discounts.Add(discount);
        }
        else
        {
            discount = await _db.Discounts
                .AsSplitQuery()
                .Include(x => x.AppliedToCategories)
                .Include(x => x.AppliedToProducts)
                .FirstOrDefaultAsync(x => x.Id == model.Id);

            if (discount == null)
            {
                errors[""] = R("NotFound");
                return errors;
            }
        }

        var dt = _services.DateTimeHelper;
        discount.Name = model.Name.Trim();
        discount.DiscountType = model.DiscountType;
        discount.UsePercentage = model.UsePercentage;
        discount.DiscountPercentage = model.UsePercentage ? percentage : 0;
        discount.DiscountAmount = model.UsePercentage ? 0 : amount;
        discount.RequiresCouponCode = model.RequiresCouponCode;
        discount.CouponCode = model.RequiresCouponCode ? code : null;
        discount.StartDateUtc = model.Start.HasValue ? dt.ConvertToUtcTime(model.Start.Value, dt.CurrentTimeZone) : null;
        discount.EndDateUtc = model.End.HasValue ? dt.ConvertToUtcTime(model.End.Value, dt.CurrentTimeZone) : null;
        discount.DiscountLimitation = model.Limitation;
        discount.LimitationTimes = model.Limitation == DiscountLimitationType.Unlimited ? 0 : model.LimitationTimes;

        // Assignments. The catalog only applies discounts to entities flagged HasDiscountsApplied;
        // Smartstore's hooks recompute that flag when the category or product is saved, so every
        // touched entity is marked modified.
        var touchedCategories = new List<Category>();
        var touchedProducts = new List<Product>();

        foreach (var c in discount.AppliedToCategories.Where(x => !categoryIds.Contains(x.Id)).ToList())
        {
            discount.AppliedToCategories.Remove(c);
            touchedCategories.Add(c);
        }
        var addCategories = await _db.Categories.Where(x => categoryIds.Contains(x.Id)).ToListAsync();
        foreach (var c in addCategories.Where(c => !discount.AppliedToCategories.Any(x => x.Id == c.Id)))
        {
            discount.AppliedToCategories.Add(c);
            touchedCategories.Add(c);
        }

        foreach (var p in discount.AppliedToProducts.Where(x => !products.Any(y => y.Id == x.Id)).ToList())
        {
            discount.AppliedToProducts.Remove(p);
            touchedProducts.Add(p);
        }
        foreach (var p in products.Where(p => !discount.AppliedToProducts.Any(x => x.Id == p.Id)))
        {
            discount.AppliedToProducts.Add(p);
            touchedProducts.Add(p);
        }

        await _db.SaveChangesAsync();

        foreach (var c in touchedCategories)
        {
            c.HasDiscountsApplied = await _db.Categories.Where(x => x.Id == c.Id).SelectMany(x => x.AppliedDiscounts).AnyAsync();
        }
        foreach (var p in touchedProducts)
        {
            p.HasDiscountsApplied = await _db.Products.Where(x => x.Id == p.Id).SelectMany(x => x.AppliedDiscounts).AnyAsync();
        }

        _services.ActivityLogger.LogActivity(
            isNew ? KnownActivityLogTypes.AddNewDiscount : KnownActivityLogTypes.EditDiscount,
            T(isNew ? "ActivityLog.AddNewDiscount" : "ActivityLog.EditDiscount", discount.Name));
        await _db.SaveChangesAsync();

        model.Id = discount.Id;
        return errors;
    }

    /// <summary>
    /// Smartstore has no "active" flag: ending sets the end date to now, starting removes a past end
    /// date and a future start date.
    /// </summary>
    public async Task<bool> SetActiveAsync(int id, bool active)
    {
        var discount = await _db.Discounts.FirstOrDefaultAsync(x => x.Id == id);
        if (discount == null)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        if (active)
        {
            if (discount.EndDateUtc < now)
            {
                discount.EndDateUtc = null;
            }
            if (discount.StartDateUtc > now)
            {
                discount.StartDateUtc = now;
            }
        }
        else
        {
            discount.EndDateUtc = now;
        }

        _services.ActivityLogger.LogActivity(KnownActivityLogTypes.EditDiscount, T("ActivityLog.EditDiscount", discount.Name));
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var discount = await _db.Discounts.FirstOrDefaultAsync(x => x.Id == id);
        if (discount == null)
        {
            return false;
        }

        // Smartstore's discount hook clears HasDiscountsApplied on the affected entities.
        _db.Discounts.Remove(discount);
        await _db.SaveChangesAsync();

        _services.ActivityLogger.LogActivity(KnownActivityLogTypes.DeleteDiscount, T("ActivityLog.DeleteDiscount", discount.Name));
        await _db.SaveChangesAsync();
        return true;
    }
}

/// <summary>
/// Everyday store settings, saved for all stores through Smartstore's setting factory.
/// Payment credentials, tax and currency rules stay in their own classic pages.
/// </summary>
public class SettingsPanelService
{
    private readonly SmartDbContext _db;
    private readonly ICommonServices _services;
    private readonly ISettingFactory _settingFactory;
    private readonly IPaymentService _paymentService;
    private readonly ModuleManager _moduleManager;

    public SettingsPanelService(
        SmartDbContext db,
        ICommonServices services,
        ISettingFactory settingFactory,
        IPaymentService paymentService,
        ModuleManager moduleManager)
    {
        _db = db;
        _services = services;
        _settingFactory = settingFactory;
        _paymentService = paymentService;
        _moduleManager = moduleManager;
    }

    public Localizer T { get; set; } = NullLocalizer.Instance;

    public async Task<SettingsPageModel> GetAsync()
    {
        var store = _services.StoreContext.CurrentStore;
        var panel = await _settingFactory.LoadSettingsAsync<MotorPanelSettings>();
        var company = await _settingFactory.LoadSettingsAsync<CompanyInformationSettings>();
        var contact = await _settingFactory.LoadSettingsAsync<ContactDataSettings>();
        var shipping = await _settingFactory.LoadSettingsAsync<ShippingSettings>();
        var tax = await _settingFactory.LoadSettingsAsync<TaxSettings>();
        var email = await _settingFactory.LoadSettingsAsync<EmailAccountSettings>();

        var model = new SettingsPageModel
        {
            StoreName = store.Name,
            Panel = new PanelSettingsForm { PanelTitle = panel.PanelTitle, AccentColor = panel.AccentColor },
            Company = new CompanyForm
            {
                StoreName = store.Name,
                CompanyName = company.CompanyName,
                Street = company.Street,
                ZipCode = company.ZipCode,
                City = company.City,
                TaxNumber = company.TaxNumber,
                CommercialRegister = company.CommercialRegister,
                Phone = contact.CompanyTelephoneNumber,
                CompanyEmail = contact.CompanyEmailAddress,
                ContactEmail = contact.ContactEmailAddress,
                SupportEmail = contact.SupportEmailAddress
            },
            Shipping = new ShippingForm
            {
                FreeShippingOverXEnabled = shipping.FreeShippingOverXEnabled,
                FreeShippingOverXValue = ProductEditService.FormatAmount(shipping.FreeShippingOverXValue),
                FreeShippingOverXIncludingTax = shipping.FreeShippingOverXIncludingTax
            },
            PrimaryCurrency = _services.CurrencyService.PrimaryCurrency.CurrencyCode,
            OtherCurrencies = await _db.Currencies.AsNoTracking()
                .Where(x => x.Published && x.Id != _services.CurrencyService.PrimaryCurrency.Id)
                .Select(x => x.CurrencyCode)
                .ToListAsync(),
            PricesIncludeTax = tax.PricesIncludeTax,
            TaxDisplay = _services.Localization.GetLocalizedEnum(tax.TaxDisplayType),
            DefaultEmailAccount = await _db.EmailAccounts.AsNoTracking().Where(x => x.Id == email.DefaultEmailAccountId).Select(x => x.Email).FirstOrDefaultAsync(),
            HideAllPrices = panel.HideAllPrices,
            HasStoreOverrides = await _db.Settings.AnyAsync(x => x.StoreId > 0 &&
                (x.Name.StartsWith("CompanyInformationSettings.") || x.Name.StartsWith("ContactDataSettings.") || x.Name.StartsWith("ShippingSettings.FreeShipping")))
        };

        foreach (var provider in await _paymentService.LoadAllPaymentProvidersAsync())
        {
            model.PaymentMethods.Add((
                _moduleManager.GetLocalizedFriendlyName(provider.Metadata).NullEmpty() ?? provider.Metadata.SystemName,
                await _paymentService.IsPaymentProviderActiveAsync(provider.Metadata.SystemName)));
        }

        return model;
    }

    public async Task SavePanelAsync(PanelSettingsForm form)
    {
        var settings = await _settingFactory.LoadSettingsAsync<MotorPanelSettings>();
        settings.PanelTitle = form.PanelTitle?.Trim();
        settings.AccentColor = form.AccentColor?.Trim();
        await _settingFactory.SaveSettingsAsync(settings);
        Log();
    }

    public async Task<string> SaveCompanyAsync(CompanyForm form, bool canUpdateStore)
    {
        if (canUpdateStore)
        {
            var store = await _db.Stores.FirstOrDefaultAsync(x => x.Id == _services.StoreContext.CurrentStore.Id);
            if (store != null && form.StoreName.HasValue())
            {
                store.Name = form.StoreName.Trim();
                await _db.SaveChangesAsync();
            }
        }

        var company = await _settingFactory.LoadSettingsAsync<CompanyInformationSettings>();
        company.CompanyName = form.CompanyName?.Trim();
        company.Street = form.Street?.Trim();
        company.ZipCode = form.ZipCode?.Trim();
        company.City = form.City?.Trim();
        company.TaxNumber = form.TaxNumber?.Trim();
        company.CommercialRegister = form.CommercialRegister?.Trim();
        await _settingFactory.SaveSettingsAsync(company);

        var contact = await _settingFactory.LoadSettingsAsync<ContactDataSettings>();
        contact.CompanyTelephoneNumber = form.Phone?.Trim();
        contact.CompanyEmailAddress = form.CompanyEmail?.Trim();
        contact.ContactEmailAddress = form.ContactEmail?.Trim();
        contact.SupportEmailAddress = form.SupportEmail?.Trim();
        await _settingFactory.SaveSettingsAsync(contact);

        Log();
        return null;
    }

    public async Task<string> SaveShippingAsync(ShippingForm form)
    {
        decimal value = 0;
        if (form.FreeShippingOverXEnabled && (!AmountParser.TryParse(form.FreeShippingOverXValue, out value, out var error) || value <= 0))
        {
            return T("Plugins.MotorShop.AdminPanel.Settings.FreeShippingValue");
        }

        var shipping = await _settingFactory.LoadSettingsAsync<ShippingSettings>();
        shipping.FreeShippingOverXEnabled = form.FreeShippingOverXEnabled;
        if (form.FreeShippingOverXEnabled)
        {
            shipping.FreeShippingOverXValue = value;
        }
        shipping.FreeShippingOverXIncludingTax = form.FreeShippingOverXIncludingTax;
        await _settingFactory.SaveSettingsAsync(shipping);

        Log();
        return null;
    }

    private void Log()
        => _services.ActivityLogger.LogActivity(KnownActivityLogTypes.EditSettings, T("Plugins.MotorShop.AdminPanel.Log.Settings"));
}
