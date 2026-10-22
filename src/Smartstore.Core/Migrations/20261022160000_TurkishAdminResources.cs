using System.Xml.Linq;
using FluentMigrator;
using Smartstore.Core.Installation;
using Smartstore.Core.Localization;
using Smartstore.Data.Migrations;
using Smartstore.IO;

namespace Smartstore.Core.Data.Migrations;

/// <summary>
/// MOTOR: Turkish admin texts for EXISTING databases. The tr language pack used to carry English values for
/// the admin-only resources, so databases installed or migrated from it show an English admin.
/// Fresh installs read the corrected pack directly.
///
/// Copies every Turkish value of the pack (App_Data/Localization/App/tr) into each tr-TR language, but only
/// where the stored value still equals the English value of the pack: texts an admin edited are kept.
/// Missing resources are added. Runs again harmlessly after later pack updates.
/// </summary>
[MigrationVersion("2026-10-22 16:00:00", "MOTOR: Turkish admin resources")]
internal class TurkishAdminResources : Migration, IDataSeeder<SmartDbContext>
{
    public override void Up()
    {
    }

    public override void Down()
    {
    }

    public DataSeederStage Stage => DataSeederStage.Early;
    public bool AbortOnFailure => false;

    public async Task SeedAsync(SmartDbContext context, CancellationToken cancelToken = default)
    {
        var languages = await context.Languages
            .Where(x => x.LanguageCulture == TurkishStoreDefaults.Culture)
            .ToListAsync(cancelToken);

        if (languages.Count == 0)
        {
            return;
        }

        var appData = EngineContext.Current.Application.AppDataRoot;
        var english = LoadPack(appData.GetFile("Localization/App/en/all.smres.xml"));
        var turkish = LoadPack(appData.GetFile("Localization/App/" + TurkishStoreDefaults.UniqueSeoCode + "/all.smres.xml"));

        if (english.Count == 0 || turkish.Count == 0)
        {
            return;
        }

        foreach (var language in languages)
        {
            var stored = (await context.LocaleStringResources
                .Where(x => x.LanguageId == language.Id)
                .ToListAsync(cancelToken))
                .ToDictionarySafe(x => x.ResourceName, StringComparer.OrdinalIgnoreCase);

            foreach (var (name, value) in turkish)
            {
                if (!english.TryGetValue(name, out var englishValue) || value == englishValue)
                {
                    continue;
                }

                if (!stored.TryGetValue(name, out var resource))
                {
                    context.LocaleStringResources.Add(new LocaleStringResource
                    {
                        LanguageId = language.Id,
                        ResourceName = name,
                        ResourceValue = value
                    });
                }
                else if (NormalizeLineEndings(resource.ResourceValue) == englishValue)
                {
                    resource.ResourceValue = value;
                }
            }

            await context.SaveChangesAsync(cancelToken);
        }

        context.DetachEntities<LocaleStringResource>();
    }

    // Installed values may use CRLF while XDocument normalizes line endings to LF.
    private static string NormalizeLineEndings(string value)
        => value?.Replace("\r\n", "\n");

    private static Dictionary<string, string> LoadPack(IFile file)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!file.Exists)
        {
            return result;
        }

        // The packs are flat: one LocaleResource element per resource, no nested children.
        foreach (var el in XDocument.Load(file.PhysicalPath).Root.Elements("LocaleResource"))
        {
            var name = (string)el.Attribute("Name");
            if (name.HasValue())
            {
                result[name] = (string)el.Element("Value") ?? string.Empty;
            }
        }

        return result;
    }
}
