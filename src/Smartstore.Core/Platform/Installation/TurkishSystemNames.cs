namespace Smartstore.Core.Installation;

/// <summary>
/// MOTOR: Turkish names of system entities that the admin shows by name (activity log types, scheduled tasks,
/// templates, currencies, measures, customer roles, quantity units).
/// Each entry maps a stable key to the English default name and its Turkish name. The English name lets a
/// migration rename only values that nobody has changed.
/// </summary>
public static class TurkishSystemNames
{
    /// <summary>Key: <c>ActivityLogType.SystemKeyword</c>.</summary>
    public static IReadOnlyDictionary<string, (string En, string Tr)> ActivityLogTypes { get; } = new Dictionary<string, (string, string)>
    {
        ["AddNewCategory"] = ("Add a new category", "Yeni kategori eklendi"),
        ["AddNewCheckoutAttribute"] = ("Add a new checkout attribute", "Yeni ödeme özelliği eklendi"),
        ["AddNewCustomer"] = ("Add a new customer", "Yeni müşteri eklendi"),
        ["AddNewCustomerRole"] = ("Add a new customer role", "Yeni müşteri rolü eklendi"),
        ["AddNewDiscount"] = ("Add a new discount", "Yeni indirim eklendi"),
        ["AddNewGiftCard"] = ("Add a new gift card", "Yeni hediye kartı eklendi"),
        ["AddNewManufacturer"] = ("Add a new manufacturer", "Yeni üretici eklendi"),
        ["AddNewProduct"] = ("Add a new product", "Yeni ürün eklendi"),
        ["AddNewProductAttribute"] = ("Add a new product attribute", "Yeni ürün özelliği eklendi"),
        ["AddNewSetting"] = ("Add a new setting", "Yeni ayar eklendi"),
        ["AddNewSpecAttribute"] = ("Add a new specification attribute", "Yeni açıklama özelliği eklendi"),
        ["AddNewWidget"] = ("Add a new widget", "Yeni widget eklendi"),
        ["DeleteCategory"] = ("Delete category", "Kategori silindi"),
        ["DeleteCheckoutAttribute"] = ("Delete a checkout attribute", "Ödeme özelliği silindi"),
        ["DeleteCustomer"] = ("Delete a customer", "Müşteri silindi"),
        ["DeleteCustomerRole"] = ("Delete a customer role", "Müşteri rolü silindi"),
        ["DeleteDiscount"] = ("Delete a discount", "İndirim silindi"),
        ["DeleteGiftCard"] = ("Delete a gift card", "Hediye kartı silindi"),
        ["DeleteManufacturer"] = ("Delete a manufacturer", "Üretici silindi"),
        ["DeleteProduct"] = ("Delete a product", "Ürün silindi"),
        ["DeleteProductAttribute"] = ("Delete a product attribute", "Ürün özelliği silindi"),
        ["DeleteReturnCase"] = ("Delete a return", "İade talebi silindi"),
        ["DeleteSetting"] = ("Delete a setting", "Ayar silindi"),
        ["DeleteSpecAttribute"] = ("Delete a specification attribute", "Açıklama özelliği silindi"),
        ["DeleteWidget"] = ("Delete a widget", "Widget silindi"),
        ["EditCategory"] = ("Edit category", "Kategori düzenlendi"),
        ["EditCheckoutAttribute"] = ("Edit a checkout attribute", "Ödeme özelliği düzenlendi"),
        ["EditCustomer"] = ("Edit a customer", "Müşteri düzenlendi"),
        ["EditCustomerRole"] = ("Edit a customer role", "Müşteri rolü düzenlendi"),
        ["EditDiscount"] = ("Edit a discount", "İndirim düzenlendi"),
        ["EditGiftCard"] = ("Edit a gift card", "Hediye kartı düzenlendi"),
        ["EditManufacturer"] = ("Edit a manufacturer", "Üretici düzenlendi"),
        ["EditProduct"] = ("Edit a product", "Ürün düzenlendi"),
        ["EditProductAttribute"] = ("Edit a product attribute", "Ürün özelliği düzenlendi"),
        ["EditPromotionProviders"] = ("Edit promotion providers", "Promosyon sağlayıcıları düzenlendi"),
        ["EditReturnCase"] = ("Edit a return", "İade talebi düzenlendi"),
        ["EditSettings"] = ("Edit setting(s)", "Ayarlar düzenlendi"),
        ["EditSpecAttribute"] = ("Edit a specification attribute", "Açıklama özelliği düzenlendi"),
        ["EditWidget"] = ("Edit a widget", "Widget düzenlendi"),
        ["EditThemeVars"] = ("Edit theme variables", "Tema değişkenleri düzenlendi"),
        ["EditOrder"] = ("Edit an order", "Sipariş düzenlendi"),
        ["ResetThemeVars"] = ("Reset theme variables to defaults", "Tema değişkenleri varsayılana sıfırlandı"),
        ["ImportThemeVars"] = ("Import theme variables", "Tema değişkenleri içe aktarıldı"),
        ["ExportThemeVars"] = ("Export theme variables", "Tema değişkenleri dışa aktarıldı"),
        ["PublicStore.ViewCategory"] = ("Public store. View a category", "Mağaza: kategori görüntülendi"),
        ["PublicStore.ViewManufacturer"] = ("Public store. View a manufacturer", "Mağaza: üretici görüntülendi"),
        ["PublicStore.ViewProduct"] = ("Public store. View a product", "Mağaza: ürün görüntülendi"),
        ["PublicStore.PlaceOrder"] = ("Public store. Place an order", "Mağaza: sipariş verildi"),
        ["PublicStore.ContactUs"] = ("Public store. Use contact us form", "Mağaza: iletişim formu kullanıldı"),
        ["PublicStore.AddToCompareList"] = ("Public store. Add to compare list", "Mağaza: karşılaştırma listesine eklendi"),
        ["PublicStore.AddToShoppingCart"] = ("Public store. Add to shopping cart", "Mağaza: sepete eklendi"),
        ["PublicStore.AddToWishlist"] = ("Public store. Add to wishlist", "Mağaza: favori listesine eklendi"),
        ["PublicStore.Login"] = ("Public store. Login", "Mağaza: giriş yapıldı"),
        ["PublicStore.Logout"] = ("Public store. Logout", "Mağaza: çıkış yapıldı"),
        ["PublicStore.AddProductReview"] = ("Public store. Add product review", "Mağaza: ürün değerlendirmesi eklendi"),
        ["PublicStore.AddNewsComment"] = ("Public store. Add news comment", "Mağaza: haber yorumu eklendi"),
        ["PublicStore.AddBlogComment"] = ("Public store. Add blog comment", "Mağaza: blog yorumu eklendi")
    };

