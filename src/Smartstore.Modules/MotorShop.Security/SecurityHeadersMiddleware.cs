using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace MotorShop.Security;

/// <summary>
/// Adds security response headers, removes the platform version header
/// and answers liveness probes on <see cref="HealthPath"/>.
/// </summary>
internal sealed class SecurityHeadersMiddleware
{
    /// <summary>
    /// Path of the liveness endpoint used by load balancers and uptime monitors.
    /// </summary>
    public const string HealthPath = "/healthz";

    const string ReferrerPolicy = "strict-origin-when-cross-origin";
    const string PermissionsPolicy = "camera=(), microphone=(), geolocation=()";

    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task Invoke(HttpContext context)
    {
        context.Response.OnStarting(ApplyHeaders, context);

        var request = context.Request;
        if (request.Path.Equals(HealthPath, StringComparison.OrdinalIgnoreCase) && (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)))
        {
            // Liveness only: the process is up and serving requests. No database round trip.
            var response = context.Response;
            response.StatusCode = StatusCodes.Status200OK;
            response.ContentType = "text/plain";
            response.Headers.CacheControl = "no-store";

            return HttpMethods.IsHead(request.Method)
                ? Task.CompletedTask
                : response.WriteAsync("Healthy");
        }

        return _next(context);
    }

    private static Task ApplyHeaders(object state)
    {
        var headers = ((HttpContext)state).Response.Headers;

        // Smartstore writes "X-Powered-By: Smartstore {version}", which only helps attackers.
        headers.Remove(HeaderNames.XPoweredBy);

        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = ReferrerPolicy;
        headers["Permissions-Policy"] = PermissionsPolicy;

        return Task.CompletedTask;
    }
}
