using Microsoft.AspNetCore.Mvc;
using MotorShop.Fitment.Models;
using MotorShop.Fitment.Services;
using Smartstore;
using Smartstore.Core.Catalog;
using Smartstore.Core.Catalog.Search;
using Smartstore.Core.Localization.Routing;
using Smartstore.Web.Controllers;
using Smartstore.Web.Models.Catalog;

namespace MotorShop.Fitment.Controllers;

public class MotorFitmentController : PublicController
{
    private readonly FitmentService _fitmentService;
    private readonly ICatalogSearchService _catalogSearchService;
    private readonly CatalogHelper _helper;
    private readonly CatalogSettings _catalogSettings;

    public MotorFitmentController(
        FitmentService fitmentService,
        ICatalogSearchService catalogSearchService,
        CatalogHelper helper,
        CatalogSettings catalogSettings)
    {
        _fitmentService = fitmentService;
        _catalogSearchService = catalogSearchService;
        _helper = helper;
        _catalogSettings = catalogSettings;
    }

    [HttpGet("motorfitment/models")]
    public async Task<IActionResult> Models(string make)
        => Json(await _fitmentService.GetModelsAsync(make));

    [HttpGet("motorfitment/years")]
    public async Task<IActionResult> Years(string make, string model)
        => Json(await _fitmentService.GetYearsAsync(make, model));

    [HttpGet("motorfitment/clear")]
    public IActionResult Clear(string returnUrl)
    {
        _fitmentService.SaveSelection(null);
        return RedirectToReferrer(returnUrl, () => RedirectToRoute("Homepage"));
    }

    [LocalizedRoute("/uyumlu-parcalar", Name = "MotorFitment.Results")]
    public async Task<IActionResult> Results(string make, string model, int? year, CatalogSearchQuery query)
    {
        var selection = make.HasValue()
            ? new VehicleSelection(make.Trim(), model?.Trim(), year)
            : _fitmentService.GetSelection();

        if (make.HasValue())
        {
            _fitmentService.SaveSelection(selection);
        }

        var resultModel = new FitmentResultsModel { Selection = selection };

        if (selection != null && !selection.IsEmpty)
        {
            var productIds = await _fitmentService.GetFittingProductIdsAsync(selection);
            if (productIds.Length > 0)
            {
                query.WithProductIds(productIds);

                var searchResult = await _catalogSearchService.SearchAsync(query);
                var viewMode = query.CustomData.Get("ViewMode") is string mode && mode.EqualsNoCase("list")
                    ? ProductSummaryViewMode.List
                    : ProductSummaryViewMode.Grid;

                resultModel.SearchResult = searchResult;
                resultModel.Products = await _helper.MapProductSummaryModelAsync(searchResult, _helper.GetBestFitProductSummaryMappingSettings(viewMode));

                _helper.MapListActions(resultModel.Products, null, _catalogSettings.DefaultPageSizeOptions);
            }
        }

        return View(resultModel);
    }
}
