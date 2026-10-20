using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Smartstore;

namespace MotorShop.Security;

/// <summary>
/// Adds security response headers and removes the platform version header.
/// </summary>
internal sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IOptionsMonitor<MotorSecurityOptions> _options;

    public SecurityHeadersMiddleware(RequestDelegate next, IOptionsMonitor<MotorSecurityOptions> options)
    {
        _next = next;
        _options = options;
    }

    public Task Invoke(HttpContext context)
    {
        context.Response.OnStarting(ApplyHeaders, context);
        return _next(context);
    }

    private Task ApplyHeaders(object state)
    {
        var headers = ((HttpContext)state).Response.Headers;
        var options = _options.CurrentValue;

        // Smartstore writes "X-Powered-By: Smartstore {version}", which only helps attackers.
        headers.Remove(HeaderNames.XPoweredBy);

        headers.XContentTypeOptions = "nosniff";

        if (options.ReferrerPolicy.HasValue())
        {
            headers["Referrer-Policy"] = options.ReferrerPolicy;
        }

        if (options.PermissionsPolicy.HasValue())
        {
            headers["Permissions-Policy"] = options.PermissionsPolicy;
        }

        if (options.ContentSecurityPolicyReportOnly.HasValue() && !headers.ContainsKey(HeaderNames.ContentSecurityPolicyReportOnly))
        {
            headers.ContentSecurityPolicyReportOnly = options.ContentSecurityPolicyReportOnly;
        }

        return Task.CompletedTask;
    }
}
