using System.Globalization;

namespace MotorShop.AdminPanel.Services;

/// <summary>
/// Parses amounts typed in Turkish notation ("1.250,50") as well as plain invariant input ("1250.50").
/// </summary>
public static class AmountParser
{
    public const int MaxDecimals = 2;
    public const decimal MaxAmount = 100_000_000m;

    /// <summary>
    /// Rules: a comma is always the decimal separator and dots are thousand separators.
    /// Without a comma, dots followed by exactly three digits are thousand separators ("9.999" = 9999),
    /// a single dot followed by any other number of digits is a decimal point ("12.5").
    /// </summary>
    public static bool TryParse(string input, out decimal amount, out string error)
        => TryParse(input, MaxDecimals, out amount, out error);

    public static bool TryParse(string input, int maxDecimals, out decimal amount, out string error)
    {
        amount = 0;
        error = null;

        var s = input?.Trim().Replace(" ", string.Empty).Replace(" ", string.Empty).Replace("₺", string.Empty).Replace("TL", string.Empty, StringComparison.OrdinalIgnoreCase);
        if (s.IsEmpty())
        {
            error = "Empty";
            return false;
        }

        if (s.StartsWith('-'))
        {
            error = "Negative";
            return false;
        }

        if (s.Any(c => !char.IsAsciiDigit(c) && c != '.' && c != ','))
        {
            error = "Invalid";
            return false;
        }

        string normalized;
        var commas = s.Count(c => c == ',');
        if (commas > 1)
        {
            error = "Invalid";
            return false;
        }

        if (commas == 1)
        {
            var parts = s.Split(',');
            if (!ValidThousands(parts[0]))
            {
                error = "Invalid";
                return false;
            }

            normalized = parts[0].Replace(".", string.Empty) + "." + parts[1];
        }
        else
        {
            var groups = s.Split('.');
            if (groups.Length == 1)
            {
                normalized = s;
            }
            else if (groups.Length == 2 && groups[1].Length != 3)
            {
                normalized = s;
            }
            else if (ValidThousands(s))
            {
                normalized = s.Replace(".", string.Empty);
            }
            else
            {
                error = "Invalid";
                return false;
            }
        }

        if (normalized.EndsWith('.') || normalized.StartsWith('.')
            || !decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount))
        {
            error = "Invalid";
            return false;
        }

        var dot = normalized.IndexOf('.');
        if (dot >= 0 && normalized.Length - dot - 1 > maxDecimals)
        {
            error = "TooManyDecimals";
            return false;
        }

        if (amount > MaxAmount)
        {
            error = "TooLarge";
            return false;
        }

        return true;
    }

    /// <summary>
    /// "1.250.000" style: first group 1-3 digits, every following group exactly 3 digits. No dot is valid too.
    /// </summary>
    private static bool ValidThousands(string integerPart)
    {
        if (integerPart.Length == 0)
        {
            return false;
        }

        var groups = integerPart.Split('.');
        if (groups.Length == 1)
        {
            return true;
        }

        return groups[0].Length is >= 1 and <= 3 && groups.Skip(1).All(g => g.Length == 3);
    }

    /// <summary>
    /// Percentage input like "10", "-7,5" or "12.5". Range -90..1000.
    /// </summary>
    public static bool TryParsePercent(string input, out decimal percent)
    {
        percent = 0;
        var s = input?.Trim().Replace("%", string.Empty).Replace(',', '.');
        if (s.IsEmpty() || !decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out percent))
        {
            return false;
        }

        return percent is >= -90m and <= 1000m && percent != 0;
    }

    /// <summary>
    /// Signed fixed amount for bulk changes, e.g. "-50" or "+125,50".
    /// </summary>
    public static bool TryParseSigned(string input, out decimal amount)
    {
        amount = 0;
        var s = input?.Trim() ?? string.Empty;
        var negative = s.StartsWith('-');
        if (negative || s.StartsWith('+'))
        {
            s = s[1..];
        }

        if (!TryParse(s, out amount, out _) || amount == 0)
        {
            return false;
        }

        if (negative)
        {
            amount = -amount;
        }

        return true;
    }

    public static decimal RoundPrice(decimal value)
        => Math.Round(value, MaxDecimals, MidpointRounding.AwayFromZero);
}
