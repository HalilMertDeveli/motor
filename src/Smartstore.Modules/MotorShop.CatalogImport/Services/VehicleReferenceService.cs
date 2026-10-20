using Microsoft.EntityFrameworkCore;
using MotorShop.CatalogImport.Domain;
using MotorShop.CatalogImport.Import;
using MotorShop.Fitment;
using MotorShop.Fitment.Domain;
using Smartstore.Core.Data;

namespace MotorShop.CatalogImport.Services;

public record VehicleAuditRow(MotorVehicle Vehicle, int ProductCount, CatalogVehicleReference Reference);

public record VehiclePublishResult(int Created, int Linked, int Skipped);

/// <summary>
/// Moves verified motorcycle reference records into MotorShop.Fitment's selector, and audits the selector's
/// existing motorcycles against them.
/// </summary>
public class VehicleReferenceService
{
    private readonly SmartDbContext _db;
    private readonly FitmentMatcher _matcher;
    private readonly CatalogPublicationService _publication;

    public VehicleReferenceService(SmartDbContext db, FitmentMatcher matcher, CatalogPublicationService publication)
    {
        _db = db;
        _matcher = matcher;
        _publication = publication;
    }

    /// <summary>
    /// The model name shown in the selector: model plus variant, so variants stay distinct choices.
    /// </summary>
    public static string SelectorModel(CatalogVehicleReference r)
        => string.IsNullOrWhiteSpace(r.Variant) ? r.Model : $"{r.Model} {r.Variant}";

    /// <summary>
    /// Selector years: the stated range where an official source gives it, otherwise the span of explicitly
    /// verified model years. Null if neither is known.
    /// </summary>
    public static (int From, int To)? SelectorYears(CatalogVehicleReference r)
    {
        var verified = (r.VerifiedYears ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => int.TryParse(x, out var y) ? y : (int?)null)
            .Where(x => x.HasValue)
            .Select(x => x.Value)
            .ToList();

        var from = r.YearFrom ?? (verified.Count > 0 ? verified.Min() : null);
        var to = r.YearTo ?? (verified.Count > 0 ? verified.Max() : from);

        return from.HasValue && to.HasValue && to >= from ? (from.Value, to.Value) : null;
    }

