using System.Threading;
using MotorShop.Iyzico.Services;
using Smartstore.Scheduling;

namespace MotorShop.Iyzico.Tasks;

/// <summary>
/// Re-checks open iyzico transactions with the API, for payments whose callback and webhook never arrived.
/// </summary>
public class IyzicoReconciliationTask : ITask
{
    private readonly IyzicoPaymentService _paymentService;

    public IyzicoReconciliationTask(IyzicoPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public async Task Run(TaskExecutionContext ctx, CancellationToken cancelToken = default)
    {
        await _paymentService.ReconcileAsync(cancelToken: cancelToken);
    }
}
