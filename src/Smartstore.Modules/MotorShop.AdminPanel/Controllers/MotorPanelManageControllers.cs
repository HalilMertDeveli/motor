using Microsoft.AspNetCore.Mvc;
using MotorShop.AdminPanel.Models;
using MotorShop.AdminPanel.Services;
using Smartstore.Core.Security;
using Smartstore.Web.Controllers;

namespace MotorShop.AdminPanel.Controllers;

internal static class PanelForms
{
    /// <summary>
    /// Model binding and data annotation errors, keyed by field name, in the admin language
    /// (the framework's own messages are English).
    /// </summary>
    public static Dictionary<string, string> Errors(Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary modelState, Localizer T)
    {
        var errors = new Dictionary<string, string>();
        foreach (var (key, entry) in modelState)
        {
            if (entry.Errors.Count > 0)
            {
                var field = key[(key.LastIndexOf('.') + 1)..];
                errors[field] = entry.AttemptedValue.IsEmpty()
                    ? T("Plugins.MotorShop.AdminPanel.Required")
                    : field.EndsWith("Email", StringComparison.OrdinalIgnoreCase)
                        ? T("Plugins.MotorShop.AdminPanel.Validation.Email")
                        : T("Plugins.MotorShop.AdminPanel.InvalidValue");
            }
        }

        return errors;
    }
}

[Route("motor-panel/reports")]
public class MotorPanelReportController : AdminController
{
    private readonly PanelReportService _reports;

    public MotorPanelReportController(PanelReportService reports)
    {
        _reports = reports;
    }

    [HttpGet("")]
    [Permission(Permissions.Order.Read)]
    public async Task<IActionResult> Index(ReportFilter filter)
    {
        PanelHttp.NoIndex(Response);
        return View(await _reports.GetReportAsync(filter ?? new()));
    }
}

[Route("motor-panel/content")]
public class MotorPanelContentController : AdminController
{
    private readonly ContentPanelService _content;

    public MotorPanelContentController(ContentPanelService content)
    {
        _content = content;
    }

    [HttpGet("")]
    [Permission(Permissions.Cms.Topic.Read)]
    public async Task<IActionResult> Index()
    {
        PanelHttp.NoIndex(Response);

        ViewBag.CanUpdate = await Services.Permissions.AuthorizeAsync(Permissions.Cms.Topic.Update);
        return View(new ContentListModel
        {
            Topics = await _content.GetTopicsAsync(),
            BlogInstalled = _content.BlogInstalled,
            BlogConfigureUrl = _content.BlogInstalled ? Url.Action("Index", "MotorWordPressAdmin", new { area = "Admin" }) : null
        });
    }

    [HttpGet("{id:int}/edit")]
    [Permission(Permissions.Cms.Topic.Read)]
    public async Task<IActionResult> Edit(int id)
    {
        PanelHttp.NoIndex(Response);

        var model = await _content.LoadTopicAsync(id);
        if (model == null)
        {
            return NotFound();
        }

        ViewBag.CanUpdate = await Services.Permissions.AuthorizeAsync(Permissions.Cms.Topic.Update);
        return PartialView("_TopicEditor", model);
    }

    [HttpPost("{id:int}/edit")]
    [Permission(Permissions.Cms.Topic.Update)]
    public async Task<IActionResult> Edit(int id, TopicEditorModel model)
    {
        model.Id = id;
        var errors = PanelForms.Errors(ModelState, T);
        if (errors.Count == 0)
        {
            var error = await _content.SaveTopicAsync(model);
            if (error != null)
            {
                errors[""] = error;
            }
        }

        if (errors.Count > 0)
        {
            Response.StatusCode = 422;
            return Json(new { success = false, errors });
        }

        return Json(new { success = true, message = T("Plugins.MotorShop.AdminPanel.Saved").Value });
    }

    [HttpPost("{id:int}/publish")]
    [Permission(Permissions.Cms.Topic.Update)]
    public async Task<IActionResult> Publish(int id, bool published)
    {
        if (!await _content.SetPublishedAsync(id, published))
        {
            return NotFound();
        }

        return Json(new { success = true, message = T("Plugins.MotorShop.AdminPanel.Saved").Value });
    }
}

[Route("motor-panel/coupons")]
public class MotorPanelCouponController : AdminController
{
    private readonly CouponPanelService _coupons;

    public MotorPanelCouponController(CouponPanelService coupons)
    {
        _coupons = coupons;
    }

    [HttpGet("")]
    [Permission(Permissions.Promotion.Discount.Read)]
    public async Task<IActionResult> Index()
    {
        PanelHttp.NoIndex(Response);

        ViewBag.CanCreate = await Services.Permissions.AuthorizeAsync(Permissions.Promotion.Discount.Create);
        ViewBag.CanUpdate = await Services.Permissions.AuthorizeAsync(Permissions.Promotion.Discount.Update);
        ViewBag.CanDelete = await Services.Permissions.AuthorizeAsync(Permissions.Promotion.Discount.Delete);
        return View(await _coupons.GetCouponsAsync());
    }

