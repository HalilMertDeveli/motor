using Microsoft.AspNetCore.Mvc;
using MotorShop.AdminPanel.Models;
using MotorShop.AdminPanel.Services;
using Smartstore.Core.Security;
using Smartstore.Json;
using Smartstore.Web.Controllers;

namespace MotorShop.AdminPanel.Controllers;

/// <summary>
/// Product writes of the panel. Every POST is antiforgery-validated (AdminController) and checks the
/// same permission the classic product editor uses for that change.
/// </summary>
[Route("motor-panel/product")]
public class MotorPanelProductController : AdminController
{
    private readonly ProductEditService _products;

    public MotorPanelProductController(ProductEditService products)
    {
        _products = products;
    }

    [HttpPost("{id:int}/field")]
    [Permission(Permissions.Catalog.Product.Update)]
    public async Task<IActionResult> Field(int id, string field, string value, string expected)
    {
        var result = await _products.UpdateFieldAsync(id, field, value, expected);
        if (!result.Success)
        {
            Response.StatusCode = result.Conflict ? 409 : 422;
        }

        return Json(result, SmartJsonOptions.CamelCased);
    }

    [HttpPost("bulk")]
    [Permission(Permissions.Catalog.Product.Update)]
    public async Task<IActionResult> Bulk([FromBody] BulkRequest request)
    {
        if (request == null)
        {
            return BadRequest();
        }

        var permission = request.Action switch
        {
            BulkAction.Delete => Permissions.Catalog.Product.Delete,
            BulkAction.AddCategory or BulkAction.RemoveCategory => Permissions.Catalog.Product.EditCategory,
            _ => null
        };

        if (permission != null && !await Services.Permissions.AuthorizeAsync(permission))
        {
            Response.StatusCode = 403;
            return Json(new BulkResult { Error = await Services.Permissions.GetUnauthorizedMessageAsync(permission) }, SmartJsonOptions.CamelCased);
        }

        var result = await _products.BulkAsync(request);
        if (result.Error != null)
        {
            Response.StatusCode = 422;
        }

        return Json(result, SmartJsonOptions.CamelCased);
    }

    [HttpGet("{id:int}/edit")]
    [Permission(Permissions.Catalog.Product.Read)]
    public async Task<IActionResult> Edit(int id)
    {
        PanelHttp.NoIndex(Response);

        var model = await _products.LoadEditorAsync(id);
        if (model == null)
        {
            return NotFound();
        }

        ViewBag.CanUpdate = await Services.Permissions.AuthorizeAsync(Permissions.Catalog.Product.Update);
        ViewBag.CanEditCategories = await Services.Permissions.AuthorizeAsync(Permissions.Catalog.Product.EditCategory);

        return PartialView("_ProductEditor", model);
    }

    [HttpPost("{id:int}/edit")]
    [Permission(Permissions.Catalog.Product.Update)]
    public async Task<IActionResult> Edit(int id, ProductEditorModel model)
    {
        model.Id = id;

        // Category changes need their own permission, as in the classic editor.
        if (!await Services.Permissions.AuthorizeAsync(Permissions.Catalog.Product.EditCategory))
        {
            var current = await _products.LoadEditorAsync(id);
            model.CategoryIds = current?.CategoryIds ?? [];
        }

        var errors = new Dictionary<string, string>();
        foreach (var (key, entry) in ModelState)
        {
            if (entry.Errors.Count > 0)
            {
                errors[key] = entry.Errors[0].ErrorMessage.NullEmpty() ?? T("Plugins.MotorShop.AdminPanel.InvalidValue");
            }
        }

        if (errors.Count == 0)
        {
            errors = await _products.SaveEditorAsync(model);
        }

        if (errors.Count > 0)
        {
            Response.StatusCode = 422;
            return Json(new { success = false, errors });
        }

        return Json(new { success = true, message = T("Plugins.MotorShop.AdminPanel.Saved").Value });
    }

    [HttpPost("{id:int}/duplicate")]
    [Permission(Permissions.Catalog.Product.Create)]
    public async Task<IActionResult> Duplicate(int id)
    {
        var clone = await _products.DuplicateAsync(id);
        if (clone == null)
        {
            return NotFound();
        }

        return Json(new { success = true, id = clone.Id, message = T("Plugins.MotorShop.AdminPanel.Duplicated", clone.Name).ToString() });
    }

    [HttpPost("{id:int}/delete")]
    [Permission(Permissions.Catalog.Product.Delete)]
    public async Task<IActionResult> Delete(int id)
    {
        if (!await _products.DeleteAsync(id))
        {
            return NotFound();
        }

        return Json(new { success = true, message = T("Plugins.MotorShop.AdminPanel.Deleted").Value });
    }
}
