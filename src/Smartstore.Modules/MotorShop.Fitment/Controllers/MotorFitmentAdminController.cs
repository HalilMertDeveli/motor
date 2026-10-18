using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MotorShop.Fitment.Domain;
using MotorShop.Fitment.Models;
using Smartstore;
using Smartstore.Core.Data;
using Smartstore.Core.Security;
using Smartstore.Web.Controllers;

namespace MotorShop.Fitment.Controllers;

public class MotorFitmentAdminController : AdminController
{
    private readonly SmartDbContext _db;

    public MotorFitmentAdminController(SmartDbContext db)
    {
        _db = db;
    }

    [Permission(Permissions.Catalog.Product.Read)]
    public async Task<IActionResult> Configure()
    {
        var model = new FitmentConfigurationModel
        {
            Vehicles = await _db.MotorVehicles()
                .AsNoTracking()
                .OrderBy(x => x.Make)
                .ThenBy(x => x.Model)
                .ThenBy(x => x.YearFrom)
                .ToListAsync(),
            ProductCounts = await _db.ProductMotorVehicles()
                .AsNoTracking()
                .GroupBy(x => x.MotorVehicleId)
                .Select(x => new { x.Key, Count = x.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count)
        };

        return View(model);
    }

    [HttpPost]
    [Permission(Permissions.Catalog.Product.Update)]
    public async Task<IActionResult> AddVehicle(FitmentConfigurationModel model)
    {
        var v = model.NewVehicle;

        if (v == null || !v.Make.HasValue() || !v.Model.HasValue())
        {
            NotifyError(T("Plugins.MotorShop.Fitment.MakeModelRequired"));
        }
        else
        {
            await AddVehiclesAsync([new MotorVehicle
            {
                Make = v.Make.Trim(),
                Model = v.Model.Trim(),
                YearFrom = Math.Min(v.YearFrom, v.YearTo),
                YearTo = Math.Max(v.YearFrom, v.YearTo),
                EngineCc = v.EngineCc
            }]);
        }

        return RedirectToAction(nameof(Configure));
    }

    /// <summary>
    /// Imports one vehicle per line: Make;Model;YearFrom;YearTo;EngineCc (EngineCc optional).
    /// </summary>
    [HttpPost]
    [Permission(Permissions.Catalog.Product.Update)]
    public async Task<IActionResult> BulkImport(FitmentConfigurationModel model)
    {
        var vehicles = new List<MotorVehicle>();
        var invalidLines = new List<string>();

        foreach (var line in model.BulkImport.EmptyNull().Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0))
        {
            var parts = line.Split([';', '\t'], StringSplitOptions.TrimEntries);

            if (parts.Length >= 4
                && parts[0].HasValue()
                && parts[1].HasValue()
                && int.TryParse(parts[2], out var from)
                && int.TryParse(parts[3], out var to))
            {
                vehicles.Add(new MotorVehicle
                {
                    Make = parts[0],
                    Model = parts[1],
                    YearFrom = Math.Min(from, to),
                    YearTo = Math.Max(from, to),
                    EngineCc = parts.Length > 4 && int.TryParse(parts[4], out var cc) ? cc : null
                });
            }
            else
            {
                invalidLines.Add(line);
            }
        }

        await AddVehiclesAsync(vehicles);

        if (invalidLines.Count > 0)
        {
            NotifyWarning(T("Plugins.MotorShop.Fitment.InvalidLines", string.Join(", ", invalidLines.Take(10))));
        }

        return RedirectToAction(nameof(Configure));
    }

    [HttpPost]
    [Permission(Permissions.Catalog.Product.Update)]
    public async Task<IActionResult> DeleteVehicle(int id)
    {
        var deleted = await _db.MotorVehicles().Where(x => x.Id == id).ExecuteDeleteAsync();
        if (deleted > 0)
        {
            NotifySuccess(T("Admin.Common.TaskSuccessfullyProcessed"));
        }

        return RedirectToAction(nameof(Configure));
    }

    [Permission(Permissions.Catalog.Product.Read)]
    public async Task<IActionResult> ProductEditTab(int productId)
    {
        var model = new ProductFitmentEditModel
        {
            ProductId = productId,
            VehicleIds = await _db.ProductMotorVehicles()
                .AsNoTracking()
                .Where(x => x.ProductId == productId)
                .Select(x => x.MotorVehicleId)
                .ToArrayAsync()
        };

        ViewBag.Vehicles = await _db.MotorVehicles()
            .AsNoTracking()
            .OrderBy(x => x.Make)
            .ThenBy(x => x.Model)
            .ThenBy(x => x.YearFrom)
            .ToListAsync();

        ViewData.TemplateInfo.HtmlFieldPrefix = "CustomProperties[MotorFitment]";
        return View(model);
    }

    private async Task AddVehiclesAsync(List<MotorVehicle> vehicles)
    {
        if (vehicles.Count == 0)
        {
            return;
        }

        // Skip exact duplicates.
        var existing = await _db.MotorVehicles()
            .AsNoTracking()
            .Select(x => new { x.Make, x.Model, x.YearFrom, x.YearTo })
            .ToListAsync();

        var keys = existing
            .Select(x => $"{x.Make}|{x.Model}|{x.YearFrom}|{x.YearTo}".ToLowerInvariant())
            .ToHashSet();

        var toAdd = vehicles
            .Where(x => keys.Add($"{x.Make}|{x.Model}|{x.YearFrom}|{x.YearTo}".ToLowerInvariant()))
            .ToList();

        if (toAdd.Count > 0)
        {
            _db.MotorVehicles().AddRange(toAdd);
            await _db.SaveChangesAsync();
        }

        NotifySuccess(T("Plugins.MotorShop.Fitment.VehiclesAdded", toAdd.Count));
    }
}
