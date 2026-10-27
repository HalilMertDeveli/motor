using Microsoft.AspNetCore.Http;
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
    // Ten photos of at most 10 MB each, plus the form fields.
    const long MaxCreateRequest = ProductPhotoRules.MaxPhotos * ProductPhotoRules.MaxBytes + 1024 * 1024;

    private readonly ProductEditService _products;
    private readonly SimpleProductService _simple;
    private readonly CategoryEditService _categories;

    public MotorPanelProductController(ProductEditService products, SimpleProductService simple, CategoryEditService categories)
    {
        _products = products;
        _simple = simple;
        _categories = categories;
    }

    #region Quick create and simple editor

    [HttpGet("new")]
    [Permission(Permissions.Catalog.Product.Create)]
    public async Task<IActionResult> New()
    {
        PanelHttp.NoIndex(Response);

        ViewBag.SubmissionId = Guid.NewGuid().ToString("N");
        await SetFormViewDataAsync();

        return View("Create", new QuickProductModel());
    }

    [HttpPost("create")]
    [Permission(Permissions.Catalog.Product.Create)]
    [RequestSizeLimit(MaxCreateRequest)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxCreateRequest)]
    public async Task<IActionResult> Create(QuickProductModel model, List<IFormFile> photos)
    {
        photos = photos?.Where(x => x != null).ToList() ?? [];
        if (photos.Count > 0 && !await Services.Permissions.AuthorizeAsync(Permissions.Catalog.Product.EditPicture))
        {
            return Forbidden(Permissions.Catalog.Product.EditPicture);
        }

        var result = await _simple.CreateAsync(model ?? new(), photos, Services.WorkContext.CurrentCustomer.Id);
        return SimpleJson(result);
    }

    [HttpGet("{id:int}")]
    [Permission(Permissions.Catalog.Product.Read)]
    public async Task<IActionResult> Simple(int id)
    {
        PanelHttp.NoIndex(Response);

        var model = await _simple.LoadAsync(id);
        if (model == null)
        {
            return NotFound();
        }

        // Stock per variant combination: only the classic editor can handle it.
        if (model.ManageInventoryMethod == Smartstore.Core.Catalog.Products.ManageInventoryMethod.ManageStockByAttributes)
        {
            return RedirectToAction("Edit", "Product", new { area = "Admin", id });
        }

        await SetFormViewDataAsync();
        model.CategoryOptions = ViewBag.CategoryOptions;
        model.CurrencyCode = Services.CurrencyService.PrimaryCurrency.CurrencyCode;

        return View("Edit", model);
    }

    [HttpPost("{id:int}/simple")]
    [Permission(Permissions.Catalog.Product.Update)]
    public async Task<IActionResult> SaveSimple(int id, SimpleProductModel model)
    {
        model ??= new();
        model.Id = id;

        var canEditCategories = await Services.Permissions.AuthorizeAsync(Permissions.Catalog.Product.EditCategory);
        return SimpleJson(await _simple.SaveAsync(model, canEditCategories));
    }

    [HttpPost("{id:int}/photos")]
    [Permission(Permissions.Catalog.Product.EditPicture)]
    [RequestSizeLimit(MaxCreateRequest)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxCreateRequest)]
    public async Task<IActionResult> AddPhotos(int id, long version, List<IFormFile> photos)
        => SimpleJson(await _simple.AddPhotosAsync(id, version, photos?.Where(x => x != null).ToList() ?? []));

    [HttpPost("{id:int}/photos/{fileId:int}/remove")]
    [Permission(Permissions.Catalog.Product.EditPicture)]
    public async Task<IActionResult> RemovePhoto(int id, int fileId, long version)
        => SimpleJson(await _simple.RemovePhotoAsync(id, version, fileId));

    [HttpPost("{id:int}/photos/order")]
    [Permission(Permissions.Catalog.Product.EditPicture)]
    public async Task<IActionResult> OrderPhotos(int id, long version, int[] fileIds)
        => SimpleJson(await _simple.ReorderPhotosAsync(id, version, fileIds));

    private IActionResult SimpleJson(SimpleProductResult result)
    {
        if (!result.Success)
        {
            Response.StatusCode = result.Conflict ? 409 : 422;
        }

        return Json(result, SmartJsonOptions.CamelCased);
    }

    private IActionResult Forbidden(string permission)
    {
        Response.StatusCode = 403;
        return Json(SimpleProductResult.Fail(string.Empty, T("Plugins.MotorShop.AdminPanel.NoPermission").Value), SmartJsonOptions.CamelCased);
    }

    private async Task SetFormViewDataAsync()
    {
        ViewBag.CanEditPictures = await Services.Permissions.AuthorizeAsync(Permissions.Catalog.Product.EditPicture);
        ViewBag.CanEditCategories = await Services.Permissions.AuthorizeAsync(Permissions.Catalog.Product.EditCategory);
        ViewBag.CanCreateCategory = await Services.Permissions.AuthorizeAsync(Permissions.Catalog.Category.Create);
        ViewBag.CanUpdate = await Services.Permissions.AuthorizeAsync(Permissions.Catalog.Product.Update);
        ViewBag.CategoryOptions ??= await _categories.GetPickerOptionsAsync();
        ViewBag.CurrencyCode = Services.CurrencyService.PrimaryCurrency.CurrencyCode;
    }

    #endregion

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

        var errors = PanelForms.Errors(ModelState, T);

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
