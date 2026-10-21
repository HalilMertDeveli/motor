namespace MotorShop.Security;

/// <summary>
/// Settings bound from the "MotorShop:Security" configuration section
/// (appsettings.json, Config/usersettings.json or environment variables like "MotorShop__Security__ReferrerPolicy").
/// </summary>
public class MotorSecurityOptions
{
    public const string SectionName = "MotorShop:Security";

    /// <summary>
    /// Value of the "Referrer-Policy" header. Empty to omit the header.
    /// </summary>
    public string ReferrerPolicy { get; set; } = "strict-origin-when-cross-origin";

    /// <summary>
    /// Value of the "Permissions-Policy" header. Empty to omit the header.
    /// Payment providers that use the Payment Request API need "payment" to stay allowed (it is not listed by default).
    /// </summary>
    public string PermissionsPolicy { get; set; } = "camera=(), microphone=(), geolocation=(), usb=(), magnetometer=(), gyroscope=(), accelerometer=()";

    /// <summary>
    /// Optional "Content-Security-Policy-Report-Only" value, used to trial a stricter policy
    /// without enforcing it. Not applied when Smartstore already sends a report-only policy
    /// (Smartstore:ContentSecurityPolicy:Report = true). The enforced policy stays under
    /// Smartstore:ContentSecurityPolicy (frame-ancestors, upgrade-insecure-requests).
    /// </summary>
    public string ContentSecurityPolicyReportOnly { get; set; }

    /// <summary>
    /// Path of the liveness endpoint. The process is up, no dependency is checked.
    /// </summary>
    public string LivenessPath { get; set; } = "/healthz";

    /// <summary>
    /// Path of the readiness endpoint. Checks the database connection and the task scheduler.
    /// </summary>
    public string ReadinessPath { get; set; } = "/healthz/ready";

    /// <summary>
    /// Time limit for a single readiness check, in seconds.
    /// </summary>
    public int ReadinessTimeoutSeconds { get; set; } = 5;
}
