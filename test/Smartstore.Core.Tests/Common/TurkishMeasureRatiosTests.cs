using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Smartstore.Core.Common;
using Smartstore.Core.Configuration;
using Smartstore.Core.Data.Migrations;
using Smartstore.Core.Installation;

namespace Smartstore.Core.Tests.Common;

/// <summary>
/// MOTOR: kilogram and centimetre are the base units, so their ratio must be 1 (Admin > System > Warnings).
/// </summary>
[TestFixture]
public class TurkishMeasureRatiosTests : ServiceTestBase
{
    [Test]
    public void Base_units_have_ratio_one_and_conversions_are_consistent()
    {
        Assert.That(TurkishStoreDefaults.WeightRatios.Single(x => x.Keyword == "kg").Ratio, Is.EqualTo(1M));
        Assert.That(TurkishStoreDefaults.DimensionRatios.Single(x => x.Keyword == "cm").Ratio, Is.EqualTo(1M));
        // 1 kg = 1000 g, 1 cm = 10 mm, 1 inch = 2.54 cm
        Assert.That(TurkishStoreDefaults.WeightRatios.Single(x => x.Keyword == "g").Ratio, Is.EqualTo(1000M));
        Assert.That(TurkishStoreDefaults.DimensionRatios.Single(x => x.Keyword == "mm").Ratio, Is.EqualTo(10M));
        Assert.That(1M / TurkishStoreDefaults.DimensionRatios.Single(x => x.Keyword == "inch").Ratio, Is.EqualTo(2.54M).Within(0.0001M));
    }

    [Test]
    public async Task Migration_rebases_seed_ratios_to_kilogram_and_centimetre()
    {
        var kg = new MeasureWeight { Name = "kilogram", SystemKeyword = "kg", Ratio = 0.45359237M };
        var lb = new MeasureWeight { Name = "libre", SystemKeyword = "lb", Ratio = 1M };
        var cm = new MeasureDimension { Name = "santimetre", SystemKeyword = "cm", Ratio = 0.254M };
        DbContext.MeasureWeights.AddRange(kg, lb);
        DbContext.MeasureDimensions.Add(cm);
        await DbContext.SaveChangesAsync();

        DbContext.Settings.RemoveRange(DbContext.Settings.Where(x => x.Name.StartsWith("MeasureSettings.")));
        DbContext.Settings.Add(new Setting { Name = "MeasureSettings.BaseWeightId", Value = kg.Id.ToString(), StoreId = 0 });
        DbContext.Settings.Add(new Setting { Name = "MeasureSettings.BaseDimensionId", Value = cm.Id.ToString(), StoreId = 0 });
        await DbContext.SaveChangesAsync();

        await new TurkishMeasureRatios().SeedAsync(DbContext);

        Assert.That(DbContext.MeasureWeights.Single(x => x.Id == kg.Id).Ratio, Is.EqualTo(1M));
        Assert.That(DbContext.MeasureWeights.Single(x => x.Id == lb.Id).Ratio, Is.EqualTo(2.20462262M));
        Assert.That(DbContext.MeasureDimensions.Single(x => x.Id == cm.Id).Ratio, Is.EqualTo(1M));
    }
}
