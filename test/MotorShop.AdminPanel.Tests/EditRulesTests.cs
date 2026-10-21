using System.Linq;
using MotorShop.AdminPanel.Services;
using NUnit.Framework;
using Smartstore.Core.Catalog.Products;

namespace MotorShop.AdminPanel.Tests;

[TestFixture]
public class AmountParserTests
{
    [TestCase("1.250,50", 1250.50)]
    [TestCase("9.999,00", 9999.00)]
    [TestCase("1250,5", 1250.5)]
    [TestCase("1250.50", 1250.50)]
    [TestCase("12.5", 12.5)]
    [TestCase("1.250", 1250)]
    [TestCase("1.250.000", 1250000)]
    [TestCase("1.250.000,99", 1250000.99)]
    [TestCase(" 0 ", 0)]
    [TestCase("₺ 1.250,50", 1250.50)]
    [TestCase("1.250,50 TL", 1250.50)]
    [TestCase("1 250,50", 1250.50)]
    public void Parses_turkish_and_invariant_notation(string input, decimal expected)
    {
        Assert.That(AmountParser.TryParse(input, out var amount, out var error), Is.True, error);
        Assert.That(amount, Is.EqualTo(expected));
    }

    [TestCase(null, "Empty")]
    [TestCase("", "Empty")]
    [TestCase("-5", "Negative")]
    [TestCase("abc", "Invalid")]
    [TestCase("1,2,3", "Invalid")]
    [TestCase("12.34,5", "Invalid")]
    [TestCase("1.25.5", "Invalid")]
    [TestCase(",5", "Invalid")]
    [TestCase("5,", "Invalid")]
    [TestCase("1,255", "TooManyDecimals")]
    [TestCase("1.2345", "TooManyDecimals")]
    [TestCase("1e5", "Invalid")]
    [TestCase("100.000.000,01", "TooLarge")]
    public void Rejects_invalid_amounts(string input, string expectedError)
    {
        Assert.That(AmountParser.TryParse(input, out _, out var error), Is.False);
        Assert.That(error, Is.EqualTo(expectedError));
    }

    [Test]
    public void Measures_allow_four_decimals()
    {
        Assert.That(AmountParser.TryParse("1,2345", 4, out var value, out _), Is.True);
        Assert.That(value, Is.EqualTo(1.2345m));
    }

    [TestCase("10", 10)]
    [TestCase("-7,5", -7.5)]
    [TestCase("12.5%", 12.5)]
    public void Parses_percent(string input, decimal expected)
    {
        Assert.That(AmountParser.TryParsePercent(input, out var percent), Is.True);
        Assert.That(percent, Is.EqualTo(expected));
    }

    [TestCase("0")]
    [TestCase("-90.5")]
    [TestCase("1001")]
    [TestCase("x")]
    public void Rejects_out_of_range_percent(string input)
        => Assert.That(AmountParser.TryParsePercent(input, out _), Is.False);

    [TestCase("+50", 50)]
    [TestCase("-25,50", -25.50)]
    [TestCase("1.000", 1000)]
    public void Parses_signed_amounts(string input, decimal expected)
    {
        Assert.That(AmountParser.TryParseSigned(input, out var amount), Is.True);
        Assert.That(amount, Is.EqualTo(expected));
    }

    [Test]
    public void Rounds_half_away_from_zero()
    {
        Assert.That(AmountParser.RoundPrice(10.005m), Is.EqualTo(10.01m));
        Assert.That(AmountParser.RoundPrice(110m * 1.1m), Is.EqualTo(121m));
    }
}

[TestFixture]
public class ProductRulesTests
{
    private static Product Sellable() => new()
    {
        Name = "p",
        Price = 100m,
        Published = true,
        ManageInventoryMethod = ManageInventoryMethod.ManageStock,
        StockQuantity = 5
    };

    [Test]
    public void Valid_product_passes()
        => Assert.That(ProductRules.Validate(Sellable()), Is.Null);

    [Test]
    public void Published_product_cannot_be_sold_for_zero()
    {
        var p = Sellable();
        p.Price = 0;

        Assert.That(ProductRules.Validate(p, ProductRules.Fields.Price), Is.EqualTo("Rule.ZeroPriceSellable"));

        p.CallForPrice = true;
        Assert.That(ProductRules.Validate(p, ProductRules.Fields.Price), Is.Null, "call for price cannot be bought");

        p.CallForPrice = false;
        p.Published = false;
        Assert.That(ProductRules.Validate(p, ProductRules.Fields.Price), Is.Null, "unpublished cannot be bought");
    }

