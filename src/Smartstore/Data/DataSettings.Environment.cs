namespace Smartstore.Data;

/// <summary>
/// MOTOR: lets the database connection come from the process environment
/// (e.g. the server's .env file) instead of the tenant's Settings.txt,
/// so the password never lands on the persistent App_Data volume.
/// </summary>
public partial class DataSettings
{
    /// <summary>
    /// Environment variable holding the full ADO.NET connection string.
    /// When set, it always wins over the connection string in Settings.txt.
    /// </summary>
    public const string ConnectionStringEnvVar = "MOTOR_DB_CONNECTION_STRING";

    /// <summary>
    /// Optional environment variable naming the data provider (e.g. "postgresql").
    /// When set, it must match the provider an existing installation was created with.
    /// </summary>
    public const string DataProviderEnvVar = "MOTOR_DB_PROVIDER";

    /// <summary>
    /// Written to Settings.txt instead of the real connection string while the
    /// environment variable is in use.
    /// </summary>
    internal const string EnvironmentPlaceholder = "env:" + ConnectionStringEnvVar;

    // Test seam
    internal static Func<string, string> EnvironmentReader { get; set; } = Environment.GetEnvironmentVariable;

    /// <summary>
    /// The connection string from <see cref="ConnectionStringEnvVar"/>, or <c>null</c>.
    /// </summary>
    public static string EnvironmentConnectionString
        => EnvironmentReader(ConnectionStringEnvVar)?.Trim().NullEmpty();

    /// <summary>
    /// The data provider from <see cref="DataProviderEnvVar"/>, or <c>null</c>.
    /// </summary>
    public static string EnvironmentDataProvider
        => EnvironmentReader(DataProviderEnvVar)?.Trim().NullEmpty();

    public static bool UsesEnvironmentConnectionString
        => EnvironmentConnectionString != null;

    /// <summary>
    /// Picks the effective connection string. Fails closed when Settings.txt says the
    /// connection string lives in the environment but the variable is missing, so a
    /// configured server never falls back to the public installer.
    /// </summary>
    internal static string ResolveConnectionString(string fromFile, string fromEnvironment)
    {
        if (fromEnvironment.HasValue())
        {
            return fromEnvironment;
        }

        if (fromFile == EnvironmentPlaceholder)
        {
            throw new InvalidOperationException(
                $"The database connection string is configured to come from the '{ConnectionStringEnvVar}' environment variable, but the variable is not set.");
        }

        return fromFile;
    }

    /// <summary>
    /// The value persisted to Settings.txt: never the secret while the environment variable is in use.
    /// </summary>
    internal static string GetPersistableConnectionString(string connectionString, string fromEnvironment)
        => fromEnvironment.HasValue() ? EnvironmentPlaceholder : connectionString;

    /// <summary>
    /// Guards against pointing an installation at a database of another provider.
    /// </summary>
    internal static void EnsureProviderMatches(string fromFile, string fromEnvironment)
    {
        if (fromEnvironment.HasValue() && fromFile.HasValue() && !fromFile.EqualsNoCase(fromEnvironment))
        {
            throw new InvalidOperationException(
                $"'{DataProviderEnvVar}' is '{fromEnvironment}', but this installation uses '{fromFile}'.");
        }
    }
}
