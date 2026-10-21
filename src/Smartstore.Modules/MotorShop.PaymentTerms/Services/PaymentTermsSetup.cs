using System.Threading;
using Microsoft.EntityFrameworkCore;
using Smartstore.Core.Checkout.Payment;
using Smartstore.Core.Checkout.Tax;
using Smartstore.Core.Configuration;
using Smartstore.Core.Data;
using Smartstore.Core.Identity;
using Smartstore.Utilities;

namespace MotorShop.PaymentTerms.Services;

/// <summary>
/// Install steps. Idempotent and additive: existing roles, descriptions and admin choices are kept.
/// </summary>
public static class PaymentTermsSetup
{
    /// <summary>
    /// Creates the "Bayi" (Dealer) customer role. Dealers see prices excluding KDV, as the catalog's dealer prices are.
    /// </summary>
    /// <returns><c>true</c> if the role was created.</returns>
    public static async Task<bool> EnsureDealerRoleAsync(SmartDbContext db, CancellationToken cancelToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (await db.CustomerRoles.AnyAsync(x => x.SystemName == PaymentTermsDefaults.DealerRoleSystemName, cancelToken))
        {
            return false;
        }

        db.CustomerRoles.Add(new CustomerRole
        {
            Name = PaymentTermsDefaults.DealerRoleName,
            SystemName = PaymentTermsDefaults.DealerRoleSystemName,
            Active = true,
            IsSystemRole = false,
            TaxDisplayType = (int)TaxDisplayType.ExcludingTax
        });

        await db.SaveChangesAsync(cancelToken);
        return true;
    }

    /// <summary>
    /// Activates card (iyzico), bank transfer, cash on delivery and dealer terms, and deactivates pay in store,
    /// in every PaymentSettings.ActivePaymentMethodSystemNames row. iyzico stays hidden in checkout until its keys are entered.
    /// </summary>
    public static async Task ConfigureActiveMethodsAsync(SmartDbContext db, CancellationToken cancelToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var name = TypeHelper.NameOf<PaymentSettings>(x => x.ActivePaymentMethodSystemNames, true);
        var rows = await db.Settings.Where(x => x.Name == name).ToListAsync(cancelToken);

        if (rows.Count == 0)
        {
            db.Settings.Add(new Setting { Name = name, StoreId = 0, Value = UpdateActiveMethods([]).Convert<string>() });
        }

        foreach (var row in rows)
        {
            var active = row.Value.Convert<List<string>>() ?? [];
            row.Value = UpdateActiveMethods(active).Convert<string>() ?? string.Empty;
        }

        await db.SaveChangesAsync(cancelToken);
    }

    public static List<string> UpdateActiveMethods(List<string> active)
    {
        var result = (active ?? [])
            .Where(x => !PaymentTermsDefaults.DeactivatedMethods.Any(d => d.EqualsNoCase(x)))
            .ToList();

        foreach (var method in PaymentTermsDefaults.ActivatedMethods)
        {
            if (!result.Any(x => x.EqualsNoCase(method)))
            {
                result.Add(method);
            }
        }

        return result;
    }

    /// <summary>
    /// Checkout descriptions of bank transfer (with bank account placeholders) and cash on delivery, only where none is set.
    /// </summary>
    public static async Task EnsureDescriptionsAsync(SmartDbContext db, CancellationToken cancelToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var descriptions = new Dictionary<string, string>
        {
            [PaymentTermsDefaults.PrepaymentSystemName] = PaymentTermsDefaults.PrepaymentDescription,
            [PaymentTermsDefaults.CashOnDeliverySystemName] = PaymentTermsDefaults.CashOnDeliveryDescription
        };

        var systemNames = descriptions.Keys.ToArray();
        var methods = await db.PaymentMethods
            .Where(x => systemNames.Contains(x.PaymentMethodSystemName))
            .ToListAsync(cancelToken);

        foreach (var (systemName, description) in descriptions)
        {
            var method = methods.FirstOrDefault(x => x.PaymentMethodSystemName.EqualsNoCase(systemName));
            if (method == null)
            {
                db.PaymentMethods.Add(new PaymentMethod { PaymentMethodSystemName = systemName, FullDescription = description });
            }
            else if (method.FullDescription.IsEmpty())
            {
                method.FullDescription = description;
            }
        }

        await db.SaveChangesAsync(cancelToken);
    }
}