    [Test]
    public void Showing_the_price_or_publishing_an_unpriced_product_is_refused()
    {
        var p = Sellable();
        p.Price = 0;

        Assert.That(ProductRules.Validate(p, ProductRules.Fields.CallForPrice), Is.EqualTo("Rule.ZeroPriceSellable"));
        Assert.That(ProductRules.Validate(p, ProductRules.Fields.Published), Is.EqualTo("Rule.ZeroPriceSellable"));
    }

    [Test]
    public void Unrelated_field_edit_is_not_blocked_by_an_old_violation()
    {
        var p = Sellable();
        p.Price = 0;

        Assert.That(ProductRules.Validate(p, ProductRules.Fields.Stock), Is.Null);
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(100)]
    [TestCase(150)]
    public void Sale_price_must_be_positive_and_below_price(decimal special)
    {
        var p = Sellable();
        p.SpecialPrice = special;

        Assert.That(ProductRules.Validate(p, ProductRules.Fields.SpecialPrice), Is.EqualTo("Rule.SpecialPrice"));
    }

    [Test]
    public void Lowering_the_price_below_the_sale_price_is_refused()
    {
        var p = Sellable();
        p.SpecialPrice = 80m;
        Assert.That(ProductRules.Validate(p, ProductRules.Fields.SpecialPrice), Is.Null);

        p.Price = 70m;
        Assert.That(ProductRules.Validate(p, ProductRules.Fields.Price), Is.EqualTo("Rule.SpecialPrice"));
    }

    [TestCase(-1)]
    [TestCase(1_000_001)]
    public void Stock_range(int stock)
    {
        var p = Sellable();
        p.StockQuantity = stock;

        Assert.That(ProductRules.Validate(p, ProductRules.Fields.Stock), Is.EqualTo("Rule.Stock"));
    }

    [Test]
    public void Negative_price_is_refused()
    {
        var p = Sellable();
        p.Price = -1;

        Assert.That(ProductRules.Validate(p, ProductRules.Fields.Price), Is.EqualTo("Rule.PriceNegative"));
    }

    [Test]
    public void Inline_values_use_invariant_culture()
    {
        var p = Sellable();
        p.Price = 1250.5m;
        p.SpecialPrice = null;

        Assert.That(ProductEditService.CurrentValue(p, ProductRules.Fields.Price), Is.EqualTo("1250.5"));
        Assert.That(ProductEditService.CurrentValue(p, ProductRules.Fields.SpecialPrice), Is.EqualTo(string.Empty));
        Assert.That(ProductEditService.CurrentValue(p, ProductRules.Fields.Published), Is.EqualTo("true"));
        Assert.That(ProductEditService.FormatAmount(1250.5m), Is.EqualTo("1.250,50"));
    }
}

[TestFixture]
public class CategoryTreeRulesTests
{
    // 1 ─ 2 ─ 3
    //   └ 4
    // 5
    private static readonly (int Id, int? ParentId)[] Nodes = [(1, null), (2, 1), (3, 2), (4, 1), (5, null)];

    [Test]
    public void Subtree_contains_all_descendants()
        => Assert.That(CategoryEditService.Subtree(1, Nodes), Is.EquivalentTo(new[] { 1, 2, 3, 4 }));

    [TestCase(1, 1, false)]
    [TestCase(1, 3, false)]
    [TestCase(2, 3, false)]
    [TestCase(3, 1, true)]
    [TestCase(1, 5, true)]
    [TestCase(1, 0, true)]
    [TestCase(0, 3, true)]
    public void Parent_cannot_be_self_or_descendant(int categoryId, int parentId, bool valid)
        => Assert.That(CategoryEditService.IsValidParent(categoryId, parentId, Nodes), Is.EqualTo(valid));

    [Test]
    public void Corrupt_cycle_in_old_data_does_not_hang()
    {
        (int, int?)[] cyclic = [(1, 2), (2, 1)];
        Assert.That(CategoryEditService.Subtree(1, cyclic).Count(), Is.EqualTo(2));
    }
}
