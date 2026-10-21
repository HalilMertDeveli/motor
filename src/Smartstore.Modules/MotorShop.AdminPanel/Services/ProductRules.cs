using Smartstore.Core.Catalog.Products;

namespace MotorShop.AdminPanel.Services;

/// <summary>
/// Panel rules on top of Smartstore's own validation. They keep a product from becoming purchasable for 0
/// and keep the sale price meaningful. Only rules touching the changed fields are checked, so an old record
/// that already breaks a rule can still be fixed field by field.
/// </summary>
public static class ProductRules
{
    public const int MaxStock = 1_000_000;

    public static class Fields
    {
        public const string Price = "price";
        public const string SpecialPrice = "specialPrice";
        public const string Stock = "stock";
        public const string CallForPrice = "callForPrice";
        public const string Published = "published";
    }

    /// <returns>Resource key of the first violated rule, or <c>null</c>.</returns>
    public static string Validate(Product p, params string[] changedFields)
    {
        bool Changed(params string[] fields) => changedFields.Length == 0 || fields.Any(changedFields.Contains);

        if (Changed(Fields.Price) && p.Price < 0)
        {
            return "Rule.PriceNegative";
        }

        if (Changed(Fields.Price, Fields.SpecialPrice) && p.SpecialPrice.HasValue && (p.SpecialPrice <= 0 || p.SpecialPrice >= p.Price))
        {
            return "Rule.SpecialPrice";
        }

        if (Changed(Fields.Price, Fields.CallForPrice, Fields.Published) && p.Published && !p.CallForPrice && p.Price <= 0)
        {
            return "Rule.ZeroPriceSellable";
        }

        if (Changed(Fields.Stock) && p.ManageInventoryMethod == ManageInventoryMethod.ManageStock && (p.StockQuantity < 0 || p.StockQuantity > MaxStock))
        {
            return "Rule.Stock";
        }

        return null;
    }
}
