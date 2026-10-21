using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Smartstore.Core.Catalog.Categories;
using Smartstore.Core.Installation;

namespace Smartstore.Core.Tests.Platform.Installation;

/// <summary>
/// MOTOR: sample data must install on top of the alias-less Prosev catalog tree (PopulateCategories crashed with a null key).
/// </summary>
[TestFixture]
public class SeedCategoriesByAliasTests : ServiceTestBase
{
    private readonly List<Category> _categories =
    [
        new() { Name = "Kask", Alias = null },
        new() { Name = "İnterkom", Alias = "" },
        new() { Name = "Fashion", Alias = "Fashion" },
        new() { Name = "Sports", Alias = "Sports" }
    ];

    [OneTimeSetUp]
    public new async Task SetUp()
    {
        await DbContext.Categories.AddRangeAsync(_categories);
        await DbContext.SaveChangesAsync();
    }

    [OneTimeTearDown]
    public async Task Teardown()
    {
        DbContext.Categories.RemoveRange(_categories);
        await DbContext.SaveChangesAsync();
    }

    [Test]
    public void Categories_without_alias_are_skipped()
    {
        var categories = InvariantSeedData.GetCategoriesByAlias(DbContext);

        Assert.That(categories.Keys, Does.Contain("Fashion").And.Contain("Sports"));
        Assert.That(categories.Keys.Any(string.IsNullOrEmpty), Is.False);
        Assert.That(categories.Values.Select(x => x.Name), Does.Not.Contain("Kask").And.Not.Contain("İnterkom"));
        Assert.That(categories["Fashion"].Name, Is.EqualTo("Fashion"));
    }
}
