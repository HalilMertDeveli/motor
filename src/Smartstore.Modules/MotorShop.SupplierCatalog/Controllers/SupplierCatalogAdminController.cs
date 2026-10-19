using System.IO;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MotorShop.SupplierCatalog.Models;
using MotorShop.SupplierCatalog.Services;
using Smartstore.Core.Security;
using Smartstore.Web.Controllers;

namespace MotorShop.SupplierCatalog.Controllers;

public class SupplierCatalogAdminController : AdminController
{
    const long MaxPackageSize = 512L * 1024 * 1024;

    private readonly SupplierCatalogImporter _importer;

    public SupplierCatalogAdminController(SupplierCatalogImporter importer)
    {
        _importer = importer;
    }

    [Permission(Permissions.Catalog.Product.Create)]
    public IActionResult Configure()
    {
        return View(new SupplierCatalogImportModel());
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
            return View(nameof(Configure), model);
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

            model.LastResult = await _importer.ImportAsync(catalog, new SupplierCatalogImportOptions
            {
                PublishUnpricedProducts = model.PublishUnpricedProducts,
                UpdateExistingProducts = model.UpdateExistingProducts
            }, HttpContext.RequestAborted);

            NotifySuccess(T("Plugins.MotorShop.SupplierCatalog.ImportCompleted"));
        }
        catch (InvalidDataException ex)
        {
            NotifyError(ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Supplier catalog import failed.");
            NotifyError(T("Plugins.MotorShop.SupplierCatalog.ImportFailed"));
        }
        finally
        {
            try { System.IO.File.Delete(tempPath); } catch { }
        }

        return View(nameof(Configure), model);
    }
}
