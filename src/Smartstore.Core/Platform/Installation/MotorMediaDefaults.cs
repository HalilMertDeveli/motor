using Smartstore.Core.Content.Media;

namespace Smartstore.Core.Installation;

/// <summary>
/// MOTOR: picture sizes and quality for the MotorDark storefront.
/// Product cards are about 280 CSS px wide, i.e. about 560 device pixels on phones; Smartstore's default
/// card thumbnail (256 px) is stretched there and looks blurry. The product page shows its main picture at
/// up to about 560 CSS px, so 1024 px stays sharp on high-density screens.
/// Used by <see cref="InvariantSeedData"/> (fresh installs) and the MotorMediaSizes migration (existing databases).
/// </summary>
public static class MotorMediaDefaults
{
    public static readonly (string Name, int SmartstoreDefault, int Value)[] Settings =
    [
        ($"{nameof(MediaSettings)}.{nameof(MediaSettings.ProductThumbPictureSize)}", MediaSettings.ThumbnailSizeMd, MediaSettings.ThumbnailSizeLg),
        ($"{nameof(MediaSettings)}.{nameof(MediaSettings.ProductDetailsPictureSize)}", MediaSettings.ThumbnailSizeXl, MediaSettings.ThumbnailSizeXxl),
        ($"{nameof(MediaSettings)}.{nameof(MediaSettings.CategoryThumbPictureSize)}", MediaSettings.ThumbnailSizeMd, MediaSettings.ThumbnailSizeLg),
        ($"{nameof(MediaSettings)}.{nameof(MediaSettings.ManufacturerThumbPictureSize)}", MediaSettings.ThumbnailSizeMd, MediaSettings.ThumbnailSizeLg),
        ($"{nameof(MediaSettings)}.{nameof(MediaSettings.DefaultImageQuality)}", 85, 90)
    ];

    public static void Apply(MediaSettings settings)
    {
        Guard.NotNull(settings);

        settings.ProductThumbPictureSize = MediaSettings.ThumbnailSizeLg;
        settings.ProductDetailsPictureSize = MediaSettings.ThumbnailSizeXxl;
        settings.CategoryThumbPictureSize = MediaSettings.ThumbnailSizeLg;
        settings.ManufacturerThumbPictureSize = MediaSettings.ThumbnailSizeLg;
        settings.DefaultImageQuality = 90;
    }
}
