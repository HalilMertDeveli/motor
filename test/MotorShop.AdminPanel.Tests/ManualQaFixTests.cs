using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Moq;
using MotorShop.AdminPanel.Controllers;
using MotorShop.AdminPanel.Services;
using NUnit.Framework;
using Smartstore.Core;
using Smartstore.Core.Catalog.Categories;
using Smartstore.Core.Content.Media;
using Smartstore.Core.Localization;
using Smartstore.Core.Logging;
using Smartstore.Core.Seo;

namespace MotorShop.AdminPanel.Tests;

/// <summary>
/// Findings of the manual QA pass (2026-10-27).
/// </summary>
[TestFixture]
public class SearchTermTests
{
    [TestCase("shad sh58", new[] { "shad", "sh58" })]
    [TestCase("  shad   sh58  ", new[] { "shad", "sh58" })]
    [TestCase("SHAD shad", new[] { "SHAD" })]
    [TestCase("kask", new[] { "kask" })]
    [TestCase("", new string[0])]
    [TestCase(null, new string[0])]
    [TestCase("a b c d e f g", new[] { "a", "b", "c", "d", "e" })]
    public void Search_words_are_split_deduplicated_and_limited(string q, string[] expected)
        => Assert.That(PanelQueryService.SearchTerms(q), Is.EqualTo(expected));
}

[TestFixture]
public class PanelFormErrorTests
{
    private static string Key(string key) => "Plugins.MotorShop.AdminPanel." + key;

    [Test]
    public void Invalid_email_gets_an_email_message_and_other_fields_the_generic_one()
    {
        var state = new ModelStateDictionary();
        state.SetModelValue("ContactEmail", "yanlis-eposta", "yanlis-eposta");
        state.AddModelError("ContactEmail", "The ContactEmail field is not a valid e-mail address.");
        state.SetModelValue("Model.Phone", "x", "x");
        state.AddModelError("Model.Phone", "bad");
        state.SetModelValue("Name", "", "");
        state.AddModelError("Name", "required");

        var errors = PanelForms.Errors(state, NullLocalizer.Instance);

        Assert.That(errors["ContactEmail"], Is.EqualTo(Key("Validation.Email")));
        Assert.That(errors["Phone"], Is.EqualTo(Key("InvalidValue")));
        Assert.That(errors["Name"], Is.EqualTo(Key("Required")));
    }
}

[TestFixture]
public class NewCategoryOrderTests : PanelDbTestBase
{
    [Test]
    public async Task New_category_editor_puts_it_after_its_siblings()
    {
        var kask = await AddCategoryAsync("Kask");
        var a = await AddCategoryAsync("Açık Kask", kask.Id);
        a.DisplayOrder = 4;
        await Db.SaveChangesAsync();

        var services = new Mock<ICommonServices>();
        services.SetupGet(x => x.ActivityLogger).Returns(Mock.Of<IActivityLogger>());
        var media = new Mock<IMediaService>();
        media.Setup(x => x.GetFilesByIdsAsync(It.IsAny<int[]>(), It.IsAny<MediaLoadFlags>())).ReturnsAsync([]);
        var service = new CategoryEditService(Db, services.Object, Mock.Of<ICategoryService>(), media.Object, Mock.Of<IUrlService>());

        var child = await service.LoadEditorAsync(0, kask.Id);
        var root = await service.LoadEditorAsync(0, 0);

        Assert.That(child.DisplayOrder, Is.EqualTo(5));
        Assert.That(child.ParentId, Is.EqualTo(kask.Id));
        Assert.That(root.DisplayOrder, Is.EqualTo(kask.DisplayOrder + 1));
    }
}
