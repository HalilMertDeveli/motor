using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MotorShop.Legal.Services;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Data;
using Smartstore.Web.Components;

namespace MotorShop.Legal.Components;

/// <summary>
/// Order details page: links to the document snapshots accepted for this order.
/// The zone passes no model, the order id comes from the route.
/// </summary>
public class OrderLegalDocumentsViewComponent : SmartViewComponent
{
    private readonly SmartDbContext _db;
    private readonly LegalDocumentService _documentService;

    public OrderLegalDocumentsViewComponent(SmartDbContext db, LegalDocumentService documentService)
    {
        _db = db;
        _documentService = documentService;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (!int.TryParse(RouteData.Values["id"]?.ToString(), out var orderId) || orderId <= 0)
        {
            return Empty();
        }

        // Access to the order itself has already been checked by OrderController.Details.
        var keys = LegalDocumentKeys.All.Select(x => LegalAcknowledgment.SnapshotAttributeKeyPrefix + x).ToArray();
        var storedKeys = await _db.GenericAttributes
            .AsNoTracking()
            .Where(x => x.KeyGroup == nameof(Order) && x.EntityId == orderId && keys.Contains(x.Key))
            .Select(x => x.Key)
            .ToListAsync();

        if (storedKeys.Count == 0)
        {
            return Empty();
        }

        var links = new List<(string Title, string Url)>();
        foreach (var key in LegalDocumentKeys.All.Where(x => storedKeys.Contains(LegalAcknowledgment.SnapshotAttributeKeyPrefix + x)))
        {
            var topic = await _documentService.LoadTopicAsync(_documentService.GetTopicSystemName(key));
            var title = topic?.GetLocalized(x => x.Title).Value.NullEmpty() ?? key;
            links.Add((title, Url.RouteUrl("MotorLegal.Document", new { orderId, key })));
        }

        return View(links);
    }
}