    /// <summary>Key: <c>TaskDescriptor.Type</c>.</summary>
    public static IReadOnlyDictionary<string, (string En, string Tr)> ScheduleTasks { get; } = new Dictionary<string, (string, string)>
    {
        ["QueuedMessagesSendTask"] = ("Send emails", "E-postaları gönder"),
        ["QueuedMessagesClearTask"] = ("Clear email queue", "E-posta kuyruğunu temizle"),
        ["DeleteGuestsTask"] = ("Delete guests", "Misafir müşterileri sil"),
        ["DeleteLogsTask"] = ("Delete logs", "Günlükleri sil"),
        ["ClearCacheTask"] = ("Clear cache", "Önbelleği temizle"),
        ["UpdateExchangeRateTask"] = ("Update currency exchange rates", "Döviz kurlarını güncelle"),
        ["TransientMediaClearTask"] = ("Clear transient uploads", "Geçici yüklemeleri temizle"),
        ["TempFileCleanupTask"] = ("Cleanup temporary files", "Geçici dosyaları temizle"),
        ["RebuildXmlSitemapTask"] = ("Rebuild XML Sitemap", "XML site haritasını yeniden oluştur"),
        ["TargetGroupEvaluatorTask"] = ("Update assignments of customers to customer roles", "Müşterilerin rol atamalarını güncelle"),
        ["ProductRuleEvaluatorTask"] = ("Update assignments of products to categories", "Ürünlerin kategori atamalarını güncelle")
    };

