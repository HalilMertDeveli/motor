using System.Text;
using System.Text.RegularExpressions;
using Smartstore.Core.Catalog.Categories;
using Smartstore.Core.Data;
using Smartstore.Core.Seo;

namespace Smartstore.Core.Installation;

/// <summary>
/// MOTOR: MRT Motors store identity and the category tree of the Motosev 2026 catalog
/// ("2026 KATALOG 12.10.2026.pdf", all 30 catalog sections).
/// Used by <see cref="TrTRSeedData"/> (fresh installs) and the ProsevStore migration (existing databases).
/// </summary>
/// <remarks>
/// Category names and paths are identical to the paths in the Motosev supplier package
/// (MotorShop.SupplierCatalog, "Main &gt; Sub"), so the importer reuses these categories instead of creating new ones.
/// Legal company title, tax number, address and IBAN are not part of the catalog and stay placeholders.
/// </remarks>
public static class ProsevStoreDefaults
{
    public const string StoreName = "MRT Motors";

    /// <summary>
    /// House brand of the supplier Motosev. The owner sells this range under the store brand
    /// (decision 2026-10-22), so it is replaced in product data, brand names and URL slugs.
    /// </summary>
    public const string SupplierBrandName = "Prosev";

    /// <summary>
    /// URL slug of the store brand (the manufacturer page of the former supplier brand).
    /// </summary>
    public const string StoreBrandSlug = "mrt-motors";

