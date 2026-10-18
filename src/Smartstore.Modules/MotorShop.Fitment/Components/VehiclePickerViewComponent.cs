using Microsoft.AspNetCore.Mvc;
using MotorShop.Fitment.Models;
using MotorShop.Fitment.Services;
using Smartstore.Web.Components;

namespace MotorShop.Fitment.Components;

/// <summary>
/// Make / model / year picker that leads to the list of fitting parts.
/// </summary>
public class VehiclePickerViewComponent : SmartViewComponent
{
    private readonly FitmentService _fitmentService;

    public VehiclePickerViewComponent(FitmentService fitmentService)
    {
        _fitmentService = fitmentService;
    }

    public async Task<IViewComponentResult> InvokeAsync(VehicleSelection selection = null)
    {
        var model = new VehiclePickerModel
        {
            Makes = await _fitmentService.GetMakesAsync(),
            Selection = selection ?? _fitmentService.GetSelection()
        };

        if (model.Makes.Count == 0)
        {
            return Empty();
        }

        if (model.Selection != null)
        {
            model.Models = await _fitmentService.GetModelsAsync(model.Selection.Make);
            model.Years = await _fitmentService.GetYearsAsync(model.Selection.Make, model.Selection.Model);
        }

        return View(model);
    }
}
