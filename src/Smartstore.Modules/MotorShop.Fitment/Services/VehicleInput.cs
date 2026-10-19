using MotorShop.Fitment.Domain;

namespace MotorShop.Fitment.Services;

/// <summary>
/// Validates and normalizes motorcycles entered in the admin (single form and bulk import),
/// so that every stored range satisfies YearFrom &lt;= YearTo.
/// </summary>
public static class VehicleInput
{
    public const int MinYear = 1950;
    public const int MaxYear = 2100;

    /// <summary>
    /// Creates a vehicle from admin input. Make and model are trimmed and required,
    /// both years must lie within [<see cref="MinYear"/>, <see cref="MaxYear"/>],
    /// and a reversed range (to &lt; from) is swapped.
    /// </summary>
    public static bool TryCreate(string make, string model, int yearFrom, int yearTo, int? engineCc, out MotorVehicle vehicle)
    {
        vehicle = null;
        make = make?.Trim();
        model = model?.Trim();

        if (string.IsNullOrEmpty(make) || make.Length > 100
            || string.IsNullOrEmpty(model) || model.Length > 200
            || !IsValidYear(yearFrom)
            || !IsValidYear(yearTo))
        {
            return false;
        }

        vehicle = new MotorVehicle
        {
            Make = make,
            Model = model,
            YearFrom = Math.Min(yearFrom, yearTo),
            YearTo = Math.Max(yearFrom, yearTo),
            EngineCc = engineCc
        };

        return true;
    }

    /// <summary>
    /// Parses one bulk import line: Make;Model;YearFrom;YearTo;EngineCc (EngineCc optional, ';' or tab separated).
    /// </summary>
    public static bool TryParseLine(string line, out MotorVehicle vehicle)
    {
        vehicle = null;

        var parts = (line ?? string.Empty).Split([';', '\t'], StringSplitOptions.TrimEntries);
        if (parts.Length < 4
            || !int.TryParse(parts[2], out var from)
            || !int.TryParse(parts[3], out var to))
        {
            return false;
        }

        int? cc = parts.Length > 4 && int.TryParse(parts[4], out var parsedCc) ? parsedCc : null;

        return TryCreate(parts[0], parts[1], from, to, cc, out vehicle);
    }

    private static bool IsValidYear(int year)
        => year >= MinYear && year <= MaxYear;
}
