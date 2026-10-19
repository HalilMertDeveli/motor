using MotorShop.Legal.Configuration;
using MotorShop.Legal.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Checkout.Orders.Events;
using Smartstore.Core.Data;
using Smartstore.Events;

namespace MotorShop.Legal;

internal class Events : IConsumer
{
    private readonly SmartDbContext _db;
    private readonly ICheckoutStateAccessor _checkoutStateAccessor;
    private readonly LegalDocumentService _documentService;
    private readonly LegalCheckoutSettings _settings;

    public Events(
        SmartDbContext db,
        ICheckoutStateAccessor checkoutStateAccessor,
        LegalDocumentService documentService,
        LegalCheckoutSettings settings)
    {
        _db = db;
        _checkoutStateAccessor = checkoutStateAccessor;
        _documentService = documentService;
        _settings = settings;
    }

    public Localizer T { get; set; } = NullLocalizer.Instance;

    public ILogger Logger { get; set; } = NullLogger.Instance;

    /// <summary>
    /// Persists the acknowledgment record and a snapshot of the documents rendered with the placed order.
    /// Runs after the order transaction has been committed, in the customer's request.
    /// </summary>
    public async Task HandleEventAsync(OrderPlacedEvent message)
    {
        if (!_settings.Enabled)
        {
            return;
        }

        var state = _checkoutStateAccessor.CheckoutState;
        if (state == null
            || !state.CustomProperties.TryGetValue(LegalAcknowledgment.CheckoutStateKey, out var value)
            || LegalAcknowledgment.Deserialize(value?.ToString()) is not LegalAcknowledgment ack)
        {
            // Orders created without the storefront confirm step (admin, recurring, API) have no record.
            return;
        }

        var order = message.Order;

        try
        {
            var data = await _documentService.CreateDataAsync(order);
            var documents = await _documentService.RenderAllAsync(data);

            ack.ApplyOrderDocuments(documents);

            order.GenericAttributes.Set(LegalAcknowledgment.OrderAttributeKey, ack.Serialize());
            foreach (var doc in documents)
            {
                order.GenericAttributes.Set(LegalAcknowledgment.SnapshotAttributeKeyPrefix + doc.Key, doc.Html);
            }

            var summary = string.Join(", ", ack.Documents.Select(x => $"{x.TopicSystemName} ({x.TemplateHash?[..12]})"));
            _db.OrderNotes.Add(order, T("Plugins.MotorShop.Legal.OrderNote",
                ack.Accepted ? T("Common.Yes").Value : T("Common.No").Value,
                ack.AcceptedOnUtc.ToString("u"),
                summary), false);

            await _db.SaveChangesAsync();

            state.CustomProperties.Remove(LegalAcknowledgment.CheckoutStateKey);
        }
        catch (Exception ex)
        {
            // Never fail the (already committed) order placement because of the record.
            Logger.Error(ex, $"Could not store the legal acknowledgment for order {order.Id}.");
        }
    }
}
