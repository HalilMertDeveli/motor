using Smartstore.Core.Common.Configuration;
using Smartstore.Core.Configuration;
using Smartstore.Data.Migrations;
using Smartstore.Utilities;

namespace Smartstore.Core.Data.Migrations;

public class SmartDbContextDataSeeder : IDataSeeder<SmartDbContext>
{
    public DataSeederStage Stage => DataSeederStage.Early;
    public bool AbortOnFailure => false;

    public async Task SeedAsync(SmartDbContext context, CancellationToken cancelToken = default)
    {
        await context.MigrateLocaleResourcesAsync(MigrateLocaleResources);
        await MigrateSettingsAsync(context, cancelToken);
    }

    public async Task MigrateSettingsAsync(SmartDbContext context, CancellationToken cancelToken = default)
    {

    }

    public void MigrateLocaleResources(LocaleResourcesBuilder builder)
    {
        builder.AddOrUpdate("Enums.QuantizationMethod.Octree", "Hexadecatree (formerly Octree)", "Hexadecatree (vormals Octree)");
        builder.AddOrUpdate("Enums.JpegColorType.YCbCrRatio422", "YCbCr 4:2:2", "YCbCr 4:2:2");
        builder.AddOrUpdate("Enums.JpegColorType.YCbCrRatio411", "YCbCr 4:1:1", "YCbCr 4:1:1");
        builder.AddOrUpdate("Enums.JpegColorType.YCbCrRatio410", "YCbCr 4:1:0", "YCbCr 4:1:0");
        builder.AddOrUpdate("Admin.Configuration.Settings.Media.GifQuantizationMethod", "GIF palette quantization", "GIF-Palettenquantisierung");

        builder.AddOrUpdate("Admin.Configuration.Settings.Media.JpegColorType",
            "JPEG color type",
            "JPEG-Farbtyp",
            "Leave empty for automatic selection.",
            "Leer lassen für automatische Auswahl.");
        builder.AddOrUpdate("Admin.Configuration.Settings.Media.PngQuantizationMethod",
            "PNG palette quantization",
            "PNG-Palettenquantisierung",
            "Only affects palette-based PNG images. Has no effect on RGB or RGBA images.",
            "Wirkt nur auf PNG-Bilder mit Farbpalette. Hat bei RGB- und RGBA-Bildern keine Wirkung.");
    }
}
