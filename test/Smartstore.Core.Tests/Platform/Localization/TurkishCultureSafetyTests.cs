using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using DotLiquid;
using DotLiquid.NamingConventions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using NUnit.Framework;
using Smartstore.Caching;
using Smartstore.Core.Checkout.Payment;
using Smartstore.Core.Checkout.Rules;
using Smartstore.Core.Common.Configuration;
using Smartstore.Core.Configuration;
using Smartstore.Core.Data;
using Smartstore.Core.Identity;
using Smartstore.Core.Localization;
using Smartstore.Core.Rules;
using Smartstore.Core.Security;
using Smartstore.Core.Seo;
using Smartstore.Core.Seo.Routing;
using Smartstore.Core.Stores;
using Smartstore.Data;
using Smartstore.Data.Providers;
using Smartstore.Domain;
using Smartstore.Engine;
using Smartstore.Engine.Modularity;
using Smartstore.Templating.Liquid;
using Smartstore.Test.Common;
using Smartstore.Utilities.Html;

namespace Smartstore.Core.Tests.Platform.Localization;

/// <summary>
/// MOTOR: tr-TR is the default storefront culture. Under tr-TR "I".ToLower() is dotless "ı" and
/// "i".ToUpper() is dotted "İ", so identifiers (keys, system names, slugs, tag names) must be
/// cased and compared culture-independently. Every test runs with CurrentCulture/CurrentUICulture = tr-TR.
/// </summary>
[TestFixture]
public class TurkishCultureSafetyTests : ServiceTestBase
{
    const int LanguageId = 77;

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private CultureInfo _prevCulture;
    private CultureInfo _prevUICulture;
    private Language _language;
    private Mock<IWorkContext> _workContextMock;

    [OneTimeSetUp]
    public new void SetUp()
    {
        _language = new Language { Id = LanguageId, LanguageCulture = "tr-TR", UniqueSeoCode = "tr" };

        _workContextMock = new Mock<IWorkContext>();
        _workContextMock.Setup(x => x.WorkingLanguage).Returns(_language);
        _workContextMock.Setup(x => x.CurrentCustomer).Returns(new Customer { Id = 1 });
    }

    [SetUp]
    public void SwitchToTurkish()
    {
        _prevCulture = CultureInfo.CurrentCulture;
        _prevUICulture = CultureInfo.CurrentUICulture;

        CultureInfo.CurrentCulture = Turkish;
        CultureInfo.CurrentUICulture = Turkish;
    }

    [TearDown]
    public void RestoreCulture()
    {
        CultureInfo.CurrentCulture = _prevCulture;
        CultureInfo.CurrentUICulture = _prevUICulture;

        DbContext.LocaleStringResources.RemoveRange(DbContext.LocaleStringResources.Where(x => x.LanguageId == LanguageId));
        DbContext.Settings.RemoveRange(DbContext.Settings.Where(x => x.Name.StartsWith(nameof(TrCultureTestSettings))));
        DbContext.UrlRecords.RemoveRange(DbContext.UrlRecords.Where(x => x.EntityName == nameof(InvoiceItem)));
        DbContext.SaveChanges();
    }

    [Test]
    public void Runtime_Applies_Turkish_Casing()
    {
        // Guards the other tests: without ICU (InvariantGlobalization) they would prove nothing.
        Assert.That("I".ToLower(), Is.EqualTo("ı"));
        Assert.That("i".ToUpper(), Is.EqualTo("İ"));
    }

    #region Settings & localization

    [Test]
    public async Task Setting_Keys_Are_Matched_Case_Insensitively()
    {
        // Stored in lowercase; the settings class asks for "...IsEnabled" and "...TaxCategoryId".
        DbContext.Settings.AddRange(
            new Setting { Name = "TrCultureTestSettings.isenabled", Value = "True", StoreId = 0 },
            new Setting { Name = "TrCultureTestSettings.taxcategoryid", Value = "2", StoreId = 0 });
        await DbContext.SaveChangesAsync();

        var options = new DbContextOptionsBuilder<SmartDbContext>()
            .UseDbFactory(b => b.AddModelAssemblies([typeof(SmartDbContext).Assembly]))
            .Options;

        var dbFactoryMock = new Mock<IDbContextFactory<SmartDbContext>>();
        dbFactoryMock.Setup(x => x.CreateDbContext()).Returns(() => new SmartDbContext((DbContextOptions<SmartDbContext>)options));

        var settingFactory = new SettingFactory(new Mock<IHttpContextAccessor>().Object, NullCache.Instance, dbFactoryMock.Object);
        var settings = await settingFactory.LoadSettingsAsync<TrCultureTestSettings>();

        Assert.That(settings.IsEnabled, Is.True);
        Assert.That(settings.TaxCategoryId, Is.EqualTo(2));
    }