    /// <summary>
    /// Markets whose motorcycles belong in a Turkish selector. Records for other markets (e.g. India-only models
    /// or engine versions) stay reference data; their parts often do not interchange. A record without a market
    /// is not known to be sold here, so it stays out too.
    /// </summary>
    public static readonly IReadOnlySet<string> SelectorMarkets = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "TR", "EU", "GLOBAL" };

    public static bool IsSelectorMarket(CatalogVehicleReference r)
        => !string.IsNullOrWhiteSpace(r.Market) && SelectorMarkets.Contains(r.Market.Trim());

    public static bool IsPublishable(CatalogVehicleReference r)
        => r.Status == VehicleReferenceStatus.Verified && r.EngineCc.HasValue && SelectorYears(r).HasValue && IsSelectorMarket(r);

    /// <summary>
    /// Creates a MotorVehicle for each publishable reference that has none. An existing motorcycle with the same
    /// make, model and years is linked instead of duplicated. Idempotent.
    /// </summary>
    public async Task<VehiclePublishResult> PublishVerifiedAsync(IEnumerable<int> referenceIds = null, CancellationToken cancelToken = default)
    {
        var query = _db.CatalogVehicleReferences().Where(x => x.Status == VehicleReferenceStatus.Verified && x.MotorVehicleId == null);
        if (referenceIds != null)
        {
            var ids = referenceIds.ToList();
            query = query.Where(x => ids.Contains(x.Id));
        }

        var references = await query.ToListAsync(cancelToken);
        var vehicles = await _db.MotorVehicles().ToListAsync(cancelToken);
        int created = 0, linked = 0, skipped = 0;

        foreach (var r in references)
        {
            if (!IsPublishable(r))
            {
                skipped++;
                continue;
            }

            var (from, to) = SelectorYears(r).Value;
            var model = SelectorModel(r);
            var makeKey = CatalogNormalizer.MakeKey(r.Make);
            var modelKey = CatalogNormalizer.ModelKey(model);

            var vehicle = vehicles.FirstOrDefault(x =>
                CatalogNormalizer.MakeKey(x.Make) == makeKey
                && CatalogNormalizer.ModelKey(x.Model) == modelKey
                && Math.Min(x.YearFrom, x.YearTo) == from
                && Math.Max(x.YearFrom, x.YearTo) == to);

            if (vehicle == null)
            {
                vehicle = new MotorVehicle
                {
                    Make = r.Make,
                    Model = model,
                    YearFrom = from,
                    YearTo = to,
                    // Truncated, as manufacturers name engines (124.9 cc -> "124").
                    EngineCc = (int)decimal.Truncate(r.EngineCc.Value)
                };
                _db.MotorVehicles().Add(vehicle);
                vehicles.Add(vehicle);
                await _db.SaveChangesAsync(cancelToken);
                created++;
            }
            else
            {
                linked++;
            }

            r.MotorVehicleId = vehicle.Id;
            r.HasPendingChanges = false;
        }

        await _db.SaveChangesAsync(cancelToken);

        if (created + linked > 0)
        {
            await RematchAsync(cancelToken);
        }

        return new VehiclePublishResult(created, linked, skipped);
    }

    /// <summary>
    /// Updates a published motorcycle from its changed reference record (years, displacement, name).
    /// Product mappings stay: they belong to the vehicle row.
    /// </summary>
    public async Task<bool> ResyncAsync(int referenceId, CancellationToken cancelToken = default)
    {
        var r = await _db.CatalogVehicleReferences().FirstOrDefaultAsync(x => x.Id == referenceId, cancelToken);
        if (r?.MotorVehicleId == null || !IsPublishable(r))
        {
            return false;
        }

        var vehicle = await _db.MotorVehicles().FirstOrDefaultAsync(x => x.Id == r.MotorVehicleId, cancelToken);
        if (vehicle == null)
        {
            return false;
        }

        var (from, to) = SelectorYears(r).Value;
        vehicle.Make = r.Make;
        vehicle.Model = SelectorModel(r);
        vehicle.YearFrom = from;
        vehicle.YearTo = to;
        vehicle.EngineCc = (int)decimal.Truncate(r.EngineCc.Value);
        r.HasPendingChanges = false;

        await _db.SaveChangesAsync(cancelToken);
        await RematchAsync(cancelToken);
        return true;
    }

    /// <summary>
    /// Re-matches every part against the selector and brings product mappings and readiness in line,
    /// so a mapping never outlives the match that justified it.
    /// </summary>
    public async Task<FitmentMatchResult> RematchAsync(CancellationToken cancelToken = default)
    {
        var result = await _matcher.MatchPartsAsync(null, cancelToken);
        if (result.AffectedPartIds.Count > 0)
        {
            await _publication.RefreshAsync(result.AffectedPartIds, cancelToken);
        }

        return result;
    }

    /// <summary>
    /// Every selector motorcycle with its product count and the verified reference behind it (null = unverified).
    /// </summary>
    public async Task<List<VehicleAuditRow>> GetAuditAsync(CancellationToken cancelToken = default)
    {
        var vehicles = await _db.MotorVehicles().AsNoTracking()
            .OrderBy(x => x.Make).ThenBy(x => x.Model).ThenBy(x => x.YearFrom)
            .ToListAsync(cancelToken);

        var counts = await _db.ProductMotorVehicles().AsNoTracking()
            .GroupBy(x => x.MotorVehicleId)
            .Select(x => new { x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancelToken);

        var references = (await _db.CatalogVehicleReferences().AsNoTracking()
            .Where(x => x.MotorVehicleId != null && x.Status == VehicleReferenceStatus.Verified)
            .ToListAsync(cancelToken))
            .GroupBy(x => x.MotorVehicleId.Value)
            .ToDictionary(x => x.Key, x => x.First());

        return [.. vehicles.Select(v => new VehicleAuditRow(
            v,
            counts.TryGetValue(v.Id, out var c) ? c : 0,
            references.TryGetValue(v.Id, out var r) ? r : null))];
    }

    /// <summary>
    /// Removes selector motorcycles that have no verified reference and no product mapping (e.g. the approximate
    /// starter list). Motorcycles with mappings are always kept: the mapping may be genuine merchant work.
    /// </summary>
    public async Task<int> RetireUnverifiedAsync(CancellationToken cancelToken = default)
    {
        var verifiedIds = _db.CatalogVehicleReferences()
            .Where(x => x.MotorVehicleId != null && x.Status == VehicleReferenceStatus.Verified)
            .Select(x => x.MotorVehicleId.Value);

        var mappedIds = _db.ProductMotorVehicles().Select(x => x.MotorVehicleId);

        var toRemove = await _db.MotorVehicles()
            .Where(x => !verifiedIds.Contains(x.Id) && !mappedIds.Contains(x.Id))
            .ToListAsync(cancelToken);

        // Unverified links of non-verified references are cleared first (FK is SET NULL in the database anyway).
        var removeIds = toRemove.Select(x => x.Id).ToList();
        var refs = await _db.CatalogVehicleReferences().Where(x => x.MotorVehicleId != null && removeIds.Contains(x.MotorVehicleId.Value)).ToListAsync(cancelToken);
        refs.ForEach(x => x.MotorVehicleId = null);

        _db.MotorVehicles().RemoveRange(toRemove);
        await _db.SaveChangesAsync(cancelToken);
        return toRemove.Count;
    }
}
