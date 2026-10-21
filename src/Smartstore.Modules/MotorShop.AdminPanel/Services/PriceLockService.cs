using MotorShop.AdminPanel.Configuration;
using Smartstore.Core;
using Smartstore.Core.Configuration;
using Smartstore.Core.Logging;

namespace MotorShop.AdminPanel.Services;

public class PriceLockService
{
    private readonly ICommonServices _services;
    private readonly ISettingFactory _settingFactory;

    public PriceLockService(ICommonServices services, ISettingFactory settingFactory)
    {
        _services = services;
        _settingFactory = settingFactory;
    }

    public Localizer T { get; set; } = NullLocalizer.Instance;

    public async Task<MotorPanelSettings> GetAsync()
        => await _settingFactory.LoadSettingsAsync<MotorPanelSettings>();

    /// <summary>
    /// Switches the store-wide price lock. Only the setting changes; prices, orders and invoices stay as they are.
    /// </summary>
    public async Task<MotorPanelSettings> SetAsync(bool hideAllPrices)
    {
        var settings = await _settingFactory.LoadSettingsAsync<MotorPanelSettings>();
        if (settings.HideAllPrices == hideAllPrices)
        {
            return settings;
        }

        var customer = _services.WorkContext.CurrentCustomer;
        settings.HideAllPrices = hideAllPrices;
        settings.HideAllPricesChangedOnUtc = DateTime.UtcNow;
        settings.HideAllPricesChangedBy = customer.Email.NullEmpty() ?? customer.Id.ToStringInvariant();

        await _settingFactory.SaveSettingsAsync(settings);

        _services.ActivityLogger.LogActivity(
            KnownActivityLogTypes.EditSettings,
            T(hideAllPrices ? "Plugins.MotorShop.AdminPanel.Log.PriceLockOn" : "Plugins.MotorShop.AdminPanel.Log.PriceLockOff"));
        await _services.DbContext.SaveChangesAsync();

        return settings;
    }
}
