using FluentMigrator;
using Smartstore.Core.Common;
using Smartstore.Core.Installation;
using Smartstore.Data.Migrations;

namespace Smartstore.Core.Data.Migrations;

/// <summary>
/// MOTOR: Turkish installs use kilogram and centimetre as base units but kept the pound/inch based ratios of the
/// invariant seed, so Admin > System > Warnings showed two red warnings ("Oran 1 olarak ayarlanmalıdır") and weight
/// or size conversions were wrong. Sets <see cref="TurkishStoreDefaults.WeightRatios"/> and
/// <see cref="TurkishStoreDefaults.DimensionRatios"/> in EXISTING databases.
///
/// Only runs while the base units are kilogram and centimetre and their ratio is not 1 yet (untouched seed values).
/// </summary>
[MigrationVersion("2026-10-26 21:00:00", "MOTOR: Turkish measure ratios")]
internal class TurkishMeasureRatios : Migration, IDataSeeder<SmartDbContext>
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
        var settings = await context.Settings
            .Where(x => x.StoreId == 0 && (x.Name == "MeasureSettings.BaseWeightId" || x.Name == "MeasureSettings.BaseDimensionId"))
            .ToDictionaryAsync(x => x.Name, x => x.Value.ToInt(), cancelToken);

        var weights = await context.MeasureWeights.ToListAsync(cancelToken);
        var baseWeight = weights.FirstOrDefault(x => x.Id == settings.Get("MeasureSettings.BaseWeightId"));
        if (baseWeight?.SystemKeyword == "kg" && baseWeight.Ratio != 1M)
        {
            Apply(weights, x => x.SystemKeyword, (x, r) => x.Ratio = r, TurkishStoreDefaults.WeightRatios);
        }

        var dimensions = await context.MeasureDimensions.ToListAsync(cancelToken);
        var baseDimension = dimensions.FirstOrDefault(x => x.Id == settings.Get("MeasureSettings.BaseDimensionId"));
        if (baseDimension?.SystemKeyword == "cm" && baseDimension.Ratio != 1M)
        {
            Apply(dimensions, x => x.SystemKeyword, (x, r) => x.Ratio = r, TurkishStoreDefaults.DimensionRatios);
        }

        await context.SaveChangesAsync(cancelToken);
    }

    private static void Apply<T>(List<T> entities, Func<T, string> keyword, Action<T, decimal> set, (string Keyword, decimal Ratio)[] ratios)
    {
        foreach (var (key, ratio) in ratios)
        {
            foreach (var entity in entities.Where(x => keyword(x) == key))
            {
                set(entity, ratio);
            }
        }
    }
}