    [Test]
    public void Setting_Cache_Keys_Are_Ascii()
    {
        var rawKey = SettingService.BuildCacheKeyForRawAccess("Tax.TaxProvider.FixedRate.TaxCategoryId2", 0);
        var classKey = SettingService.BuildCacheKeyForClassAccess(typeof(TrCultureTestSettings), 0);

        Assert.That(rawKey, Does.Contain("tax.taxprovider.fixedrate.taxcategoryid2"));
        Assert.That(classKey, Does.Contain("trculturetestsettings"));
        Assert.That(rawKey + classKey, Does.Not.Contain("ı"));
    }

    [Test]
    public void Resource_Keys_Are_Matched_Case_Insensitively()
    {
        DbContext.LocaleStringResources.Add(new LocaleStringResource
        {
            LanguageId = LanguageId,
            ResourceName = "Admin.Configuration.Settings.Tax.ShippingIsTaxable",
            ResourceValue = "Kargo vergilendirilir"
        });
        DbContext.SaveChanges();

        var languageServiceMock = new Mock<ILanguageService>();
        languageServiceMock.Setup(x => x.IsMultiLanguageEnvironment(It.IsAny<int>())).Returns(true);

        var localizationService = new LocalizationService(
            DbContext,
            NullCache.Instance,
            new Lazy<IWorkContext>(() => _workContextMock.Object),
            languageServiceMock.Object);

        Assert.That(localizationService.GetResource("Admin.Configuration.Settings.Tax.ShippingIsTaxable", LanguageId), Is.EqualTo("Kargo vergilendirilir"));
        Assert.That(localizationService.GetResource("admin.configuration.settings.tax.shippingistaxable", LanguageId), Is.EqualTo("Kargo vergilendirilir"));
        Assert.That(localizationService.GetResource("ADMIN.CONFIGURATION.SETTINGS.TAX.SHIPPINGISTAXABLE", LanguageId), Is.EqualTo("Kargo vergilendirilir"));
    }

    #endregion

    #region Slugs

    [Test]
    public void Slugify_Turkish_Text_Is_Ascii_And_Culture_Independent()
    {
        // "Ü" is left out on purpose: the default SEO char map converts it the German way ("ue").
        const string input = "İletişim Işık INDIRIM";

        var turkish = SlugUtility.Slugify(input, new SeoSettings());

        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var invariant = SlugUtility.Slugify(input, new SeoSettings());

        Assert.That(turkish, Is.EqualTo(invariant));
        Assert.That(turkish, Is.EqualTo("iletisim-isik-indirim"));
    }

    [Test]
    public async Task Empty_Slug_Falls_Back_To_Invariant_Entity_Name()
    {
        var urlService = new UrlService(
            db: DbContext,
            cache: NullCache.Instance,
            httpContextAccessor: null,
            workContext: _workContextMock.Object,
            storeContext: new Mock<IStoreContext>().Object,
            languageService: new Mock<ILanguageService>().Object,
            routeHelper: new Mock<IRouteHelper>().Object,
            localizationSettings: new LocalizationSettings(),
            seoSettings: new SeoSettings(),
            performanceSettings: new PerformanceSettings(),
            securitySettings: new SecuritySettings());

        var result = await urlService.ValidateSlugAsync(new InvoiceItem { Id = 5 }, string.Empty, null, true);

        Assert.That(result.Slug, Is.EqualTo("invoiceitem5"));
    }

    #endregion

    #region Permissions & payment

