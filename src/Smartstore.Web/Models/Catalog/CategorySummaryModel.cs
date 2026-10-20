using Smartstore.Core.Localization;
using Smartstore.Web.Models.Media;

namespace Smartstore.Web.Models.Catalog;

public partial class CategorySummaryModel : EntityModelBase
{
    public LocalizedValue<string> Name { get; set; }
    public string Url { get; set; }
    public ImageModel Image { get; set; } = new ImageModel();

    /// <summary>
    /// MOTOR: the category has no picture of its own; <see cref="Image"/> is the main picture of one of its products.
    /// </summary>
    public bool IsProductImage { get; set; }

    // TODO: Badges
}