    [HttpGet("editor/{id:int?}")]
    [Permission(Permissions.Promotion.Discount.Read)]
    public async Task<IActionResult> Editor(int? id)
    {
        PanelHttp.NoIndex(Response);

        var model = await _coupons.LoadAsync(id ?? 0);
        if (model == null)
        {
            return NotFound();
        }

        ViewBag.CanSave = await Services.Permissions.AuthorizeAsync(model.Id == 0 ? Permissions.Promotion.Discount.Create : Permissions.Promotion.Discount.Update);
        return PartialView("_CouponEditor", model);
    }

    [HttpPost("save")]
    public async Task<IActionResult> Save(CouponEditorModel model)
    {
        var permission = model.Id == 0 ? Permissions.Promotion.Discount.Create : Permissions.Promotion.Discount.Update;
        if (!await Services.Permissions.AuthorizeAsync(permission))
        {
            Response.StatusCode = 403;
            return Json(new { success = false, errors = new Dictionary<string, string> { [""] = await Services.Permissions.GetUnauthorizedMessageAsync(permission) } });
        }

        var errors = PanelForms.Errors(ModelState, T);
        if (errors.Count == 0)
        {
            errors = await _coupons.SaveAsync(model);
        }

        if (errors.Count > 0)
        {
            Response.StatusCode = 422;
            return Json(new { success = false, errors });
        }

        return Json(new { success = true, message = T("Plugins.MotorShop.AdminPanel.Saved").Value });
    }

    [HttpPost("{id:int}/active")]
    [Permission(Permissions.Promotion.Discount.Update)]
    public async Task<IActionResult> Active(int id, bool active)
    {
        if (!await _coupons.SetActiveAsync(id, active))
        {
            return NotFound();
        }

        return Json(new { success = true, message = T("Plugins.MotorShop.AdminPanel.Saved").Value });
    }

    [HttpPost("{id:int}/delete")]
    [Permission(Permissions.Promotion.Discount.Delete)]
    public async Task<IActionResult> Delete(int id)
    {
        if (!await _coupons.DeleteAsync(id))
        {
            return NotFound();
        }

        return Json(new { success = true, message = T("Plugins.MotorShop.AdminPanel.Deleted").Value });
    }
}

[Route("motor-panel/settings")]
public class MotorPanelSettingsController : AdminController
{
    private readonly SettingsPanelService _settings;

    public MotorPanelSettingsController(SettingsPanelService settings)
    {
        _settings = settings;
    }

    [HttpGet("")]
    [Permission(Permissions.Configuration.Setting.Read)]
    public async Task<IActionResult> Index()
    {
        PanelHttp.NoIndex(Response);

        var model = await _settings.GetAsync();
        model.CanUpdate = await Services.Permissions.AuthorizeAsync(Permissions.Configuration.Setting.Update);
        model.CanUpdateStore = await Services.Permissions.AuthorizeAsync(Permissions.Configuration.Store.Update);
        return View(model);
    }

    [HttpPost("panel")]
    [Permission(Permissions.Configuration.Setting.Update)]
    public async Task<IActionResult> Panel(PanelSettingsForm form)
    {
        if (form.AccentColor.HasValue() && Components.MotorPanelHeaderViewComponent.NormalizeColor(form.AccentColor) != form.AccentColor.Trim().ToLowerInvariant())
        {
            return Fail(nameof(form.AccentColor), T("Plugins.MotorShop.AdminPanel.Settings.AccentInvalid"));
        }

        await _settings.SavePanelAsync(form);
        return Ok();
    }

    [HttpPost("company")]
    [Permission(Permissions.Configuration.Setting.Update)]
    public async Task<IActionResult> Company(CompanyForm form)
    {
        var errors = PanelForms.Errors(ModelState, T);
        if (errors.Count > 0)
        {
            Response.StatusCode = 422;
            return Json(new { success = false, errors });
        }

        var error = await _settings.SaveCompanyAsync(form, await Services.Permissions.AuthorizeAsync(Permissions.Configuration.Store.Update));
        return error == null ? Ok() : Fail("", error);
    }

    [HttpPost("shipping")]
    [Permission(Permissions.Configuration.Setting.Update)]
    public async Task<IActionResult> Shipping(ShippingForm form)
    {
        var error = await _settings.SaveShippingAsync(form);
        return error == null ? Ok() : Fail(nameof(form.FreeShippingOverXValue), error);
    }

    private new IActionResult Ok()
        => Json(new { success = true, message = T("Plugins.MotorShop.AdminPanel.Saved").Value });

    private IActionResult Fail(string field, string message)
    {
        Response.StatusCode = 422;
        return Json(new { success = false, errors = new Dictionary<string, string> { [field] = message } });
    }
}
