using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MotorShop.Iyzico.Configuration;
using MotorShop.Iyzico.Models;
using MotorShop.Iyzico.Providers;
using MotorShop.Iyzico.Services;
using Smartstore.ComponentModel;
using Smartstore.Core.Data;
using Smartstore.Core.Security;
using Smartstore.Core.Stores;
using Smartstore.Engine.Modularity;
using Smartstore.Web.Controllers;
using Smartstore.Web.Modelling.Settings;

namespace MotorShop.Iyzico.Controllers;

/// <summary>
/// AdminController brings backend access, the admin IP check and antiforgery validation for every POST.
/// </summary>
public class IyzicoAdminController : AdminController
{
    private readonly SmartDbContext _db;
    private readonly IIyzicoCredentialProvider _credentialProvider;
    private readonly IProviderManager _providerManager;
    private readonly IyzicoPaymentService _paymentService;

    public IyzicoAdminController(
        SmartDbContext db,
        IIyzicoCredentialProvider credentialProvider,
        IProviderManager providerManager,
        IyzicoPaymentService paymentService)
    {
        _db = db;
        _credentialProvider = credentialProvider;
        _providerManager = providerManager;
        _paymentService = paymentService;
    }

    [LoadSetting, Permission(Permissions.Configuration.PaymentMethod.Read)]
    public async Task<IActionResult> Configure(IyzicoSettings settings)
    {
        var model = MiniMapper.Map<IyzicoSettings, ConfigurationModel>(settings);
        await PrepareModelAsync(model, settings);

        return View(model);
    }

    [HttpPost, SaveSetting, Permission(Permissions.Configuration.PaymentMethod.Update)]
    public async Task<IActionResult> Configure(ConfigurationModel model, IyzicoSettings settings)
    {
        if (!ModelState.IsValid)
        {
            return await Configure(settings);
        }

        ModelState.Clear();

        // Keys are write-only: an empty field keeps the stored value. Never map them by name.
        var apiKey = model.ApiKey;
        var secretKey = model.SecretKey;
        model.ApiKey = null;
        model.SecretKey = null;

        MiniMapper.Map(model, settings);

        if ((apiKey.HasValue() || secretKey.HasValue()) && GetActiveStoreScopeConfiguration() > 0)
        {
            // Store-scoped saves drop settings without an override checkbox. Keys are kept for all stores only.
            NotifyWarning(T("Plugins.MotorShop.Iyzico.KeysAllStoresOnly"));
            return RedirectToAction(nameof(Configure));
        }

        if (apiKey.HasValue())
        {
            settings.ApiKeyProtected = _credentialProvider.Protect(apiKey);
        }
        if (secretKey.HasValue())
        {
            settings.SecretKeyProtected = _credentialProvider.Protect(secretKey);
        }

        return RedirectToAction(nameof(Configure));
    }

    [HttpPost, ActionName("Configure"), FormValueRequired("clearkeys"), Permission(Permissions.Configuration.PaymentMethod.Update)]
    public async Task<IActionResult> ClearKeys()
    {
        var storeScope = GetActiveStoreScopeConfiguration();
        var settings = await Services.SettingFactory.LoadSettingsAsync<IyzicoSettings>(storeScope);

        settings.ApiKeyProtected = null;
        settings.SecretKeyProtected = null;
        await Services.SettingFactory.SaveSettingsAsync(settings, storeScope);

        NotifySuccess(T("Plugins.MotorShop.Iyzico.KeysCleared"));
        return RedirectToAction(nameof(Configure));
    }

    /// <summary>
    /// Asks the iyzico transaction report whether unconfirmed refunds of a transaction were processed.
    /// </summary>
    [HttpPost, Permission(Permissions.Order.Update)]
    public async Task<IActionResult> CheckRefunds(int transactionId)
    {
        var confirmed = await _paymentService.CheckPendingRefundsAsync(transactionId, HttpContext.RequestAborted);
        if (confirmed > 0)
        {
            NotifySuccess(T("Plugins.MotorShop.Iyzico.PendingRefunds.Confirmed", confirmed));
        }
        else
        {
            NotifyWarning(T("Plugins.MotorShop.Iyzico.PendingRefunds.NotConfirmed"));
        }

        return RedirectToAction(nameof(Configure));
    }

    /// <summary>
    /// Admin decision on a refund with an unknown outcome, after checking the iyzico merchant panel.
    /// </summary>
    [HttpPost, Permission(Permissions.Order.Update)]
    public async Task<IActionResult> ResolveRefund(int transactionId, string conversationId, bool processed)
    {
        var resolvedBy = $"admin (customer id {Services.WorkContext.CurrentCustomer.Id})";
        if (await _paymentService.ResolvePendingRefundAsync(transactionId, conversationId, processed, resolvedBy, HttpContext.RequestAborted))
        {
            NotifySuccess(T(processed ? "Plugins.MotorShop.Iyzico.PendingRefunds.ResolvedProcessed" : "Plugins.MotorShop.Iyzico.PendingRefunds.ResolvedNotProcessed"));
        }
        else
        {
            NotifyWarning(T("Plugins.MotorShop.Iyzico.PendingRefunds.NotFound"));
        }

        return RedirectToAction(nameof(Configure));
    }

    private async Task PrepareModelAsync(ConfigurationModel model, IyzicoSettings settings)
    {
        ViewBag.Provider = _providerManager.GetProvider(IyzicoProvider.SystemName).Metadata;

        var store = Services.StoreContext.CurrentStore;
        var credentials = await _credentialProvider.GetCredentialsAsync(GetActiveStoreScopeConfiguration());

        model.ApiKey = null;
        model.SecretKey = null;
        model.HasApiKey = settings.ApiKeyProtected.HasValue();
        model.HasSecretKey = settings.SecretKeyProtected.HasValue();
        model.HasConfigurationOverride = _credentialProvider.HasConfigurationOverride;
        model.SkipResponseSignatureVerification = _credentialProvider.SkipResponseSignatureVerification;
        model.AllowUnsignedWebhooks = _credentialProvider.AllowUnsignedWebhooks;
        model.PendingRefunds = await _paymentService.GetPendingRefundsAsync();
        model.EffectiveEndpoint = credentials.BaseUrl;
        model.CallbackUrl = store.GetAbsoluteUrl("iyzico/callback");
        model.WebhookUrl = store.GetAbsoluteUrl("iyzico/webhook");

        model.RecentTransactions = await _db.IyzicoTransactions()
            .AsNoTracking()
            .OrderByDescending(x => x.Id)
            .Take(25)
            .Select(x => new IyzicoTransactionRow
            {
                OrderId = x.OrderId,
                Status = x.Status,
                Amount = x.Amount,
                PaidPrice = x.PaidPrice,
                RefundedAmount = x.RefundedAmount,
                Currency = x.Currency,
                Installment = x.Installment,
                PaymentId = x.PaymentId,
                LastErrorCode = x.LastErrorCode,
                CreatedOnUtc = x.CreatedOnUtc,
                UpdatedOnUtc = x.UpdatedOnUtc
            })
            .ToListAsync();
    }
}
