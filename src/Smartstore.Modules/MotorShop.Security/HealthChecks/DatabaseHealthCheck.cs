using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Smartstore.Core.Data;

namespace MotorShop.Security.HealthChecks;

/// <summary>
/// Ready when the application database accepts connections.
/// The result description is generic on purpose: it never contains the connection string or server name.
/// </summary>
internal sealed class DatabaseHealthCheck : IHealthCheck
{
    public const string Name = "database";

    private readonly IDbContextFactory<SmartDbContext> _dbContextFactory;

    public DatabaseHealthCheck(IDbContextFactory<SmartDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancelToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancelToken);

        return await db.Database.CanConnectAsync(cancelToken)
            ? HealthCheckResult.Healthy()
            : new HealthCheckResult(context.Registration.FailureStatus, "Database is not reachable.");
    }
}
