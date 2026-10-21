using System.Collections.Generic;
using System.Linq;
using MotorShop.AdminPanel.Models;
using MotorShop.AdminPanel.Services;
using NUnit.Framework;
using Smartstore.Core.Catalog.Attributes;
using Smartstore.Core.Catalog.Products;

namespace MotorShop.AdminPanel.Tests;

/// <summary>
/// Counting and filter rules of the panel. Translation to SQL is covered by the runtime QA on SQLite.
/// </summary>
[TestFixture]
public class PanelQueryTests
{
    private IQueryable<Product> _products;
    private IQueryable<ProductVariantAttributeCombination> _combinations;

    [SetUp]
    public void SetUp()
    {
        _products = new List<Product>
        {
            Product(1, "in-stock", ManageInventoryMethod.ManageStock, stock: 5, price: 100m),
            Product(2, "zero-stock", ManageInventoryMethod.ManageStock, stock: 0, price: 300m),
            Product(3, "negative-stock", ManageInventoryMethod.ManageStock, stock: -2, price: 50m),
            Product(4, "not-tracked", ManageInventoryMethod.DontManageStock, stock: 0, price: 200m),
            Product(5, "variants-in-stock", ManageInventoryMethod.ManageStockByAttributes, stock: 0, price: 10m),
            Product(6, "variants-sold-out", ManageInventoryMethod.ManageStockByAttributes, stock: 99, price: 20m),
            Product(7, "variants-none", ManageInventoryMethod.ManageStockByAttributes, stock: 5, price: 30m),
            Product(8, "call-for-price", ManageInventoryMethod.ManageStock, stock: 3, price: 0m, callForPrice: true),
            Product(9, "deleted", ManageInventoryMethod.ManageStock, stock: 0, price: 1m, deleted: true),
            Product(10, "system", ManageInventoryMethod.ManageStock, stock: 0, price: 1m, system: true),
            Product(11, "unpriced", ManageInventoryMethod.ManageStock, stock: 1, price: 0m)
        }.AsQueryable();

        _combinations = new List<ProductVariantAttributeCombination>
        {
            Combination(5, stock: 0, active: true),
            Combination(5, stock: 4, active: true),
            Combination(6, stock: 0, active: true),
            // An inactive combination cannot be bought, so its stock does not count.
            Combination(6, stock: 7, active: false)
        }.AsQueryable();
    }

    [Test]
    public void Countable_products_exclude_deleted_and_system_products()
    {
        var names = PanelQueryService.CountableProducts(_products).Select(x => x.Name).ToList();

        Assert.That(names, Has.Count.EqualTo(9));
        Assert.That(names, Does.Not.Contain("deleted").And.Not.Contain("system"));
    }

    [Test]
    public void Out_of_stock_counts_tracked_products_at_or_below_zero_and_sold_out_variants()
    {
        // A product managed per variant without any combination cannot be bought either.
        Assert.That(Names(StockFilter.OutOfStock),
            Is.EquivalentTo(new[] { "zero-stock", "negative-stock", "variants-sold-out", "variants-none" }));
    }

    [Test]
    public void In_stock_ignores_untracked_products_and_product_level_quantity_of_variant_products()
    {
        Assert.That(Names(StockFilter.InStock), Is.EquivalentTo(new[] { "in-stock", "variants-in-stock", "call-for-price", "unpriced" }));
    }

    [Test]
    public void Not_tracked_filter()
    {
        Assert.That(Names(StockFilter.NotTracked), Is.EquivalentTo(new[] { "not-tracked" }));
    }

    [Test]
    public void All_filter_returns_everything_countable()
    {
        Assert.That(Names(StockFilter.All), Has.Count.EqualTo(9));
    }

    [Test]
    public void Price_filters_separate_call_for_price_from_unpriced_products()
    {
        var countable = PanelQueryService.CountableProducts(_products);

        Assert.That(PanelQueryService.ApplyPriceFilter(countable, PriceVisibilityFilter.CallForPrice).Select(x => x.Name),
            Is.EquivalentTo(new[] { "call-for-price" }));

        // call-for-price has a price of 0 too, but cannot be bought, so it is no "unpriced" risk.
        Assert.That(PanelQueryService.ApplyPriceFilter(countable, PriceVisibilityFilter.ZeroPrice).Select(x => x.Name),
            Is.EquivalentTo(new[] { "unpriced" }));

        Assert.That(PanelQueryService.ApplyPriceFilter(countable, PriceVisibilityFilter.Visible).Count(), Is.EqualTo(8));
    }

    [TestCase(ProductSort.PriceAsc)]
    [TestCase(ProductSort.PriceDesc)]
    [TestCase(ProductSort.StockAsc)]
    [TestCase(ProductSort.NameAsc)]
    public void Sorting(ProductSort sort)
    {
        var sorted = PanelQueryService.ApplySorting(PanelQueryService.CountableProducts(_products), sort).ToList();

        switch (sort)
        {
            case ProductSort.PriceAsc: Assert.That(sorted.Select(x => x.Price), Is.Ordered.Ascending); break;
            case ProductSort.PriceDesc: Assert.That(sorted.Select(x => x.Price), Is.Ordered.Descending); break;
            case ProductSort.StockAsc: Assert.That(sorted.Select(x => x.StockQuantity), Is.Ordered.Ascending); break;
            case ProductSort.NameAsc: Assert.That(sorted.Select(x => x.Name), Is.Ordered.Ascending.Using((System.Collections.Generic.IComparer<string>)System.StringComparer.Ordinal)); break;
        }
    }

    private List<string> Names(StockFilter filter)
        => PanelQueryService.ApplyStockFilter(PanelQueryService.CountableProducts(_products), filter, _combinations)
            .Select(x => x.Name)
            .ToList();

    private static Product Product(int id, string name, ManageInventoryMethod method, int stock, decimal price,
        bool callForPrice = false, bool deleted = false, bool system = false)
    {
        return new Product
        {
            Id = id,
            Name = name,
            ManageInventoryMethod = method,
            StockQuantity = stock,
            Price = price,
            CallForPrice = callForPrice,
            Deleted = deleted,
            IsSystemProduct = system,
            Published = true
        };
    }

    private static ProductVariantAttributeCombination Combination(int productId, int stock, bool active)
        => new() { ProductId = productId, StockQuantity = stock, IsActive = active };
}
