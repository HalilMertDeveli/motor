using Microsoft.AspNetCore.Mvc;
using MotorShop.Tracking.Configuration;
using MotorShop.Tracking.Models;
using MotorShop.Tracking.Services;
using Smartstore.Core.Security;
using Smartstore.Web.Controllers;
using Smartstore.Web.Modelling.Settings;

namespace MotorShop.Tracking.Controllers;

public class MotorTrackingAdminController : AdminController
{
    [LoadSetting]
    [Permission(Permissions.Configuration.Setting.Read)]
    public IActionResult Configure(ShipmentTrackingSettings settings)
    {
        return View(CreateModel(settings.Carriers, settings.DefaultCarrier, settings.ShowOnOrderDetails));
    }

    [HttpPost, SaveSetting]
    [Permission(Permissions.Configuration.Setting.Update)]
    public IActionResult Configure(TrackingConfigurationModel model, ShipmentTrackingSettings settings)
    {
        var carriers = CarrierTrackingService.ParseCarriers(model.Carriers);
        var defaultCarrier = model.DefaultCarrier?.Trim();

        if (defaultCarrier.HasValue() && !carriers.Any(x => x.Name.EqualsNoCase(defaultCarrier)))
        {
            ModelState.AddModelError(nameof(model.DefaultCarrier), T("Plugins.MotorShop.Tracking.DefaultCarrierUnknown"));
        }

        if (!ModelState.IsValid)
        {
            return View(CreateModel(model.Carriers, model.DefaultCarrier, model.ShowOnOrderDetails));
        }

        settings.Carriers = model.Carriers?.Trim() ?? string.Empty;
        settings.DefaultCarrier = defaultCarrier ?? string.Empty;
        settings.ShowOnOrderDetails = model.ShowOnOrderDetails;

        return RedirectToAction(nameof(Configure));
    }

    /// <summary>
    /// For a Turkish language added after the module was installed.
    /// </summary>
    [HttpPost]
    [Permission(Permissions.Cms.MessageTemplate.Update)]
    public async Task<IActionResult> ApplyTurkishTemplates([FromServices] ShipmentTemplateLocalizer localizer)
    {
        var written = await localizer.ApplyAsync();
        NotifyInfo(T("Plugins.MotorShop.Tracking.TemplatesApplied", written));

        return RedirectToAction(nameof(Configure));
    }

    private static TrackingConfigurationModel CreateModel(string carriers, string defaultCarrier, bool showOnOrderDetails)
    {
        var valid = CarrierTrackingService.ParseCarriers(carriers);
        var lines = (carriers ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !x.StartsWith('#'))
            .ToList();

        return new TrackingConfigurationModel
        {
            Carriers = carriers,
            DefaultCarrier = defaultCarrier,
            ShowOnOrderDetails = showOnOrderDetails,
            ValidCarriers = [.. valid.Select(x => $"{x.Name}: {x.UrlTemplate}")],
            // Lines that were skipped: no "|", no {0}, not https, or a duplicate name.
            InvalidLines = [.. lines.Where(l => !valid.Any(v => l.StartsWith(v.Name, StringComparison.OrdinalIgnoreCase) && l.EndsWith(v.UrlTemplate, StringComparison.Ordinal)))]
        };
    }
}
