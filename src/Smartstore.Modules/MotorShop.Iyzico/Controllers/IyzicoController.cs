using System.IO;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MotorShop.Iyzico.Client;
using MotorShop.Iyzico.Configuration;
using MotorShop.Iyzico.Domain;
using MotorShop.Iyzico.Models;
using MotorShop.Iyzico.Services;
using Smartstore.Core.Checkout.Payment;
using Smartstore.Core.Data;
using Smartstore.Core.Identity;
using Smartstore.Core.Stores;
using Smartstore.Web.Controllers;

namespace MotorShop.Iyzico.Controllers;

public class IyzicoController : PublicController
{
    public const string SignatureHeader = "X-IYZ-SIGNATURE-V3";

    private readonly SmartDbContext _db;
    private readonly IyzicoPaymentService _paymentService;
    private readonly IIyzicoCredentialProvider _credentialProvider;

    public IyzicoController(
        SmartDbContext db,
        IyzicoPaymentService paymentService,
        IIyzicoCredentialProvider credentialProvider)
    {
        _db = db;
        _paymentService = paymentService;
        _credentialProvider = credentialProvider;
    }

    /// <summary>
    /// Initializes a checkout form for a placed order and redirects to the hosted iyzico page.
    /// Reached from checkout (PostProcessPayment) and from "complete payment" on the order page.
    /// </summary>
    [HttpGet, Route("iyzico/pay/{orderGuid:guid}")]
    public async Task<IActionResult> Pay(Guid orderGuid)
    {
        var order = await _db.Orders
            .Include(x => x.Customer)
            .Include(x => x.BillingAddress).ThenInclude(x => x.Country)
            .Include(x => x.BillingAddress).ThenInclude(x => x.StateProvince)
            .Include(x => x.ShippingAddress).ThenInclude(x => x.Country)
            .Include(x => x.ShippingAddress).ThenInclude(x => x.StateProvince)
            .Include(x => x.OrderItems).ThenInclude(x => x.Product)
            .FirstOrDefaultAsync(x => x.OrderGuid == orderGuid);

        if (order == null || order.Deleted || order.CustomerId != Services.WorkContext.CurrentCustomer.Id)
        {
            return NotFound();
        }

        var store = Services.StoreContext.GetStoreById(order.StoreId) ?? Services.StoreContext.CurrentStore;
        var callbackUrl = store.GetAbsoluteUrl("iyzico/callback");
        var locale = IyzicoRequestBuilder.GetLocale(Services.WorkContext.WorkingLanguage?.LanguageCulture);

        var result = await _paymentService.StartPaymentAsync(order, callbackUrl, locale, HttpContext.RequestAborted);

        switch (result.Status)
        {
            case IyzicoStartStatus.Redirect:
                return Redirect(result.RedirectUrl);
            case IyzicoStartStatus.AlreadyPaid:
            case IyzicoStartStatus.NotPayable:
                return RedirectToAction(nameof(Return), new { orderGuid });
            case IyzicoStartStatus.NotConfigured:
                NotifyError(T("Plugins.MotorShop.Iyzico.NotConfigured"));
                break;
            case IyzicoStartStatus.UnsupportedCurrency:
                NotifyError(T("Plugins.MotorShop.Iyzico.UnsupportedCurrency", order.CustomerCurrencyCode));
                break;
            default:
                NotifyError(T("Plugins.MotorShop.Iyzico.StartFailed"));
                break;
        }

        return View("Return", await CreateReturnModelAsync(order, startFailed: true));
    }

    /// <summary>
    /// Customer return from the hosted form. iyzico posts the token cross-site, usually without the session cookie,
    /// so the payment is correlated through the stored transaction only.
    /// </summary>
    // WebhookEndpoint: without the cross-site (Lax) cookie Smartstore would otherwise create a new guest and overwrite
    // the buyer's visitor cookie, so the buyer could no longer reach Completed or retry. The callback needs no customer.
    [HttpPost, Route("iyzico/callback"), IgnoreAntiforgeryToken, WebhookEndpoint]
    [RequestSizeLimit(16 * 1024)]
    public async Task<IActionResult> Callback([FromForm] string token)
    {
        var result = await _paymentService.ProcessTokenAsync(token, IyzicoEventSource.Callback, HttpContext.RequestAborted);

        var orderGuid = result.OrderGuid;
        if (orderGuid == null && result.Outcome != IyzicoOutcome.UnknownToken)
        {
            orderGuid = await (
                from tx in _db.IyzicoTransactions().AsNoTracking()
                join o in _db.Orders.AsNoTracking() on tx.OrderId equals o.Id
                where tx.Token == token
                select (Guid?)o.OrderGuid).FirstOrDefaultAsync();
        }

        if (orderGuid == null)
        {
            NotifyError(T("Plugins.MotorShop.Iyzico.UnknownPayment"));
            return RedirectToRoute("Homepage");
        }

        // 303 turns the cross-site POST into a top-level GET, which carries the Lax session cookie again.
        Response.Headers.Location = Url.Action(nameof(Return), new { orderGuid });
        return StatusCode(StatusCodes.Status303SeeOther);
    }

