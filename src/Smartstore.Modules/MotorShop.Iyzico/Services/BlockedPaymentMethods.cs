using System.Threading;
using Microsoft.EntityFrameworkCore;
using Smartstore.Utilities;
using Smartstore.Core.Checkout.Payment;
using Smartstore.Core.Configuration;
using Smartstore.Core.Data;

namespace MotorShop.Iyzico.Services;

/// <summary>
/// Payment methods MOTOR must never offer. "Payments.Manual" (Smartstore OfflinePayment, "Credit card (manual)")
/// stores card numbers in the shop database, which is not allowed: cards are only entered on the iyzico page.
/// The upstream module stays untouched; the method is hidden by <see cref="BlockedPaymentMethodFilter"/> and removed
/// from the active payment methods on install (<see cref="Module"/>) and for existing databases (migration seeder).
/// </summary>
public static class BlockedPaymentMethods
{
    public const string ManualCreditCard = "Payments.Manual";

    public static readonly IReadOnlySet<string> SystemNames
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ManualCreditCard };

    public static bool IsBlocked(string systemName)
        => systemName != null && SystemNames.Contains(systemName);

    /// <summary>
    /// Removes blocked methods from PaymentSettings.ActivePaymentMethodSystemNames for all stores. Idempotent.
    /// </summary>
    /// <returns>Number of setting rows changed.</returns>
    public static async Task<int> DeactivateAsync(SmartDbContext db, CancellationToken cancelToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var name = TypeHelper.NameOf<PaymentSettings>(x => x.ActivePaymentMethodSystemNames, true);
        var rows = await db.Settings
            .Where(x => x.Name == name)
            .ToListAsync(cancelToken);

        var changed = 0;
        foreach (var row in rows)
        {
            var active = row.Value.Convert<List<string>>() ?? [];
            if (active.RemoveAll(IsBlocked) > 0)
            {
                row.Value = active.Convert<string>() ?? string.Empty;
                changed++;
            }
        }

        if (changed > 0)
        {
            await db.SaveChangesAsync(cancelToken);
        }

        return changed;
    }
}

/// <summary>
/// Hides blocked payment methods in checkout, even if an admin activates them again.
/// Must never throw: Smartstore treats a failing filter as "not excluded".
/// </summary>
public class BlockedPaymentMethodFilter : IPaymentMethodFilter
{
    public Task<bool> IsExcludedAsync(PaymentFilterRequest request)
        => Task.FromResult(BlockedPaymentMethods.IsBlocked(request?.PaymentProvider?.Metadata?.SystemName));
}
