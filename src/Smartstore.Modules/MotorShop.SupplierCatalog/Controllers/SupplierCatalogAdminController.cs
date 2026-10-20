using System.IO;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MotorShop.SupplierCatalog.Models;
using MotorShop.SupplierCatalog.Services;
using Smartstore.Core.Data;
using Smartstore.Core.Security;
using Smartstore.Web.Controllers;

namespace MotorShop.SupplierCatalog.Controllers;

public class SupplierCatalogAdminController : AdminController
{
    /// <summary>
    /// Upload limit of the action. The effective limit can be lower: Smartstore:MaxRequestBodySize and, on the VPS,
    /// MOTOR_MAX_UPLOAD_BYTES at the reverse proxy.
    /// </summary>
    const long MaxPackageSize = 512L * 1024 * 1024;
    const string ResultKey = "SupplierCatalog.LastResult";
    const string DefaultTaxCategoryName = "Genel KDV";

    private readonly SmartDbContext _db;
    private readonly SupplierCatalogImporter _importer;

    public SupplierCatalogAdminController(SmartDbContext db, SupplierCatalogImporter importer)
    {
        _db = db;
        _importer = importer;
    }

    [Permission(Permissions.Catalog.Product.Create)]
    [Permission(Permissions.Catalog.Product.Update)]
    public async Task<IActionResult> Configure()
    {
        var model = new SupplierCatalogImportModel();
        if (TempData[ResultKey] is string json)
        {
            model.LastResult = JsonSerializer.Deserialize<SupplierCatalogImportResult>(json);
        }

        var taxCategories = await _db.TaxCategories.AsNoTracking().OrderBy(x => x.DisplayOrder).ToListAsync();
        model.TaxCategoryId = taxCategories.FirstOrDefault(x => x.Name.EqualsNoCase(DefaultTaxCategoryName))?.Id
            ?? taxCategories.FirstOrDefault()?.Id;
        model.AvailableTaxCategories = taxCategories
            .Select(x => new SelectListItem(x.Name, x.Id.ToStringInvariant(), x.Id == model.TaxCategoryId))
            .ToList();

        return View(model);
    }

    [HttpPost]
    [Permission(Permissions.Catalog.Product.Create)]
    [Permission(Permissions.Catalog.Product.Update)]
    [RequestSizeLimit(MaxPackageSize)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxPackageSize)]
    public async Task<IActionResult> Import(SupplierCatalogImportModel model, IFormFile package)
    {
        if (package == null || package.Length == 0 || !Path.GetExtension(package.FileName).EqualsNoCase(".zip"))
        {
            NotifyError(T("Plugins.MotorShop.SupplierCatalog.PackageRequired"));
            return RedirectToAction(nameof(Configure));
        }

        if (model.SupplierName.IsEmpty())
        {
            NotifyError(T("Plugins.MotorShop.SupplierCatalog.SupplierRequired"));
            return RedirectToAction(nameof(Configure));
        }

        if (model.TaxCategoryId is > 0 && !await _db.TaxCategories.AnyAsync(x => x.Id == model.TaxCategoryId))
        {
            model.TaxCategoryId = null;
        }

        // Spool the upload to disk: zip archives need a seekable stream and packages can be large.
        var tempPath = Path.Combine(Path.GetTempPath(), $"supplier-catalog-{Guid.NewGuid():N}.zip");
        try
        {
            await using (var target = System.IO.File.Create(tempPath))
            {
                await package.CopyToAsync(target, HttpContext.RequestAborted);
            }

            using var catalog = CatalogPackage.Open(System.IO.File.OpenRead(tempPath));

            var result = await _importer.ImportAsync(catalog, new SupplierCatalogImportOptions
            {
                SupplierName = model.SupplierName,
                TaxCategoryId = model.TaxCategoryId,
                UpdateExistingProducts = model.UpdateExistingProducts
            }, HttpContext.RequestAborted);

            // Keep TempData small; the full warning list is not needed after a redirect.
            result.Warnings = result.Warnings.Take(50).ToList();
            TempData[ResultKey] = JsonSerializer.Serialize(result);

            NotifySuccess(T("Plugins.MotorShop.SupplierCatalog.ImportCompleted"));
        }
        catch (InvalidDataException ex)
        {
            // Package problems (missing products.json, limits, invalid JSON): safe to show.
            NotifyError(ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Supplier catalog import failed.");
            NotifyError(T("Plugins.MotorShop.SupplierCatalog.ImportFailed"));

            // Drop the failed batch, otherwise the request-end save would retry it.
            _db.ChangeTracker.Clear();
        }
        finally
        {
            try { System.IO.File.Delete(tempPath); } catch { }
        }

        // Post/Redirect/Get: a page refresh must not upload and import the package again.
        return RedirectToAction(nameof(Configure));
    }
}
