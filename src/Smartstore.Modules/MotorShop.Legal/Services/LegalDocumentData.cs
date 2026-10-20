using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace MotorShop.Legal.Services;

/// <summary>
/// Order data available to the legal topic templates. Built from the cart at the confirm step
/// and from the placed order afterwards, so both renderings use the same tokens.
/// </summary>
public class LegalDocumentData
{
    // Escapes markup characters only, Turkish letters stay readable in the stored snapshot.
    private static readonly HtmlEncoder _encoder = HtmlEncoder.Create(UnicodeRanges.All);

    public string SellerName { get; set; }
    public string SellerAddress { get; set; }
    public string SellerPhone { get; set; }
    public string SellerEmail { get; set; }
    public string SellerTaxNumber { get; set; }
    public string SellerVatId { get; set; }
    public string SellerCommercialRegister { get; set; }
    public string SellerUrl { get; set; }

    public string BuyerName { get; set; }
    public string BuyerEmail { get; set; }
    public string BuyerPhone { get; set; }
    public string BillingAddress { get; set; }
    public string ShippingAddress { get; set; }

    /// <summary>
    /// Empty at the confirm step, the order number is only known after placement.
    /// </summary>
    public string OrderNumber { get; set; }
    public DateTime Date { get; set; }
    public string PaymentMethod { get; set; }
    public string ShippingMethod { get; set; }

    public string SubTotal { get; set; }
    public string Shipping { get; set; }
    public string Tax { get; set; }
    public string Total { get; set; }

    public List<LegalDocumentItem> Items { get; set; } = [];

    /// <summary>
    /// Creates the Liquid model. Every value is HTML encoded because the rendered
    /// document is shown as HTML to the customer and to administrators.
    /// </summary>
    public IDictionary<string, object> ToLiquidModel(IFormatProvider formatProvider = null)
    {
        static string E(string value) => _encoder.Encode(value ?? string.Empty);

        return new Dictionary<string, object>
        {
            ["Seller"] = new Dictionary<string, object>
            {
                ["Name"] = E(SellerName),
                ["Address"] = E(SellerAddress),
                ["Phone"] = E(SellerPhone),
                ["Email"] = E(SellerEmail),
                ["TaxNumber"] = E(SellerTaxNumber),
                ["VatId"] = E(SellerVatId),
                ["CommercialRegister"] = E(SellerCommercialRegister),
                ["Url"] = E(SellerUrl)
            },
            ["Buyer"] = new Dictionary<string, object>
            {
                ["Name"] = E(BuyerName),
                ["Email"] = E(BuyerEmail),
                ["Phone"] = E(BuyerPhone),
                ["BillingAddress"] = E(BillingAddress),
                ["ShippingAddress"] = E(ShippingAddress)
            },
            ["Order"] = new Dictionary<string, object>
            {
                ["Number"] = E(OrderNumber),
                ["Date"] = E(Date.ToString("d", formatProvider)),
                ["PaymentMethod"] = E(PaymentMethod),
                ["ShippingMethod"] = E(ShippingMethod),
                ["SubTotal"] = E(SubTotal),
                ["Shipping"] = E(Shipping),
                ["Tax"] = E(Tax),
                ["Total"] = E(Total)
            },
            ["Items"] = Items
                .Select(x => (object)new Dictionary<string, object>
                {
                    ["Name"] = E(x.Name),
                    ["Sku"] = E(x.Sku),
                    ["Quantity"] = x.Quantity,
                    ["UnitPrice"] = E(x.UnitPrice),
                    ["Total"] = E(x.Total)
                })
                .ToList()
        };
    }
}

public class LegalDocumentItem
{
    public string Name { get; set; }
    public string Sku { get; set; }
    public int Quantity { get; set; }
    public string UnitPrice { get; set; }
    public string Total { get; set; }
}
