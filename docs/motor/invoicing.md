# Invoicing: what Smartstore has, and the e-Arşiv / e-Fatura boundary

> **A Smartstore order PDF is not a legal invoice in Türkiye.** It has no invoice number, no GİB e-Arşiv / e-Fatura
> envelope, no UUID or signature, and it is not reported to GİB. It must not be presented or named as a
> "fatura" / "e-Arşiv fatura" to customers.

## 1. What Smartstore provides (audited, 6.4)

| Capability | Where | Notes |
|---|---|---|
| Printable order | `OrderController.Print(id, pdf)` → view `Order/Details.Print` | Titled "Order Details": address, order number, date, methods, lines, totals. Runtime: rendered for order #2 |
| Order PDF | `OrderHelper.GeneratePdfAsync` → `IPdfConverter` | wkhtmltopdf, bundled in core (`services.AddWkHtmlToPdf()`); the native binary is downloaded as NuGet package `Smartstore.wkhtmltopdf.Native` at first use if missing. File name resource `Order.PdfInvoiceFileName` uses the order **Id** |
| Admin | Order edit: "Print", "PDF invoice" (if `PdfSettings.Enabled`), bulk "Export to PDF" | |
| E-mail attachment | `PdfSettings.AttachOrderPdfToOrderPlacedEmail`, `AttachOrderPdfToOrderCompletedEmail` (both off by default) | Self HTTP call `/order/print?pdf=true` with a 10 s timeout; failures are only logged |
| Numbering | `Order.OrderNumber` (string) – **nothing in core assigns it**, `GetOrderNumber()` falls back to `Id` | No invoice number, no sequence, no fiscal year reset |
| Billing data | `Order.BillingAddress` (copied at placement), `Customer` tax fields (VAT id) | No TCKN / VKN / tax office fields (deliberately not collected, see legal checkout) |

Not verified at runtime: the PDF itself (the wkhtmltopdf native package was not downloaded on the QA machine).

Recommendation until an e-Arşiv provider is live: keep `PdfSettings.Enabled` for internal use only, do not attach the PDF
to customer e-mails, and change the "PDF invoice" wording to "Sipariş özeti" (resource text is owned by the localization work).

## 2. Integration boundary (provider neutral)

e-Arşiv / e-Fatura is issued by a GİB-authorised integrator (özel entegratör). The integrator is **not chosen**.
The boundary below keeps Smartstore order processing unchanged and puts the integrator behind one interface,
implemented later in its own module (e.g. `MotorShop.EInvoice.<Provider>`), the same way payment and shipping providers plug in.

### When an invoice is issued

The trigger is an owner / accountant decision. Options, all available as Smartstore events without core changes:

| Trigger | Event | Notes |
|---|---|---|
| Payment received | `OrderPaidEvent` | Common for prepaid card orders |
| Shipment sent | `TrackingNumberAddedEvent` or the admin "set as shipped" action | Invoice travels with the goods |
| Manual | Admin button on the order (tab via `TabStripCreated`, `order-edit`) | Fallback and corrections |

Cancellation after issuing → cancellation / return invoice (iade faturası) via the same provider; refund events are not
published by Smartstore (`RefundAsync` only updates the order), so the module must hook the admin refund action or poll.

### Contract

```csharp
namespace MotorShop.EInvoice;

/// Implemented by one module per integrator. Registered as a Smartstore provider.
public interface IEInvoiceProvider
{
    /// e.g. "EInvoice.ProviderX"
    string SystemName { get; }

    /// Asks the integrator whether the buyer is an e-Fatura taxpayer (VKN lookup) to choose e-Fatura vs. e-Arşiv.
    Task<EInvoiceRecipientType> GetRecipientTypeAsync(EInvoiceBuyer buyer, CancellationToken cancelToken = default);

    /// Issues the document. Must be idempotent per Order.OrderGuid (the provider stores it as the external reference).
    Task<EInvoiceResult> IssueAsync(EInvoiceRequest request, CancellationToken cancelToken = default);

    /// Cancels an issued e-Arşiv document or issues a return document, depending on the integrator and the legal deadline.
    Task<EInvoiceResult> CancelAsync(string documentUuid, string reason, CancellationToken cancelToken = default);

    /// Status at the integrator / GİB (queued, issued, rejected, cancelled).
    Task<EInvoiceStatus> GetStatusAsync(string documentUuid, CancellationToken cancelToken = default);

    /// The legally valid PDF / HTML rendering produced by the integrator, to show or e-mail to the customer.
    Task<EInvoiceFile> DownloadAsync(string documentUuid, CancellationToken cancelToken = default);
}

public enum EInvoiceRecipientType { EArsiv, EFatura }

public record EInvoiceBuyer(
    string Name, string Email, string Phone,
    string Address, string City, string CountryCode,
    string TaxNumber,      // VKN for companies, only if the buyer entered one for an invoice; never collected by default
    string TaxOffice);

public record EInvoiceLine(string Sku, string Name, decimal Quantity, string Unit,
    decimal UnitPriceExclTax, decimal VatRate, decimal VatAmount, decimal LineTotalInclTax);

public record EInvoiceRequest(
    Guid OrderGuid, string OrderNumber, DateTime OrderDateUtc, string CurrencyCode,
    EInvoiceBuyer Buyer, IReadOnlyList<EInvoiceLine> Lines,
    decimal ShippingExclTax, decimal ShippingVatRate,
    decimal TotalExclTax, decimal TotalVat, decimal TotalInclTax,
    string PaymentMethod, string PaymentReference,
    EInvoiceRecipientType RecipientType);

public record EInvoiceResult(bool Success, string DocumentUuid, string DocumentNumber, string Error);
public record EInvoiceStatus(string DocumentUuid, string State, string Message);
public record EInvoiceFile(string FileName, string ContentType, byte[] Content);
```

### Storage on the order (no core table change)

| Data | Store |
|---|---|
| Document UUID, number, type, state, issued at | Order generic attributes `MotorShop.EInvoice.*` (or a module table if querying / reporting is needed; then mind the fresh-install trap: EF mapping + idempotent migration) |
| Errors / retries | Order notes (admin only) and the module's own retry task |
| Customer copy | Link on the order details page through a widget zone, served from `DownloadAsync` |

### Rules for the implementation

- Idempotent per `OrderGuid`: a second issue call returns the existing document.
- Run outside the order placement transaction (event consumer or scheduled task) so an integrator outage never blocks checkout.
- Retries with back-off in a scheduled task; never drop silently (see the QueuedEmail lesson in `order-operations.md`).
- Amounts from the order in TRY; KDV rates per line from `OrderItem` tax data (the localization work owns TRY/KDV setup).
- No card data and no TCKN unless the accountant requires it for a specific case (then explicit field + KVKK notice).

## 3. Decisions for the owner and the accountant

1. Integrator (özel entegratör) and contract.
2. Trigger: payment, shipment or manual.
3. e-Fatura vs. e-Arşiv detection (VKN lookup) and whether B2B buyers may enter VKN / tax office at checkout.
4. Invoice numbering series and whether `Order.OrderNumber` gets its own sequence (today it is the order id).
5. Cancellation / return invoice process and deadlines.
6. Whether the Smartstore PDF stays visible to customers at all until then.
