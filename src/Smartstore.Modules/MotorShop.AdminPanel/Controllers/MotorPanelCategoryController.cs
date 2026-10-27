using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MotorShop.AdminPanel.Models;
using MotorShop.AdminPanel.Services;
using Smartstore.Core.Security;
using Smartstore.Web.Controllers;

namespace MotorShop.AdminPanel.Controllers;

[Route("motor-panel/categories")]
public class MotorPanelCategoryController : AdminController
{
    private readonly CategoryEditService _categories;

    public MotorPanelCategoryController(CategoryEditService categories)
    {
        _categories = categories;
    }

    [HttpGet("")]
    [Permission(Permissions.Catalog.Category.Read)]
    public async Task<IActionResult> Index(string q)
    {
        PanelHttp.NoIndex(Response);

        var model = await _categories.GetTreeAsync(q);
        await SetPermissionsAsync();

        return View(model);
    }

    [HttpGet("editor/{id:int?}")]
    [Permission(Permissions.Catalog.Category.Read)]
    public async Task<IActionResult> Editor(int? id, int parentId = 0)
    {
        PanelHttp.NoIndex(Response);

        var model = await _categories.LoadEditorAsync(id ?? 0, parentId);
        if (model == null)
        {
            return NotFound();
        }

        await SetPermissionsAsync();
        return PartialView("_CategoryEditor", model);
    }

    [HttpPost("save")]
    public async Task<IActionResult> Save(CategoryEditorModel model, IFormFile image)
    {
        var permission = model.Id == 0 ? Permissions.Catalog.Category.Create : Permissions.Catalog.Category.Update;
        if (!await Services.Permissions.AuthorizeAsync(permission))
        {
            Response.StatusCode = 403;
            return Json(new { success = false, errors = new Dictionary<string, string> { [""] = await Services.Permissions.GetUnauthorizedMessageAsync(permission) } });
        }

        var errors = PanelForms.Errors(ModelState, T);

        if (errors.Count == 0)
        {
            errors = await _categories.SaveAsync(model, image);
        }

        if (errors.Count > 0)
        {
            Response.StatusCode = 422;
            return Json(new { success = false, errors });
        }

        return Json(new { success = true, id = model.Id, message = T("Plugins.MotorShop.AdminPanel.Saved").Value });
    }

    /// <summary>
    /// "Yeni kategori oluştur" from the product form. Returns the existing category when the name is taken
    /// under the same parent.
    /// </summary>
    [HttpPost("quick")]
    [Permission(Permissions.Catalog.Category.Create)]
    public async Task<IActionResult> QuickCreate(string name, int parentId = 0)
    {
        var result = await _categories.QuickCreateAsync(name, parentId);
        if (!result.Success)
        {
            Response.StatusCode = 422;
        }

        return Json(result, Smartstore.Json.SmartJsonOptions.CamelCased);
    }

    [HttpPost("{id:int}/move")]
    [Permission(Permissions.Catalog.Category.Update)]
    public async Task<IActionResult> Move(int id, bool up)
    {
        var moved = await _categories.MoveAsync(id, up);
        return Json(new { success = moved });
    }

    [HttpGet("{id:int}/delete")]
    [Permission(Permissions.Catalog.Category.Delete)]
    public async Task<IActionResult> DeleteInfo(int id)
    {
        PanelHttp.NoIndex(Response);

        var info = await _categories.GetDeleteInfoAsync(id);
        if (info == null)
        {
            return NotFound();
        }

        return PartialView("_CategoryDelete", info);
    }

    [HttpPost("{id:int}/delete")]
    [Permission(Permissions.Catalog.Category.Delete)]
    public async Task<IActionResult> Delete(int id, bool deleteSubCategories)
    {
        if (!await _categories.DeleteAsync(id, deleteSubCategories))
        {
            return NotFound();
        }

        return Json(new { success = true, message = T("Plugins.MotorShop.AdminPanel.Deleted").Value });
    }

    private async Task SetPermissionsAsync()
    {
        ViewBag.CanCreate = await Services.Permissions.AuthorizeAsync(Permissions.Catalog.Category.Create);
        ViewBag.CanUpdate = await Services.Permissions.AuthorizeAsync(Permissions.Catalog.Category.Update);
        ViewBag.CanDelete = await Services.Permissions.AuthorizeAsync(Permissions.Catalog.Category.Delete);
    }
}