    [HttpGet, Route("iyzico/return/{orderGuid:guid}")]
    public async Task<IActionResult> Return(Guid orderGuid)
    {
        var order = await _db.Orders.AsNoTracking().FirstOrDefaultAsync(x => x.OrderGuid == orderGuid);
        if (order == null || order.Deleted)
        {
            return NotFound();
        }

        var isOwner = order.CustomerId == Services.WorkContext.CurrentCustomer.Id;
        if (isOwner && order.PaymentStatus == PaymentStatus.Pending)
        {
            // The owner reloads the page while the payment is still open: check iyzico again.
            var openToken = await _db.IyzicoTransactions()
                .AsNoTracking()
                .Where(x => x.OrderId == order.Id && (x.Status == IyzicoTransactionStatus.Initialized || x.Status == IyzicoTransactionStatus.Pending))
                .OrderByDescending(x => x.Id)
                .Select(x => x.Token)
                .FirstOrDefaultAsync();

            if (openToken != null)
            {
                await _paymentService.ProcessTokenAsync(openToken, IyzicoEventSource.Callback, HttpContext.RequestAborted);
                order = await _db.Orders.AsNoTracking().FirstAsync(x => x.Id == order.Id);
            }
        }

        if (isOwner && order.PaymentStatus is PaymentStatus.Paid or PaymentStatus.Authorized)
        {
            return RedirectToAction("Completed", "Checkout");
        }

        return View(await CreateReturnModelAsync(order, startFailed: false));
    }

    /// <summary>
    /// iyzico webhook (Checkout Form format). Authenticated with X-IYZ-SIGNATURE-V3. The body is only a hint:
    /// the payment result is always retrieved from the iyzico API before anything changes.
    /// Non-2xx makes iyzico retry, so transient errors return 500 and everything else 200.
    /// </summary>
    [HttpPost, Route("iyzico/webhook"), WebhookEndpoint, IgnoreAntiforgeryToken]
    [RequestSizeLimit(64 * 1024)]
    public async Task<IActionResult> Webhook()
    {
        IyzicoWebhookPayload payload;
        try
        {
            using var reader = new StreamReader(Request.Body);
            var json = await reader.ReadToEndAsync(HttpContext.RequestAborted);
            payload = JsonSerializer.Deserialize<IyzicoWebhookPayload>(json, IyzicoApiClient.JsonOptions);
        }
        catch (JsonException)
        {
            return BadRequest();
        }

        if (payload == null || payload.Token.IsEmpty())
        {
            // Not a checkout form event (e.g. direct API payment). Nothing to do for this module.
            return Ok();
        }

        var tx = await _db.IyzicoTransactions()
            .AsNoTracking()
            .Where(x => x.Token == payload.Token)
            .Select(x => new { x.StoreId })
            .FirstOrDefaultAsync();

        var storeId = tx?.StoreId ?? Services.StoreContext.CurrentStore.Id;
        var settings = await Services.SettingFactory.LoadSettingsAsync<IyzicoSettings>(storeId);
        var credentials = await _credentialProvider.GetCredentialsAsync(storeId);
        var auth = IyzicoWebhookVerifier.Verify(payload, Request.Headers[SignatureHeader].ToString(), credentials.SecretKey, settings.RequireWebhookSignature);
        if (auth is IyzicoWebhookAuth.Invalid or IyzicoWebhookAuth.Missing)
        {
            Logger.LogWarning("iyzico webhook rejected: signature {Auth} (event {EventType}).", auth, payload.IyziEventType);
            return Unauthorized();
        }

        if (tx == null)
        {
            Logger.LogInformation("iyzico webhook for an unknown token ignored (event {EventType}).", payload.IyziEventType);
            return Ok();
        }

        var result = await _paymentService.ProcessTokenAsync(payload.Token, IyzicoEventSource.Webhook, HttpContext.RequestAborted);

        return result.Outcome == IyzicoOutcome.Error ? StatusCode(500) : Ok();
    }

    private async Task<IyzicoReturnModel> CreateReturnModelAsync(Smartstore.Core.Checkout.Orders.Order order, bool startFailed)
    {
        var isOwner = order.CustomerId == Services.WorkContext.CurrentCustomer.Id;
        var lastStatus = await _db.IyzicoTransactions()
            .AsNoTracking()
            .Where(x => x.OrderId == order.Id)
            .OrderByDescending(x => x.Id)
            .Select(x => (IyzicoTransactionStatus?)x.Status)
            .FirstOrDefaultAsync();

        var state = order.PaymentStatus switch
        {
            PaymentStatus.Paid => IyzicoReturnState.Paid,
            PaymentStatus.Authorized => IyzicoReturnState.InReview,
            _ => lastStatus switch
            {
                IyzicoTransactionStatus.Failed or IyzicoTransactionStatus.Expired => IyzicoReturnState.Failed,
                IyzicoTransactionStatus.VerificationFailed or IyzicoTransactionStatus.Duplicate => IyzicoReturnState.InReview,
                null when startFailed => IyzicoReturnState.Failed,
                _ when startFailed => IyzicoReturnState.Failed,
                _ => IyzicoReturnState.Processing
            }
        };

        return new IyzicoReturnModel
        {
            State = state,
            IsOwner = isOwner,
            OrderId = isOwner ? order.Id : null,
            OrderNumber = isOwner ? order.GetOrderNumber() : null,
            OrderGuid = order.OrderGuid,
            CanRetry = isOwner && state == IyzicoReturnState.Failed && IyzicoPaymentService.IsPayable(order)
        };
    }
}