    [Test]
    public async Task Permission_Display_Name_Resolves_Tokens_With_Capital_I()
    {
        DbContext.LocaleStringResources.AddRange(
            new LocaleStringResource { LanguageId = LanguageId, ResourceName = "Admin.Customers", ResourceValue = "Müşteriler" },
            new LocaleStringResource { LanguageId = LanguageId, ResourceName = "Admin.Customers.Customers.Impersonate", ResourceValue = "Müşteri adına oturum aç" });
        await DbContext.SaveChangesAsync();

        var permissionService = new PermissionService(
            DbContext,
            new Lazy<IWorkContext>(() => _workContextMock.Object),
            new Mock<ILocalizationService>().Object,
            NullCache.Instance);

        var displayName = await permissionService.GetDisplayNameAsync("Customer.Impersonate");

        Assert.That(displayName, Is.EqualTo("Müşteriler » Müşteri adına oturum aç"));
    }

    [Test]
    public async Task Payment_System_Names_Are_Matched_Case_Insensitively()
    {
        ProviderManager.RegisterProvider("MotorShop.Iyzico", new Checkout.Payment.TestPaymentMethod1());

        var paymentSettings = new PaymentSettings { ActivePaymentMethodSystemNames = ["MotorShop.Iyzico"] };

        var ruleProviderFactoryMock = new Mock<IRuleProviderFactory>();
        ruleProviderFactoryMock.Setup(x => x.GetProvider(RuleScope.Cart, null)).Returns(new Mock<ICartRuleProvider>().Object);

        var paymentService = new PaymentService(
            DbContext,
            new Mock<IStoreContext>().Object,
            null,
            paymentSettings,
            ruleProviderFactoryMock.Object,
            ProviderManager,
            NullCache.Instance,
            new Mock<IRequestCache>().Object,
            new Mock<ITypeScanner>().Object,
            new NullModuleContraint());

        Assert.That(await paymentService.LoadPaymentProviderBySystemNameAsync("motorshop.iyzico", onlyWhenEnabled: true), Is.Not.Null);
        Assert.That(await paymentService.LoadPaymentProviderBySystemNameAsync("MOTORSHOP.IYZICO", onlyWhenEnabled: true), Is.Not.Null);
        Assert.That(await paymentService.IsPaymentProviderEnabledAsync("motorshop.iyzico"), Is.True);
    }

    #endregion

    #region HTML & Liquid

    [TestCase("<div> </div>")]
    [TestCase("<div><p>&nbsp;</p></div>")]
    [TestCase("<DIV></DIV>")]
    public void Empty_Html_Is_Detected(string html)
    {
        // AngleSharp reports upper-case tag names ("DIV"), which must not become "dıv".
        Assert.That(HtmlUtility.IsEmptyHtml(html), Is.True);
    }

    [Test]
    public void Non_Empty_Html_Is_Detected()
    {
        Assert.That(HtmlUtility.IsEmptyHtml("<div>İndirim</div>"), Is.False);
    }

    [TestCase("tr-TR")]
    [TestCase("en-US")]
    public async Task Liquid_Tokens_With_Capital_I_Are_Resolved(string culture)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        Template.NamingConvention = new CSharpNamingConvention();

        const string source = "{{ Order.CustomerId }}|{{ Order.OrderNumber }}|{% if Order.IsPaid %}paid{% endif %}|{{ Order.ShippingMethodId }}";
        var model = new Dictionary<string, object>
        {
            ["Order"] = new Dictionary<string, object>
            {
                ["CustomerId"] = 42,
                ["OrderNumber"] = "MTR-1001",
                ["IsPaid"] = true,
                ["ShippingMethodId"] = 7
            }
        };

        var template = new LiquidTemplate(Template.Parse(source), source);
        var result = await template.RenderAsync(model, Turkish);

        Assert.That(result, Is.EqualTo("42|MTR-1001|paid|7"));
    }

    #endregion

    public class TrCultureTestSettings : ISettings
    {
        public bool IsEnabled { get; set; }
        public int TaxCategoryId { get; set; }
    }

    public class InvoiceItem : BaseEntity, ISlugSupported
    {
        public string[] GetDisplayNameMemberNames() => [];
        public string GetDisplayName() => null;
    }
}
