using Microsoft.EntityFrameworkCore;
using MotorShop.CatalogImport.Domain;
using MotorShop.CatalogImport.Import;
using MotorShop.Fitment;
using Smartstore.Core.Data;

namespace MotorShop.CatalogImport.Services;

/// <summary>
/// Proposes links between a part manufacturer's fitment statements and motorcycles.
/// Rules: only motorcycles backed by a verified reference record are candidates; make and model (including variant)
/// must be equal after normalization; displacement is never used. A match is <see cref="FitmentMatchKind.Exact"/>
/// only when the motorcycle's whole year range lies inside the stated range. Matching never approves anything.
/// </summary>
public class FitmentMatcher
{
    private readonly SmartDbContext _db;

    public FitmentMatcher(SmartDbContext db)
    {
        _db = db;
    }

    public record VehicleCandidate(int Id, string MakeKey, string ModelKey, int FirstYear, int LastYear);

    /// <summary>
    /// Classifies how a motorcycle's year range relates to a fitment statement, or null if they do not overlap.
    /// </summary>
    /// <param name="sourceYear">Year the source was read. An open range ("2021-") covers only years up to it.</param>
    public static FitmentMatchKind? Classify(int fitFrom, int? fitTo, int vehicleFirst, int vehicleLast, int sourceYear)
    {
        var upper = fitTo ?? sourceYear;

        if (vehicleFirst >= fitFrom && vehicleLast <= upper)
        {
            return FitmentMatchKind.Exact;
        }

        var overlaps = vehicleFirst <= (fitTo ?? int.MaxValue) && vehicleLast >= fitFrom;
        return overlaps ? FitmentMatchKind.Partial : null;
    }

    public static string FitmentModelKey(string model, string variant)
        => CatalogNormalizer.ModelKey($"{model} {variant}");

    /// <summary>
    /// Motorcycles that are linked to a verified reference record. Seed or hand-entered motorcycles without
    /// verified data never receive imported fitment.
    /// </summary>
    public async Task<List<VehicleCandidate>> GetVerifiedVehiclesAsync(CancellationToken cancelToken = default)
    {
        var verifiedIds = _db.CatalogVehicleReferences()
            .Where(x => x.Status == VehicleReferenceStatus.Verified && x.MotorVehicleId != null)
            .Select(x => x.MotorVehicleId.Value);

        var vehicles = await _db.MotorVehicles()
            .AsNoTracking()
            .Where(x => verifiedIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Make, x.Model, x.YearFrom, x.YearTo })
            .ToListAsync(cancelToken);

        return [.. vehicles.Select(x => new VehicleCandidate(
            x.Id,
            CatalogNormalizer.MakeKey(x.Make),
            CatalogNormalizer.ModelKey(x.Model),
            Math.Min(x.YearFrom, x.YearTo),
            Math.Max(x.YearFrom, x.YearTo)))];
    }

    /// <summary>
    /// Adds missing proposals and removes proposals that no longer hold. Reviewed matches (approved or rejected) are kept.
    /// </summary>
    /// <param name="partIds">Parts to match, or null for all.</param>
    /// <returns>Number of new proposals.</returns>
    public async Task<int> MatchPartsAsync(IEnumerable<int> partIds, CancellationToken cancelToken = default)
    {
        var ids = partIds?.ToList();
        var vehicles = await GetVerifiedVehiclesAsync(cancelToken);
        var byMakeModel = vehicles.ToLookup(x => (x.MakeKey, x.ModelKey));

        var fitmentQuery = _db.CatalogPartFitments().AsQueryable();
        if (ids != null)
        {
            fitmentQuery = fitmentQuery.Where(x => ids.Contains(x.PartId));
        }

        var fitments = await fitmentQuery.AsNoTracking().ToListAsync(cancelToken);
        if (fitments.Count == 0)
        {
            return 0;
        }

        var fitmentPartIds = fitments.Select(x => x.PartId).Distinct().ToList();
        var sourceYears = await _db.CatalogParts()
            .AsNoTracking()
            .Where(x => fitmentPartIds.Contains(x.Id))
            .Select(x => new { x.Id, x.AccessedOn, x.FirstSeenOnUtc })
            .ToDictionaryAsync(x => x.Id, x => (x.AccessedOn ?? x.FirstSeenOnUtc).Year, cancelToken);

        var fitmentIds = fitments.Select(x => x.Id).ToList();
        var matches = (await _db.CatalogFitmentMatches()
            .Where(x => fitmentIds.Contains(x.FitmentId))
            .ToListAsync(cancelToken))
            .ToLookup(x => x.FitmentId);

        var added = 0;

        foreach (var f in fitments)
        {
            var sourceYear = sourceYears.TryGetValue(f.PartId, out var y) ? y : DateTime.UtcNow.Year;
            var candidates = byMakeModel[(CatalogNormalizer.MakeKey(f.Make), FitmentModelKey(f.Model, f.Variant))]
                .Select(v => (Vehicle: v, Kind: Classify(f.YearFrom, f.YearTo, v.FirstYear, v.LastYear, sourceYear)))
                .Where(x => x.Kind.HasValue)
                .ToDictionary(x => x.Vehicle.Id, x => x.Kind.Value);

            var existing = matches[f.Id].ToDictionary(x => x.MotorVehicleId);

            foreach (var (vehicleId, kind) in candidates)
            {
                if (existing.TryGetValue(vehicleId, out var match))
                {
                    if (match.Kind != kind)
                    {
                        match.Kind = kind;
                        if (kind != FitmentMatchKind.Exact && match.Status == FitmentMatchStatus.Approved)
                        {
                            // The vehicle's years changed so the statement no longer covers it; needs a new review.
                            match.Status = FitmentMatchStatus.Proposed;
                        }
                    }
                }
                else
                {
                    _db.CatalogFitmentMatches().Add(new CatalogFitmentMatch
                    {
                        FitmentId = f.Id,
                        PartId = f.PartId,
                        MotorVehicleId = vehicleId,
                        Kind = kind,
                        Status = FitmentMatchStatus.Proposed
                    });
                    added++;
                }
            }

            foreach (var stale in existing.Values.Where(x => !candidates.ContainsKey(x.MotorVehicleId) && x.Status == FitmentMatchStatus.Proposed))
            {
                _db.CatalogFitmentMatches().Remove(stale);
            }
        }

        await _db.SaveChangesAsync(cancelToken);
        return added;
    }
}
