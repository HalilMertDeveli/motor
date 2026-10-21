using Microsoft.AspNetCore.Mvc;
using Smartstore.Core.Security;
using Smartstore.Google.Auth.Models;
using Smartstore.Web.Components;

namespace Smartstore.Google.Auth.Components;

public class GoogleAuthViewComponent : SmartViewComponent
{
    private readonly GoogleOptions _googleOptions;

    public GoogleAuthViewComponent(IOptionsMonitor<GoogleOptions> googleOptions)
    {
        _googleOptions = googleOptions.Get(GoogleDefaults.AuthenticationScheme);
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (!_googleOptions.ClientId.HasValue() || !_googleOptions.ClientSecret.HasValue())
        {
            // Never render a button that cannot work. Tell administrators what is missing instead.
            if (await Services.Permissions.AuthorizeAsync(Permissions.Configuration.Authentication.Update))
            {
                return View("NotConfigured", Url.Action("Configure", "GoogleAuth", new { area = "Admin" }));
            }

            return Empty();
        }

        var returnUrl = HttpContext.Request.Query["returnUrl"].ToString();
        var model = new GoogleAuthButtonModel
        {
            Url = Url.Action("ExternalLogin", "Identity", new
            {
                provider = GoogleDefaults.AuthenticationScheme,
                returnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null
            })
        };

        return View(model);
    }
}
