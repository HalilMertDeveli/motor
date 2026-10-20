using System.IO;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MotorShop.SupplierCatalog.Models;
using MotorShop.SupplierCatalog.Services;
using Smartstore.Core.Data;
using Smartstore.Core.Security;
using Smartstore.Web.Controllers;

namespace MotorShop.SupplierCatalog.Controllers;

public class SupplierCatalogAdminController : AdminController
{
    const long MaxPackageSize = 512L * 1024 * 1024;
    const string ResultKey = "SupplierCatalog.LastResult";

    private readonly SmartDbContext _db;
    private readonly SupplierCatalogImporter _importer;

    public SupplierCatalogAdminController(SmartDbContext db, SupplierCatalogImporter importer)
    {
        _db = db;
        _importer = importer;
    }

    [Permission(Permissions.Catalog.Product.Create)]
    public IActionResult Configure()
    {
        var model = new SupplierCatalogImportModel();
        if (TempData[ResultKey] is string json)
        {
            model.LastResult = JsonSerializer.Deserialize<SupplierCatalogImportResult>(json);
        }

        return View(model);
    }

    [HttpPost]
    [Permission(Permissions.Catalog.Product.Create)]
    [RequestSizeLimit(MaxPackageSize)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxPackageSize)]
    public async Task<IActionResult> Import(SupplierCatalogImportModel model, IFormFile package)
    {
        if (package == null || package.Length == 0 || !Path.GetExtension(package.FileName).EqualsNoCase(".zip"))
        {
            NotifyError(T("Plugins.MotorShop.SupplierCatalog.PackageRequired"));
            return RedirectToAction(nameof(Configure));
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
                PublishUnpricedProducts = model.PublishUnpricedProducts,
                UpdateExistingProducts = model.UpdateExistingProducts
            }, HttpContext.RequestAborted);

            // Keep TempData small; the full warning list is not needed after a redirect.
            result.Warnings = result.Warnings.Take(50).ToList();
            TempData[ResultKey] = JsonSerializer.Serialize(result);

            NotifySuccess(T("Plugins.MotorShop.SupplierCatalog.ImportCompleted"));
        }
        catch (InvalidDataException ex)
        {
            NotifyError(ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Supplier catalog import failed.");
            NotifyError(T("Plugins.MotorShop.SupplierCatalog.ImportFailed") + " " + ex.GetBaseException().Message);

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
