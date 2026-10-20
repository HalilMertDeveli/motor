using System.Text;
using Smartstore.Core.Catalog.Categories;
using Smartstore.Core.Data;
using Smartstore.Core.Seo;

namespace Smartstore.Core.Installation;

/// <summary>
/// MOTOR: Prosev store identity and the category tree of the Motosev 2026 catalog
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
    public const string StoreName = "Prosev";

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