    private static readonly Regex _supplierBrandRegex = new(@"\bProsev\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex _supplierBrandSlugRegex = new(@"(?<=^|-)prosev(?=-|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Replaces the supplier brand "Prosev" (whole word, any case) with the store brand.
    /// </summary>
    public static string ReplaceSupplierBrand(string text)
        => string.IsNullOrEmpty(text) ? text : _supplierBrandRegex.Replace(text, StoreName);

    /// <summary>
    /// Replaces the slug segment "prosev" with "mrt-motors", e.g. "prosev-q7-motosiklet-interkom" → "mrt-motors-q7-motosiklet-interkom".
    /// </summary>
    public static string ReplaceSupplierBrandInSlug(string slug)
        => string.IsNullOrEmpty(slug) ? slug : _supplierBrandSlugRegex.Replace(slug, StoreBrandSlug);

    public static bool ContainsSupplierBrand(string text)
        => !string.IsNullOrEmpty(text) && _supplierBrandRegex.IsMatch(text);

    // Contact data of MRT Motors as given by the owner on 2026-10-22 (Instagram profile @mrtmotorservis).
    // Legal title, tax number, MERSİS and IBAN are not known yet and stay placeholders.
    public const string Street = "Mustafa Kemal Paşa Mahallesi, Yıldırım Beyazıt Caddesi No: 10/B";
    public const string Street2 = "Parseller";
    // Company addresses are formatted without a state, so the province is part of the city.
    public const string City = "Avcılar / İstanbul";
    public const string District = "Avcılar";
    public const string StateName = "İstanbul";
    public const string Phone = "0537 490 68 32";
    public const string PhoneHref = "tel:+905374906832";
    public const string InstagramUrl = "https://www.instagram.com/mrtmotorservis/";
    public const string InstagramHandle = "@mrtmotorservis";

    public static string FullAddress => $"{Street}, {Street2}, {City}";

    public static string MapsUrl
        => "https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString($"MRT Motors, {FullAddress}");

    private static string ContactHtml =>
        $"<p>Adres: {FullAddress}<br><a href=\"{MapsUrl}\" target=\"_blank\" rel=\"noopener\">Haritada göster</a></p>" +
        $"<p>Telefon: <a href=\"{PhoneHref}\">{Phone}</a><br>" +
        $"Instagram: <a href=\"{InstagramUrl}\" target=\"_blank\" rel=\"noopener\">{InstagramHandle}</a></p>";

    public static string ContactUsBody =>
        $"<p><strong>{StoreName}</strong> · Motosiklet servisi, yedek parça ve aksesuar</p>" + ContactHtml;

    public static string AboutUsBody =>
        $"<p><strong>{StoreName}</strong>, İstanbul Avcılar'da motosiklet servisi, yedek parça ve aksesuar hizmeti veren bir motosiklet bayisidir.</p>" +
        "<p>Kask, sürücü ekipmanı, çanta ve aksesuarları bu mağazadan sipariş edebilirsiniz. Ürün ve uyumluluk sorularınız için bize telefonla ya da Instagram üzerinden ulaşın.</p>" +
        ContactHtml;

    public static string ImprintBody =>
        $"<p><strong>{StoreName}</strong></p>" + ContactHtml +
        "<p>Firma unvanı, vergi dairesi ve numarası, MERSİS numarası: [mağaza sahibi tarafından girilecek]</p>";

    /// <summary>
    /// Whether a topic body is still the text of a fresh install ("... buraya ekleyin").
    /// </summary>
    public static bool IsPlaceholderTopicBody(string body)
        => string.IsNullOrWhiteSpace(body)
        || body.Contains("buraya ekleyin", StringComparison.OrdinalIgnoreCase)
        || body.Contains("Put your", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Store names of a fresh install or of the earlier MOTOR working name, which the migration replaces.
    /// A name an admin chose is kept.
    /// </summary>
    public static readonly string[] PlaceholderStoreNames =
    [
        "Your store name", "Mağaza adınız", "Store name", "Mağazam", "Shop", "MOTOR", "Motor", "MotorShop", "Motor Shop"
    ];

    /// <summary>
    /// Category of the catalog's campaign pages ("KAMPANYALI KASK MODELLERİ", campaign prices on other pages).
    /// The supplier importer adds campaign products to it in addition to their own category.
    /// </summary>
    public const string CampaignCategoryName = "Kampanyalı Ürünler";

    /// <summary>
    /// Top-level categories with their subcategories, in catalog order.
    /// </summary>
    public static readonly (string Name, string[] Children)[] Categories =
    [
        (CampaignCategoryName, []),
        ("Kask", ["Kapalı Kask", "Çene Açılır Kask", "Açık Kask", "Cross Kask", "Çocuk Kask"]),
        ("İnterkom", []),
        ("Çanta",
        [
            "Arka Çanta (Top Case)", "Yan Çanta", "Depo Üstü Çanta", "Kuyruk ve Sırt Çantası", "Bacak ve Bel Çantası",
            "ATV Çanta", "Bağlantı Demirleri ve Kitler", "Çanta Yedek Parça ve Aksesuar"
        ]),
        ("Sissybar ve Sırt Dayama", []),
        ("Giyim", ["Mont", "Kadın Mont", "Çocuk Mont", "Pantolon", "Yelek", "Yağmurluk"]),
        ("Bandana ve Maske", []),
        ("Eldiven", []),
        ("Koruma Ekipmanları", []),
        ("Bot", []),
        ("Kilit", []),
        ("Gözlük", []),
        ("Branda", []),
        ("Muhtelif Aksesuar",
        [
            "Telefon Tutucu", "Ayna", "Sele Kılıfı", "Elcik", "Elcik Koruma", "Gidon Aksesuarı", "Isıtmalı Ürün",
            "Bağlantı Aparatı", "Plaka Tutucu", "Şarj Cihazı", "Lastik Tamir ve Şişirme", "Alarm ve Ses Sistemi",
            "İçecek Tutucu", "Çamur Sıyırıcı", "Bagaj Filesi ve Lastiği", "Alet Takımı", "Askı ve Kanca", "Yolcu Tutamağı"
        ]),
        ("Ön Siperlik", []),
        ("Sprey ve Bakım", []),
        ("Kask Aksesuarları", ["Kask Camı", "Kask Yedek Parça", "Diğer"])
    ];

    /// <summary>
    /// Payment and sales terms as printed in the catalog (pages 1 and 128). Dealer terms only.
    /// </summary>
    public const string PaymentInfoTitle = "Ödeme seçenekleri";

    public const string PaymentInfoBody =
        "<h3>Kredi kartı ile ödeme</h3>" +
        "<p><strong>Tüm kredi kartlarına 3 taksit imkânı.</strong> Kart ödemeleri iyzico güvenli ödeme sayfasında alınır; kart bilgileriniz mağazamızda saklanmaz.</p>" +
        "<p>Taksit yapılan kart programları: Sağlam Kart, Bankkart Combo, Paraf, Advantage, CardFinans, Bonus, Axess, World, Maximum.</p>" +
        "<h3>Havale / EFT</h3>" +
        "<p>Siparişinizi havale veya EFT ile ödeyebilirsiniz. Banka hesap bilgilerimiz ödeme adımında gösterilir. Açıklama alanına sipariş numaranızı yazmayı unutmayın.</p>" +
        "<h3>Kapıda ödeme</h3>" +
        "<p>Siparişinizi teslim alırken kargo görevlisine ödeyebilirsiniz.</p>" +
        "<h3>Bayi ödeme koşulları</h3>" +
        "<ul>" +
        "<li>Satışlarımız liste tarihi itibarıyla <strong>30-60-90 gün</strong> vadelidir.</li>" +
        "<li>Peşin ödeme vadesi <strong>10 gündür</strong>.</li>" +
        "<li>Vadesinde ödenmeyen faturalara aylık <strong>%8 vade farkı</strong> uygulanır.</li>" +
        "<li>Kartla taksitli çekimlerde en az <strong>10.000 TL</strong>, tek çekimlerde en az <strong>5.000 TL</strong> çekim yapılır.</li>" +
        "<li>Bayi fiyatlarına KDV dahil değildir. Firmamız fiyatlar üzerinde değişiklik yapma hakkını saklı tutar.</li>" +
        "</ul>" +
        "<p>Ödeme planına uyulmasını tüm müşterilerimizden rica ederiz.</p>";

    /// <summary>
    /// Creates the missing catalog categories (matched by parent and name, case-insensitive) with published
    /// state, catalog order and Turkish slugs. Existing categories, also admin-edited ones, are left unchanged.
    /// </summary>
    /// <returns>Number of created categories.</returns>
    public static async Task<int> EnsureCategoriesAsync(SmartDbContext db, CancellationToken cancelToken = default)
    {
        Guard.NotNull(db);

        var templateId = await db.CategoryTemplates
            .Where(x => x.ViewPath == "CategoryTemplate.ProductsInGridOrLines")
            .Select(x => x.Id)
            .FirstOrDefaultAsync(cancelToken);

        var all = await db.Categories.ToListAsync(cancelToken);
        var slugs = (await db.UrlRecords
            .Where(x => x.LanguageId == 0)
            .Select(x => x.Slug)
            .ToListAsync(cancelToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var created = 0;
        var topLevel = new List<(Category Category, string[] Children)>();
        var order = 0;

        foreach (var (name, children) in Categories)
        {
            var category = Find(all, null, name);
            if (category == null)
            {
                category = CreateCategory(name, null, ++order, templateId, isTopLevel: true);
                db.Categories.Add(category);
                all.Add(category);
                created++;
            }
            else
            {
                ++order;
            }

            topLevel.Add((category, children));
        }

        await db.SaveChangesAsync(cancelToken);

        var newCategories = new List<Category>();
        foreach (var (parent, children) in topLevel)
        {
            var childOrder = 0;
            foreach (var name in children)
            {
                ++childOrder;
                if (Find(all, parent.Id, name) == null)
                {
                    var category = CreateCategory(name, parent.Id, childOrder, templateId, isTopLevel: false);
                    db.Categories.Add(category);
                    all.Add(category);
                    newCategories.Add(category);
                    created++;
                }
            }
        }

        await db.SaveChangesAsync(cancelToken);

        // URL records for every category created above (top-level ones have no slug yet either).
        var withoutSlug = all
            .Where(x => x.Id > 0 && (newCategories.Contains(x) || topLevel.Any(t => t.Category == x)))
            .ToList();

        var existingSlugEntityIds = await db.UrlRecords
            .Where(x => x.EntityName == nameof(Category) && x.LanguageId == 0)
            .Select(x => x.EntityId)
            .ToListAsync(cancelToken);

        foreach (var category in withoutSlug.Where(x => !existingSlugEntityIds.Contains(x.Id)))
        {
            db.UrlRecords.Add(new UrlRecord
            {
                EntityId = category.Id,
                EntityName = nameof(Category),
                LanguageId = 0,
                IsActive = true,
                Slug = UniqueSlug(BuildSlug(category.Name), slugs)
            });
        }

        await db.SaveChangesAsync(cancelToken);
        return created;
    }

    /// <summary>
    /// Turkish-aware slug, e.g. "Çene Açılır Kask" => "cene-acilir-kask".
    /// </summary>
    public static string BuildSlug(string name)
    {
        var sb = new StringBuilder(name.EmptyNull().Length);
        foreach (var c in name.EmptyNull())
        {
            sb.Append(c switch
            {
                'ç' or 'Ç' => 'c',
                'ğ' or 'Ğ' => 'g',
                'ı' or 'I' or 'İ' => 'i',
                'ö' or 'Ö' => 'o',
                'ş' or 'Ş' => 's',
                'ü' or 'Ü' => 'u',
                _ => char.ToLowerInvariant(c)
            });
        }

        return SlugUtility.Slugify(sb.ToString()).Trim('-');
    }

    private static string UniqueSlug(string slug, HashSet<string> taken)
    {
        var result = slug;
        for (var i = 2; taken.Contains(result); i++)
        {
            result = $"{slug}-{i}";
        }

        taken.Add(result);
        return result;
    }

    private static Category Find(List<Category> all, int? parentId, string name)
        => all.FirstOrDefault(x => (x.ParentId ?? 0) == (parentId ?? 0) && x.Name.Trim().EqualsNoCase(name));

    private static Category CreateCategory(string name, int? parentId, int displayOrder, int templateId, bool isTopLevel)
        => new()
        {
            Name = name,
            ParentId = parentId,
            CategoryTemplateId = templateId,
            Published = true,
            DisplayOrder = displayOrder,
            ShowOnHomePage = isTopLevel,
            MetaTitle = name
        };
}
