using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Smartstore.Core.Configuration;
using Smartstore.Core.Data.Migrations;

namespace Smartstore.Core.Tests.Catalog;

/// <summary>
/// MOTOR: the MotorDark compare page builds its feature rows from the full descriptions.
/// Its texts are checked by TurkishLocalizationTests.MotorDark_Views_Only_Use_Resources_With_Turkish_Values.
/// </summary>
[TestFixture]
public class MotorCompareProductsTests : ServiceTestBase
{
    const string SettingName = "CatalogSettings.IncludeFullDescriptionInCompareProducts";

    [Test]
    public async Task Migration_turns_on_full_descriptions_in_compare()
    {
        DbContext.Settings.RemoveRange(DbContext.Settings.Where(x => x.Name == SettingName));
        DbContext.Settings.Add(new Setting { Name = SettingName, Value = "False", StoreId = 0 });
        await DbContext.SaveChangesAsync();

        await new MotorCompareProducts().SeedAsync(DbContext);

        Assert.That(DbContext.Settings.Where(x => x.Name == SettingName).Select(x => x.Value).ToList(), Is.EqualTo(new[] { "True" }));
    }

    [Test]
    public async Task Migration_adds_the_setting_when_it_was_never_saved()
    {
        DbContext.Settings.RemoveRange(DbContext.Settings.Where(x => x.Name == SettingName));
        await DbContext.SaveChangesAsync();

        await new MotorCompareProducts().SeedAsync(DbContext);

        var setting = DbContext.Settings.Single(x => x.Name == SettingName);
        Assert.That(setting.Value, Is.EqualTo("True"));
        Assert.That(setting.StoreId, Is.EqualTo(0));
    }
}