    /// <summary>Key: <c>ViewPath</c> of product, category and manufacturer templates.</summary>
    public static IReadOnlyDictionary<string, (string En, string Tr)> Templates { get; } = new Dictionary<string, (string, string)>
    {
        ["Product"] = ("Default Product Template", "Varsayılan ürün şablonu"),
        ["CategoryTemplate.ProductsInGridOrLines"] = ("Products in Grid or Lines", "Ürünler ızgara veya liste görünümünde"),
        ["ManufacturerTemplate.ProductsInGridOrLines"] = ("Products in Grid or Lines", "Ürünler ızgara veya liste görünümünde")
    };

    /// <summary>Key: <c>Currency.CurrencyCode</c>.</summary>
    public static IReadOnlyDictionary<string, (string En, string Tr)> Currencies { get; } = new Dictionary<string, (string, string)>
    {
        ["USD"] = ("US Dollar", "ABD Doları"),
        ["GBP"] = ("British Pound", "İngiliz Sterlini"),
        ["AUD"] = ("Australian Dollar", "Avustralya Doları"),
        ["CAD"] = ("Canadian Dollar", "Kanada Doları"),
        ["CHF"] = ("Swiss Franc", "İsviçre Frangı"),
        ["CNY"] = ("Chinese Yuan", "Çin Yuanı"),
        ["HKD"] = ("Hong Kong Dollar", "Hong Kong Doları"),
        ["JPY"] = ("Japanese Yen", "Japon Yeni"),
        ["RUB"] = ("Russian Ruble", "Rus Rublesi"),
        ["SEK"] = ("Swedish Krona", "İsveç Kronu")
    };

    /// <summary>Key: <c>SystemKeyword</c> of dimensions and weights.</summary>
    public static IReadOnlyDictionary<string, (string En, string Tr)> Measures { get; } = new Dictionary<string, (string, string)>
    {
        ["mm"] = ("millimetre", "milimetre"),
        ["cm"] = ("centimetre", "santimetre"),
        ["m"] = ("meter", "metre"),
        ["inch"] = ("in", "inç"),
        ["ft"] = ("feet", "fit"),
        ["oz"] = ("ounce", "ons"),
        ["lb"] = ("lb", "libre"),
        ["kg"] = ("kg", "kilogram"),
        ["g"] = ("gram", "gram"),
        ["l"] = ("liter", "litre"),
        ["ml"] = ("milliliter", "mililitre")
    };

    /// <summary>Key: <c>CustomerRole.SystemName</c>.</summary>
    public static IReadOnlyDictionary<string, (string En, string Tr)> CustomerRoles { get; } = new Dictionary<string, (string, string)>
    {
        ["Administrators"] = ("Administrators", "Yöneticiler"),
        ["ForumModerators"] = ("Forum Moderators", "Forum moderatörleri"),
        ["Registered"] = ("Registered", "Kayıtlı"),
        ["Guests"] = ("Guests", "Misafirler"),
        ["InactiveNewCustomers"] = ("Inactive new customers", "Aktif olmayan yeni müşteriler")
    };

    /// <summary>Key: English <c>QuantityUnit.Name</c>. Turkish has no separate plural here.</summary>
    public static IReadOnlyDictionary<string, string> QuantityUnits { get; } = new Dictionary<string, string>
    {
        ["Piece"] = "Adet",
        ["Box"] = "Kutu",
        ["Parcel"] = "Koli",
        ["Palette"] = "Palet",
        ["Unit"] = "Birim",
        ["Sack"] = "Çuval",
        ["Bag"] = "Poşet",
        ["Can"] = "Teneke",
        ["Packet"] = "Paket",
        ["Bar"] = "Çubuk",
        ["Bottle"] = "Şişe",
        ["Glass"] = "Kavanoz",
        ["Bunch"] = "Demet",
        ["Roll"] = "Rulo",
        ["Cup"] = "Bardak",
        ["Bundle"] = "Deste",
        ["Barrel"] = "Varil",
        ["Set"] = "Set",
        ["Bucket"] = "Kova"
    };
}
