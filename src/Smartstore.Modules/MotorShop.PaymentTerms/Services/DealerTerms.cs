using System.Globalization;
using MotorShop.PaymentTerms.Configuration;

namespace MotorShop.PaymentTerms.Services;

/// <summary>
/// A payment term a dealer can choose: cash within <see cref="Days"/> days, or deferred by <see cref="Days"/> days.
/// </summary>
public readonly record struct DealerTerm(string Value, int Days, bool IsCash);

public static class DealerTerms
{
    /// <summary>
    /// Terms offered by <paramref name="settings"/>: cash first, then the deferred terms in ascending order.
    /// Invalid, duplicate or out of range (1-365) values are ignored.
    /// </summary>
    public static IReadOnlyList<DealerTerm> GetTerms(PaymentTermsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var result = new List<DealerTerm>();
        if (settings.CashTermDays is >= 0 and <= 365)
        {
            result.Add(new(PaymentTermsDefaults.CashTermValue, settings.CashTermDays, true));
        }

        var days = settings.TermDays.EmptyNull()
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0)
            .Where(x => x is >= 1 and <= 365)
            .Distinct()
            .Order();

        result.AddRange(days.Select(x => new DealerTerm(x.ToString(CultureInfo.InvariantCulture), x, false)));
        return result;
    }

    public static DealerTerm? Find(PaymentTermsSettings settings, string value)
    {
        if (value.IsEmpty())
        {
            return null;
        }

        var term = GetTerms(settings).FirstOrDefault(x => x.Value.EqualsNoCase(value.Trim()));
        return term.Value == null ? null : term;
    }

    /// <summary>
    /// Display text, e.g. "Peşin (10 gün içinde ödeme)" or "60 gün vade".
    /// </summary>
    public static string GetLabel(DealerTerm term, Localizer T)
        => term.IsCash
            ? T("Plugins.MotorShop.PaymentTerms.Term.Cash", term.Days).ToString()
            : T("Plugins.MotorShop.PaymentTerms.Term.Deferred", term.Days).ToString();
}
