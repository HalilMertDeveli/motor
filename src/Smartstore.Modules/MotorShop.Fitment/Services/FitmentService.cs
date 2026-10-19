using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using MotorShop.Fitment.Domain;
using Smartstore.Core.Data;

namespace MotorShop.Fitment.Services;

/// <summary>
/// The motorcycle a customer selected in the picker.
/// </summary>
public record VehicleSelection(string Make, string Model, int? Year)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Make);

    public string DisplayName
        => string.Join(' ', new[] { Make, Model, Year?.ToString() }.Where(x => !string.IsNullOrWhiteSpace(x)));
}

public class FitmentService
{
    const string CookieName = ".MotorShop.Vehicle";

    /// <summary>
    /// Marks the cookie format "v2|Make|Model|Year" with each part URL-encoded separately.
    /// Cookies without it use the legacy format.
    /// </summary>
    const string CookieVersionPrefix = "v2|";

    private readonly SmartDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public FitmentService(SmartDbContext db, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
    }

    public Task<List<string>> GetMakesAsync()
    {
        return _db.MotorVehicles()
            .AsNoTracking()
            .Select(x => x.Make)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();
    }

    public Task<List<string>> GetModelsAsync(string make)
    {
        if (string.IsNullOrWhiteSpace(make))
        {
            return Task.FromResult(new List<string>());
        }

        return _db.MotorVehicles()
            .AsNoTracking()
            .Where(x => x.Make == make)
            .Select(x => x.Model)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();
    }

    public async Task<List<int>> GetYearsAsync(string make, string model)
    {
        if (string.IsNullOrWhiteSpace(make) || string.IsNullOrWhiteSpace(model))
        {
            return [];
        }

        var ranges = await _db.MotorVehicles()
            .AsNoTracking()
            .Where(x => x.Make == make && x.Model == model)
            .Select(x => new { x.YearFrom, x.YearTo })
            .ToListAsync();

        // Same rule as GetFittingProductIdsAsync: a range is [min(YearFrom, YearTo), max(YearFrom, YearTo)].
        return [.. ranges
            .SelectMany(x => Enumerable.Range(Math.Min(x.YearFrom, x.YearTo), Math.Abs(x.YearTo - x.YearFrom) + 1))
            .Distinct()
            .OrderByDescending(x => x)];
    }

    /// <summary>
    /// Gets IDs of all products that fit the selected motorcycle.
    /// </summary>
    public Task<int[]> GetFittingProductIdsAsync(VehicleSelection selection)
    {
        if (selection == null || selection.IsEmpty)
        {
            return Task.FromResult(Array.Empty<int>());
        }

        var vehicles = _db.MotorVehicles().AsNoTracking().Where(x => x.Make == selection.Make);

        if (!string.IsNullOrWhiteSpace(selection.Model))
        {
            vehicles = vehicles.Where(x => x.Model == selection.Model);
        }

        if (selection.Year.HasValue)
        {
            var year = selection.Year.Value;
            // Inclusive in either direction, so a reversed range (YearTo < YearFrom) still matches every year GetYearsAsync offers.
            vehicles = vehicles.Where(x =>
                (x.YearFrom <= year && x.YearTo >= year) ||
                (x.YearTo <= year && x.YearFrom >= year));
        }

        return _db.ProductMotorVehicles()
            .AsNoTracking()
            .Where(m => vehicles.Any(v => v.Id == m.MotorVehicleId))
            .Select(m => m.ProductId)
            .Distinct()
            .ToArrayAsync();
    }

    public Task<List<MotorVehicle>> GetVehiclesForProductAsync(int productId)
    {
        return _db.ProductMotorVehicles()
            .AsNoTracking()
            .Where(m => m.ProductId == productId)
            .Join(_db.MotorVehicles(), m => m.MotorVehicleId, v => v.Id, (m, v) => v)
            .OrderBy(v => v.Make)
            .ThenBy(v => v.Model)
            .ThenBy(v => v.YearFrom)
            .ToListAsync();
    }

    public VehicleSelection GetSelection()
    {
        var request = _httpContextAccessor.HttpContext?.Request;
        if (request == null || !request.Cookies.TryGetValue(CookieName, out var raw) || string.IsNullOrEmpty(raw))
        {
            return null;
        }

        string[] parts;
        if (raw.StartsWith(CookieVersionPrefix, StringComparison.Ordinal))
        {
            parts = raw[CookieVersionPrefix.Length..].Split('|');
            if (parts.Length != 3)
            {
                return null;
            }

            parts = [.. parts.Select(Uri.UnescapeDataString)];
        }
        else
        {
            // Legacy cookie: "Make|Model|Year" escaped as a whole. Still readable unless make or model contained '|'.
            parts = Uri.UnescapeDataString(raw).Split('|');
        }

        var make = parts.ElementAtOrDefault(0);
        var model = parts.ElementAtOrDefault(1);
        int? year = int.TryParse(parts.ElementAtOrDefault(2), NumberStyles.None, CultureInfo.InvariantCulture, out var y) ? y : null;

        var selection = new VehicleSelection(make, string.IsNullOrWhiteSpace(model) ? null : model, year);
        return selection.IsEmpty ? null : selection;
    }

    public void SaveSelection(VehicleSelection selection)
    {
        var response = _httpContextAccessor.HttpContext?.Response;
        if (response == null)
        {
            return;
        }

        if (selection == null || selection.IsEmpty)
        {
            response.Cookies.Delete(CookieName);
            return;
        }

        // Escape each part on its own, so a '|' inside make or model can never be taken for the separator.
        var value = CookieVersionPrefix + string.Join('|',
            Uri.EscapeDataString(selection.Make),
            Uri.EscapeDataString(selection.Model ?? string.Empty),
            selection.Year?.ToString(CultureInfo.InvariantCulture));

        response.Cookies.Append(CookieName, value, new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddDays(365),
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax
        });
    }
}
