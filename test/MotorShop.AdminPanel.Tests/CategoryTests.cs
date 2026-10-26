using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Moq;
using MotorShop.AdminPanel.Models;
using MotorShop.AdminPanel.Services;
using NUnit.Framework;
using Smartstore.Core;
using Smartstore.Core.Catalog.Categories;
using Smartstore.Core.Content.Media;
using Smartstore.Core.Logging;
using Smartstore.Core.Seo;

namespace MotorShop.AdminPanel.Tests;

[TestFixture]
public class CategoryNameTests
{
    [TestCase("Kask", "kask")]
    [TestCase("  Açık   Kask ", "açık kask")]
    [TestCase("IŞIK", "ışık")]
    [TestCase("İç Giyim", "iç giyim")]
    public void Same_name_in_turkish(string a, string b)
        => Assert.That(CategoryEditService.SameName(a, b), Is.True);

    [TestCase("Kask", "Kasklar")]
    [TestCase("Işık", "Isik")]
    public void Different_names(string a, string b)
        => Assert.That(CategoryEditService.SameName(a, b), Is.False);

    [Test]
    public void Picker_options_carry_the_full_path_sorted()
    {
        var options = CategoryEditService.BuildPickerOptions(
        [
            (1, null, "Kask", true),
            (2, 1, "Açık Kask", true),
            (3, null, "Aksesuar", false),
            (4, 3, "Kask", true)
        ]);

        Assert.That(options.Select(x => x.Path), Is.EqualTo(new[] { "Aksesuar", "Aksesuar › Kask", "Kask", "Kask › Açık Kask" }));
        Assert.That(options.Single(x => x.Id == 3).Published, Is.False);
    }
}

/// <summary>
/// Category writes through the real service on SQLite.
/// </summary>
[TestFixture]
public class CategoryServiceTests : PanelDbTestBase
{
    private CategoryEditService CreateCategoryService(Smartstore.Core.Data.SmartDbContext db = null)
    {
        var services = new Mock<ICommonServices>();
        services.SetupGet(x => x.ActivityLogger).Returns(Mock.Of<IActivityLogger>());

        var media = new Mock<IMediaService>();
        media.Setup(x => x.GetFilesByIdsAsync(It.IsAny<int[]>(), It.IsAny<MediaLoadFlags>())).ReturnsAsync([]);

        return new CategoryEditService(db ?? Db, services.Object, Mock.Of<ICategoryService>(), media.Object, Mock.Of<IUrlService>());
    }

    // What the editor form posts back (LoadEditorAsync also reads the slug through the engine).
    private static CategoryEditorModel Editor(Category category, string name)
        => new() { Id = category.Id, Version = category.UpdatedOnUtc.Ticks, Name = name, ParentId = category.ParentId ?? 0, Published = category.Published };

    [Test]
    public async Task Quick_create_adds_a_published_category_under_the_parent()
    {
        var kask = await AddCategoryAsync("Kask");

        var result = await CreateCategoryService().QuickCreateAsync("  Açık  Kask ", kask.Id);

        Assert.That(result.Success && result.Created, Is.True, result.Error);
        Assert.That(result.Category.Path, Is.EqualTo("Kask › Açık Kask"));
        using var read = NewContext();
        var saved = await read.Categories.SingleAsync(x => x.Id == result.Category.Id);
        Assert.That(saved.Name, Is.EqualTo("Açık Kask"));
        Assert.That(saved.ParentId, Is.EqualTo(kask.Id));
        Assert.That(saved.Published, Is.True);
    }

    [Test]
    public async Task Quick_create_returns_the_existing_category_instead_of_a_duplicate()
    {
        var kask = await AddCategoryAsync("Kask");
        var acik = await AddCategoryAsync("Açık Kask", kask.Id);

        var result = await CreateCategoryService().QuickCreateAsync("AÇIK KASK", kask.Id);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Created, Is.False);
        Assert.That(result.Category.Id, Is.EqualTo(acik.Id));
        Assert.That(result.Message, Is.EqualTo(Key("Category.Exists")));
        Assert.That(await Db.Categories.CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task Same_name_under_another_parent_is_allowed()
    {
        var kask = await AddCategoryAsync("Kask");
        var aksesuar = await AddCategoryAsync("Aksesuar");
        await AddCategoryAsync("Vizör", kask.Id);

        var result = await CreateCategoryService().QuickCreateAsync("Vizör", aksesuar.Id);

        Assert.That(result.Created, Is.True);
    }

    [Test]
    public async Task Quick_create_refuses_empty_names_and_unknown_parents()
    {
        var service = CreateCategoryService();

        Assert.That((await service.QuickCreateAsync("  ", 0)).Error, Is.EqualTo(Key("Category.NameRequired")));
        Assert.That((await service.QuickCreateAsync("Kask", 9999)).Error, Is.EqualTo(Key("Category.ParentMissing")));
        Assert.That(await Db.Categories.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task Rename_keeps_the_category_and_refuses_a_sibling_name()
    {
        var kask = await AddCategoryAsync("Kask");
        var mont = await AddCategoryAsync("Mont");
        var service = CreateCategoryService();

        var rename = Editor(mont, "Montlar");
        var ok = await service.SaveAsync(rename, null);
        Assert.That(ok, Is.Empty);

        using var fresh = NewContext();
        var clash = Editor(await fresh.Categories.AsNoTracking().SingleAsync(x => x.Id == mont.Id), " kask ");
        var errors = await CreateCategoryService(NewContext()).SaveAsync(clash, null);

        Assert.That(errors["Name"], Is.EqualTo(Key("Category.Duplicate")));
        using var read = NewContext();
        Assert.That((await read.Categories.SingleAsync(x => x.Id == mont.Id)).Name, Is.EqualTo("Montlar"));
        Assert.That(await read.Categories.CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task A_category_cannot_move_below_itself_or_a_descendant()
    {
        var kask = await AddCategoryAsync("Kask");
        var acik = await AddCategoryAsync("Açık Kask", kask.Id);
        var service = CreateCategoryService();

        var model = Editor(kask, "Kask");
        model.ParentId = acik.Id;
        var errors = await service.SaveAsync(model, null);

        Assert.That(errors["ParentId"], Is.EqualTo(Key("Category.CircularParent")));
        await service.PrepareEditorAsync(model);
        Assert.That(model.ParentOptions.Select(x => x.Id), Does.Not.Contain(kask.Id).And.Not.Contain(acik.Id));
    }

    [Test]
    public async Task Picker_lists_new_categories_at_once_and_skips_deleted_ones()
    {
        await AddCategoryAsync("Kask");
        await AddCategoryAsync("Silinmiş", deleted: true);
        var service = CreateCategoryService();
        await service.QuickCreateAsync("Mont", 0);

        var options = await service.GetPickerOptionsAsync();

        Assert.That(options.Select(x => x.Name), Is.EqualTo(new[] { "Kask", "Mont" }));
    }

    [Test]
    public async Task Product_assigned_to_a_quick_category_is_listed_there()
    {
        var created = await CreateCategoryService().QuickCreateAsync("Yağmurluk", 0);
        var product = await CreateService().CreateAsync(Quick("Yağmurluk", created.Category.Id, publish: true), [], 1);

        using var read = NewContext();
        Assert.That(await read.ProductCategories.Where(x => x.CategoryId == created.Category.Id).Select(x => x.ProductId).ToListAsync(),
            Is.EqualTo(new[] { product.ProductId }));
    }
}
