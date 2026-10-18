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

        return [.. ranges
            .SelectMany(x => Enumerable.Range(x.YearFrom, Math.Max(0, x.YearTo - x.YearFrom) + 1))
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
            vehicles = vehicles.Where(x => x.YearFrom <= year && x.YearTo >= year);
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

        var parts = Uri.UnescapeDataString(raw).Split('|');
        var make = parts.ElementAtOrDefault(0);
        var model = parts.ElementAtOrDefault(1);
        int? year = int.TryParse(parts.ElementAtOrDefault(2), out var y) ? y : null;

        var selection = new VehicleSelection(make, model, year);
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

        var value = Uri.EscapeDataString($"{selection.Make}|{selection.Model}|{selection.Year}");
        response.Cookies.Append(CookieName, value, new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddDays(365),
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax
        });
    }
}
