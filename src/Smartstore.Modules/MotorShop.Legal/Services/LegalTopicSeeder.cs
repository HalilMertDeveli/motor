using Microsoft.EntityFrameworkCore;
using MotorShop.Legal.Configuration;
using Smartstore.Core.Content.Topics;
using Smartstore.Core.Data;
using Smartstore.Core.Seo;

namespace MotorShop.Legal.Services;

/// <summary>
/// Creates the legal topics with PLACEHOLDER texts on module installation. Existing topics are never overwritten.
/// The texts are not legal advice and must be replaced after legal review.
/// </summary>
internal class LegalTopicSeeder
{
    internal const string DraftBanner =
        "<div class=\"alert alert-warning\" role=\"note\"><strong>TASLAK – hukuk onayı gerekli.</strong> " +
        "Bu metin yer tutucudur, bağlayıcı değildir ve yayına alınmadan önce bir hukukçu tarafından hazırlanmalı veya onaylanmalıdır.</div>";

    private readonly SmartDbContext _db;
    private readonly IUrlService _urlService;

    public LegalTopicSeeder(SmartDbContext db, IUrlService urlService)
    {
        _db = db;
        _urlService = urlService;
    }

    public async Task<int> SeedAsync()
    {
        var created = 0;

        foreach (var (systemName, slug, title, body, published) in GetPlaceholders())
        {
            if (await _db.Topics.AnyAsync(x => x.SystemName == systemName))
            {
                continue;
            }

            var topic = new Topic
            {
                SystemName = systemName,
                Title = title,
                Body = body,
                // Contract documents contain Liquid tokens and are rendered with order data at checkout only.
                IsPublished = published,
                IncludeInSitemap = false,
                RenderAsWidget = false,
                EnableProseContainer = true
            };

            _db.Topics.Add(topic);
            await _db.SaveChangesAsync();

            var slugResult = await _urlService.ValidateSlugAsync(topic, slug, title, true);
            await _urlService.ApplySlugAsync(slugResult, true);

            created++;
        }

        return created;
    }

    internal static IEnumerable<(string SystemName, string Slug, string Title, string Body, bool Published)> GetPlaceholders()
    {
        yield return (LegalTopicNames.PreInformation, "on-bilgilendirme-formu", "Ön Bilgilendirme Formu", DraftBanner + PreInformationBody, false);
        yield return (LegalTopicNames.DistanceSales, "mesafeli-satis-sozlesmesi", "Mesafeli Satış Sözleşmesi", DraftBanner + DistanceSalesBody, false);
        yield return (LegalTopicNames.Withdrawal, "cayma-hakki", "Cayma Hakkı", DraftBanner + WithdrawalBody, true);
        yield return (LegalTopicNames.PrivacyNotice, "kvkk-aydinlatma-metni", "KVKK Aydınlatma Metni", DraftBanner + PrivacyNoticeBody, true);
    }

    // Shared order block. Tokens: Seller.*, Buyer.*, Order.*, Items[] (see LegalDocumentData).
    const string PartiesAndOrder = """
        <h3>Satıcı</h3>
        <p>{{ Seller.Name }}<br>Adres: {{ Seller.Address }}<br>Telefon: {{ Seller.Phone }}<br>E-posta: {{ Seller.Email }}<br>Vergi no: {{ Seller.TaxNumber }}<br>MERSİS / ticaret sicil: {{ Seller.CommercialRegister }}</p>
        <h3>Alıcı</h3>
        <p>{{ Buyer.Name }}<br>Fatura adresi: {{ Buyer.BillingAddress }}<br>Teslimat adresi: {{ Buyer.ShippingAddress }}<br>Telefon: {{ Buyer.Phone }}<br>E-posta: {{ Buyer.Email }}</p>
        <h3>Sipariş</h3>
        <p>Sipariş no: {% if Order.Number != "" %}{{ Order.Number }}{% else %}[sipariş onayında atanır]{% endif %}<br>Tarih: {{ Order.Date }}<br>Ödeme yöntemi: {{ Order.PaymentMethod }}<br>Teslimat yöntemi: {{ Order.ShippingMethod }}</p>
        <table class="table table-sm">
        <thead><tr><th>Ürün</th><th>Adet</th><th>Birim fiyat</th><th>Tutar</th></tr></thead>
        <tbody>{% for item in Items %}<tr><td>{{ item.Name }}</td><td>{{ item.Quantity }}</td><td>{{ item.UnitPrice }}</td><td>{{ item.Total }}</td></tr>{% endfor %}</tbody>
        </table>
        <p>Ara toplam: {{ Order.SubTotal }}<br>Kargo: {{ Order.Shipping }}<br>Vergiler: {{ Order.Tax }}<br><strong>Genel toplam: {{ Order.Total }}</strong></p>
        """;

    const string PreInformationBody = """
        <p>[YER TUTUCU] Bu form, 6502 sayılı Tüketicinin Korunması Hakkında Kanun ve Mesafeli Sözleşmeler Yönetmeliği kapsamında sözleşme kurulmadan önce verilmesi gereken bilgiler için ayrılmıştır. Kapsamı ve metni hukukçu tarafından belirlenecektir.</p>
        """ + PartiesAndOrder + """
        <h3>Teslimat, cayma hakkı ve şikâyet</h3>
        <p>[YER TUTUCU] Teslimat süresi, cayma hakkının kullanımı ve istisnaları, iade koşulları ile şikâyet ve itiraz yolları hukukçu tarafından yazılacaktır.</p>
        """;

    const string DistanceSalesBody = """
        <p>[YER TUTUCU] Mesafeli satış sözleşmesinin maddeleri hukukçu tarafından yazılacaktır.</p>
        """ + PartiesAndOrder + """
        <h3>Sözleşme maddeleri</h3>
        <p>[YER TUTUCU] Konu, tarafların hak ve yükümlülükleri, teslimat, cayma hakkı, uyuşmazlıkların çözümü.</p>
        """;

    const string WithdrawalBody = """
        <p>[YER TUTUCU] Cayma hakkının süresi, nasıl kullanılacağı, istisnaları ve iade süreci hukukçu tarafından yazılacaktır.</p>
        """;

    const string PrivacyNoticeBody = """
        <p>[YER TUTUCU] 6698 sayılı Kişisel Verilerin Korunması Kanunu kapsamındaki aydınlatma metni (veri sorumlusu, işleme amaçları, hukuki sebepler, aktarım, haklar) hukukçu tarafından yazılacaktır. Bu metin bir açık rıza metni değildir.</p>
        """;
}
