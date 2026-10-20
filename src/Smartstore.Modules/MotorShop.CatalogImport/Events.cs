using Microsoft.EntityFrameworkCore;
using MotorShop.CatalogImport.Components;
using MotorShop.CatalogImport.Domain;
using Smartstore.Core.Data;
using Smartstore.Core.Widgets;
using Smartstore.Events;
using Smartstore.Web.Models.Catalog;
using Smartstore.Web.Rendering.Events;

namespace MotorShop.CatalogImport;

internal class Events : IConsumer
{
    private readonly SmartDbContext _db;

    public Events(SmartDbContext db)
    {
        _db = db;
    }

    public Localizer T { get; set; } = NullLocalizer.Instance;

    public async Task HandleEventAsync(TabStripCreated message)
    {
        if (message.TabStripName != "pd-tabs" || message.Model is not ProductDetailsModel product)
        {
            return;
        }

        var hasPart = await _db.CatalogParts()
            .AnyAsync(x => x.ProductId == product.Id && x.State >= PartPublicationState.VerifiedReference);

        if (hasPart)
        {
            await message.TabFactory.AppendAsync(builder => builder
                .Text(T("Plugins.MotorShop.CatalogImport.Public.TechnicalData"))
                .Name("pd-motor-part-specs")
                .Content(new ComponentWidget<CatalogPartSpecsViewComponent>(new { productId = product.Id })));
        }
    }
}
