using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MotorShop.Legal.Services;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Data;
using Smartstore.Core.Security;
using Smartstore.Web.Controllers;

namespace MotorShop.Legal.Controllers;

public class LegalDocumentViewModel
{
    public int OrderId { get; set; }
    public string OrderNumber { get; set; }
    public string Title { get; set; }
    public string Html { get; set; }
}

/// <summary>
/// Shows the document snapshot stored for an order: to the customer who placed it
/// and to administrators with order read permission (same rule as the order details page).
/// </summary>
public class MotorLegalController : PublicController
{
    private readonly SmartDbContext _db;
    private readonly LegalDocumentService _documentService;

    public MotorLegalController(SmartDbContext db, LegalDocumentService documentService)
    {
        _db = db;
        _documentService = documentService;
    }

    [Route("order/{orderId:int}/legal/{key}", Name = "MotorLegal.Document")]
    public async Task<IActionResult> Document(int orderId, string key)
    {
        // Smartstore generates lowercase URLs.
        key = LegalDocumentKeys.All.FirstOrDefault(x => x.EqualsNoCase(key));
        if (key == null)
        {
            return NotFound();
        }

        var order = await _db.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == orderId && !x.Deleted);

        if (order == null)
        {
            return NotFound();
        }

        if (!await Services.Permissions.AuthorizeAsync(Permissions.Order.Read)
            && order.CustomerId != Services.WorkContext.CurrentCustomer.Id)
        {
            return ChallengeOrForbid();
        }

        var html = await GetOrderAttributeAsync(orderId, LegalAcknowledgment.SnapshotAttributeKeyPrefix + key);
        if (html == null)
        {
            return NotFound();
        }

        var topic = await _documentService.LoadTopicAsync(_documentService.GetTopicSystemName(key));

        return View(new LegalDocumentViewModel
        {
            OrderId = orderId,
            OrderNumber = order.GetOrderNumber(),
            Title = topic?.GetLocalized(x => x.Title).Value.NullEmpty() ?? key,
            Html = html
        });
    }

    private Task<string> GetOrderAttributeAsync(int orderId, string key)
    {
        return _db.GenericAttributes
            .AsNoTracking()
            .Where(x => x.KeyGroup == nameof(Order) && x.EntityId == orderId && x.Key == key)
            .Select(x => x.Value)
            .FirstOrDefaultAsync();
    }
}
