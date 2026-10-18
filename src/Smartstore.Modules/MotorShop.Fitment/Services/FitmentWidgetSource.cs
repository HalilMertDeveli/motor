using MotorShop.Fitment.Components;
using Smartstore.Core.Widgets;

namespace MotorShop.Fitment.Services;

/// <summary>
/// Renders the motorcycle picker on the home page and on category/brand pages.
/// </summary>
internal class FitmentWidgetSource : IWidgetSource
{
    private static readonly HashSet<string> _pickerZones = new(StringComparer.OrdinalIgnoreCase)
    {
        "home_page_top",
        "categorydetails_top",
        "manufacturerdetails_top"
    };

    public int Order => 0;

    public Task<IEnumerable<Widget>> GetWidgetsAsync(IWidgetZone zone, bool isPublicArea, object model = null)
    {
        if (isPublicArea && _pickerZones.Contains(zone.Name))
        {
            return Task.FromResult<IEnumerable<Widget>>([new ComponentWidget<VehiclePickerViewComponent>()]);
        }

        return Task.FromResult<IEnumerable<Widget>>(null);
    }
}
