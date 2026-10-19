using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Smartstore.Scheduling;

namespace MotorShop.Security.HealthChecks;

/// <summary>
/// Degraded (still HTTP 200) while the task scheduler is not polling. The scheduler is activated by the
/// first request after start-up, so a fresh process reports degraded until that request has run.
/// A store that stays degraded runs no background jobs (mails, imports, exports, cleanup).
/// </summary>
internal sealed class TaskSchedulerHealthCheck : IHealthCheck
{
    public const string Name = "task-scheduler";

    private readonly ITaskScheduler _taskScheduler;

    public TaskSchedulerHealthCheck(ITaskScheduler taskScheduler)
    {
        _taskScheduler = taskScheduler;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancelToken = default)
    {
        var result = _taskScheduler.IsActive
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Degraded("Task scheduler is not active.");

        return Task.FromResult(result);
    }
}
