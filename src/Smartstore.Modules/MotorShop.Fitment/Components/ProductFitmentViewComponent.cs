using Microsoft.AspNetCore.Mvc;
using MotorShop.Fitment.Models;
using MotorShop.Fitment.Services;
using Smartstore.Web.Components;

namespace MotorShop.Fitment.Components;

/// <summary>
/// Lists the motorcycles a product fits on the product detail page.
/// </summary>
public class ProductFitmentViewComponent : SmartViewComponent
{
    private readonly FitmentService _fitmentService;

    public ProductFitmentViewComponent(FitmentService fitmentService)
    {
        _fitmentService = fitmentService;
    }

    public async Task<IViewComponentResult> InvokeAsync(int productId)
    {
        var model = new ProductFitmentModel
        {
            Vehicles = await _fitmentService.GetVehiclesForProductAsync(productId),
            Selection = _fitmentService.GetSelection()
        };

        if (model.Vehicles.Count == 0)
        {
            return Empty();
        }

        return View(model);
    }
}
