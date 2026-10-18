using Microsoft.EntityFrameworkCore;
using MotorShop.Fitment.Components;
using MotorShop.Fitment.Domain;
using MotorShop.Fitment.Models;
using Smartstore.Core.Catalog.Products;
using Smartstore.Core.Data;
using Smartstore.Core.Security;
using Smartstore.Core.Widgets;
using Smartstore.Events;
using Smartstore.Web.Models.Catalog;
using Smartstore.Web.Rendering.Events;

namespace MotorShop.Fitment;

internal class Events : IConsumer
{
    const string CustomPropertyKey = "MotorFitment";

    private readonly SmartDbContext _db;

    public Events(SmartDbContext db)
    {
        _db = db;
    }

    public Localizer T { get; set; } = NullLocalizer.Instance;

    public async Task HandleEventAsync(TabStripCreated message)
    {
        if (message.TabStripName == "product-edit")
        {
            var productId = ((TabbableModel)message.Model).Id;

            await message.TabFactory.AppendAsync(builder => builder
                .Text(T("Plugins.MotorShop.Fitment.CompatibleMotorcycles"))
                .Name("tab-motor-fitment")
                .Icon("bicycle", "bi")
                .LinkHtmlAttributes(new { data_tab_name = CustomPropertyKey })
                .Action("ProductEditTab", "MotorFitmentAdmin", new { productId, area = "Admin" })
                .Ajax());
        }
        else if (message.TabStripName == "pd-tabs" && message.Model is ProductDetailsModel product)
        {
            var hasVehicles = await _db.ProductMotorVehicles().AnyAsync(x => x.ProductId == product.Id);
            if (hasVehicles)
            {
                await message.TabFactory.AppendAsync(builder => builder
                    .Text(T("Plugins.MotorShop.Fitment.CompatibleMotorcycles"))
                    .Name("pd-motor-fitment")
                    .Content(new ComponentWidget<ProductFitmentViewComponent>(new { productId = product.Id })));
            }
        }
    }

    public async Task HandleEventAsync(ModelBoundEvent message, IPermissionService permissionService)
    {
        if (!message.BoundModel.CustomProperties.TryGetValue(CustomPropertyKey, out object value)
            || value is not ProductFitmentEditModel model
            || model.ProductId == 0
            || !await permissionService.AuthorizeAsync(Permissions.Catalog.Product.Update))
        {
            return;
        }

        var selectedIds = (model.VehicleIds ?? []).Distinct().ToHashSet();
        var mappings = await _db.ProductMotorVehicles()
            .Where(x => x.ProductId == model.ProductId)
            .ToListAsync();

        _db.ProductMotorVehicles().RemoveRange(mappings.Where(x => !selectedIds.Contains(x.MotorVehicleId)));

        var existingIds = mappings.Select(x => x.MotorVehicleId).ToHashSet();
        var validNewIds = await _db.MotorVehicles()
            .Where(x => selectedIds.Contains(x.Id) && !existingIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync();

        _db.ProductMotorVehicles().AddRange(validNewIds.Select(id => new ProductMotorVehicle
        {
            ProductId = model.ProductId,
            MotorVehicleId = id
        }));

        await _db.SaveChangesAsync();
    }

    public async Task HandleEventAsync(ProductClonedEvent message)
    {
        var vehicleIds = await _db.ProductMotorVehicles()
            .AsNoTracking()
            .Where(x => x.ProductId == message.Source.Id)
            .Select(x => x.MotorVehicleId)
            .ToListAsync();

        if (vehicleIds.Count > 0)
        {
            _db.ProductMotorVehicles().AddRange(vehicleIds.Select(id => new ProductMotorVehicle
            {
                ProductId = message.Clone.Id,
                MotorVehicleId = id
            }));

            await _db.SaveChangesAsync();
        }
    }
}
