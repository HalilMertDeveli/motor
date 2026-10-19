using Microsoft.EntityFrameworkCore;
using Smartstore.Core.Data;
using Smartstore.Core.Messaging;

namespace MotorShop.Tracking.Services;

/// <summary>
/// Applies the Turkish shipment e-mail texts shipped with the module as localized values
/// of the existing Smartstore message templates. Nothing is overwritten: a template that already has
/// a Turkish subject or body (e.g. from the localization work) is left alone.
/// </summary>
public class ShipmentTemplateLocalizer
{
    internal const string RootPath = "/Modules/MotorShop.Tracking/App_Data/EmailTemplates/";

    internal static readonly string[] TemplateNames =
    [
        MessageTemplateNames.ShipmentSentCustomer,
        MessageTemplateNames.ShipmentDeliveredCustomer
    ];

    private readonly SmartDbContext _db;
    private readonly IMessageTemplateService _templateService;
    private readonly ILocalizedEntityService _localizedEntityService;

    public ShipmentTemplateLocalizer(SmartDbContext db, IMessageTemplateService templateService, ILocalizedEntityService localizedEntityService)
    {
        _db = db;
        _templateService = templateService;
        _localizedEntityService = localizedEntityService;
    }

    /// <returns>Number of localized values written.</returns>
    public async Task<int> ApplyAsync()
    {
        var languages = await _db.Languages
            .AsNoTracking()
            .Where(x => x.LanguageCulture.StartsWith("tr"))
            .ToListAsync();

        if (languages.Count == 0)
        {
            return 0;
        }

        var written = 0;

        foreach (var name in TemplateNames)
        {
            var source = _templateService.LoadTemplate(name, "tr", RootPath);
            var targets = await _db.MessageTemplates.Where(x => x.Name == name).ToListAsync();

            foreach (var target in targets)
            {
                foreach (var language in languages)
                {
                    if (_localizedEntityService.GetLocalizedValue(language.Id, target.Id, nameof(MessageTemplate), nameof(MessageTemplate.Subject)).IsEmpty())
                    {
                        await _localizedEntityService.ApplyLocalizedValueAsync(target, x => x.Subject, source.Subject, language.Id);
                        written++;
                    }

                    if (_localizedEntityService.GetLocalizedValue(language.Id, target.Id, nameof(MessageTemplate), nameof(MessageTemplate.Body)).IsEmpty())
                    {
                        await _localizedEntityService.ApplyLocalizedValueAsync(target, x => x.Body, source.Body, language.Id);
                        written++;
                    }
                }
            }
        }

        await _db.SaveChangesAsync();
        return written;
    }
}
