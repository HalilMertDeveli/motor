using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Smartstore.Core.Configuration;
using Smartstore.Core.Content.Media;
using Smartstore.Core.Data.Migrations;
using Smartstore.Core.Installation;

namespace Smartstore.Core.Tests.Content;

/// <summary>
/// MOTOR: media URLs carry "?ver=" so a product photo corrected in place replaces the copy browsers cached for 7 days.
/// </summary>
[TestFixture]
public class MediaUrlVersionTests : ServiceTestBase
{
    [Test]
    public void Setting_name_matches_the_MediaSettings_property()
    {
        Assert.That(MediaUrlVersion.SettingName, Is.EqualTo("MediaSettings.AppendFileVersionToUrl"));
        Assert.That(typeof(MediaSettings).GetProperty(MediaUrlVersion.SettingName.Split('.')[1]), Is.Not.Null);
    }

    [Test]
    public void Fresh_installs_append_the_file_version()
    {
        var settings = new MediaSettings();
        Assert.That(settings.AppendFileVersionToUrl, Is.False, "Smartstore default changed");

        MotorMediaDefaults.Apply(settings);
        Assert.That(settings.AppendFileVersionToUrl, Is.True);
    }

    [Test]
    public async Task Migration_turns_the_untouched_default_on()
    {
        DbContext.Settings.RemoveRange(DbContext.Settings.Where(x => x.Name == MediaUrlVersion.SettingName));
        DbContext.Settings.Add(new Setting { Name = MediaUrlVersion.SettingName, Value = "False", StoreId = 0 });
        await DbContext.SaveChangesAsync();

        await new MediaUrlVersion().SeedAsync(DbContext);
        await new MediaUrlVersion().SeedAsync(DbContext);

        var values = DbContext.Settings.Where(x => x.Name == MediaUrlVersion.SettingName).Select(x => x.Value).ToList();
        Assert.That(values, Is.EqualTo(new[] { "True" }));
    }

    [Test]
    public async Task Migration_adds_the_setting_when_missing()
    {
        DbContext.Settings.RemoveRange(DbContext.Settings.Where(x => x.Name == MediaUrlVersion.SettingName));
        await DbContext.SaveChangesAsync();

        await new MediaUrlVersion().SeedAsync(DbContext);

        var setting = DbContext.Settings.Single(x => x.Name == MediaUrlVersion.SettingName);
        Assert.That(setting.Value, Is.EqualTo("True"));
        Assert.That(setting.StoreId, Is.EqualTo(0));
    }
}
