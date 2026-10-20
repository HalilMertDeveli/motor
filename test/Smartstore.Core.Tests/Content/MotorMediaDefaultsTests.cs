using System.Linq;
using NUnit.Framework;
using Smartstore.Core.Content.Media;
using Smartstore.Core.Installation;

namespace Smartstore.Core.Tests.Content;

/// <summary>
/// MOTOR: the picture size defaults must name real MediaSettings properties and use allowed thumbnail sizes,
/// otherwise the migration writes settings nobody reads or thumbnails are requested in sizes Smartstore refuses.
/// </summary>
[TestFixture]
public class MotorMediaDefaultsTests
{
    [Test]
    public void Setting_names_match_MediaSettings_properties_and_Apply()
    {
        var settings = new MediaSettings();
        MotorMediaDefaults.Apply(settings);

        foreach (var (name, smartstoreDefault, value) in MotorMediaDefaults.Settings)
        {
            var propertyName = name.Split('.')[1];
            var property = typeof(MediaSettings).GetProperty(propertyName);

            Assert.That(property, Is.Not.Null, name);
            Assert.That(property.GetValue(new MediaSettings()), Is.EqualTo(smartstoreDefault), $"{name}: Smartstore default changed");
            Assert.That(property.GetValue(settings), Is.EqualTo(value), $"{name}: Apply and Settings disagree");
        }
    }

    [Test]
    public void Picture_sizes_are_allowed_thumbnail_sizes()
    {
        var settings = new MediaSettings();
        MotorMediaDefaults.Apply(settings);
        var allowed = settings.GetAllowedThumbnailSizes();

        Assert.That(allowed, Does.Contain(settings.ProductThumbPictureSize));
        Assert.That(allowed, Does.Contain(settings.ProductDetailsPictureSize));
        Assert.That(allowed, Does.Contain(settings.CategoryThumbPictureSize));
        Assert.That(allowed, Does.Contain(settings.ManufacturerThumbPictureSize));
        Assert.That(MotorMediaDefaults.Settings.Select(x => x.Name).Distinct().Count(), Is.EqualTo(MotorMediaDefaults.Settings.Length));
    }
}
