using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MotorShop.CatalogImport.Domain;
using MotorShop.CatalogImport.Import;
using Smartstore;
using Smartstore.Core.Data;

namespace MotorShop.CatalogImport.Services;

public record StageResult(CatalogImportBatch Batch, bool AlreadyProcessed);

/// <summary>
/// RETRIEVE → VALIDATE → NORMALIZE → DEDUPLICATE → STAGE for one feed, inside one transaction.
/// Idempotent: the same feed staged twice creates nothing new. Records missing from a feed are flagged, never deleted.
/// Reviewed records are never overwritten; their incoming changes wait in <see cref="CatalogPart.PendingChangesJson"/>.
/// </summary>
public partial class CatalogImportService
{
    public const int MaxContentLength = 5 * 1024 * 1024;
    const int MaxStoredIssues = 500;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly SmartDbContext _db;
    private readonly FitmentMatcher _matcher;
    private readonly CatalogPublicationService _publication;

    public CatalogImportService(SmartDbContext db, FitmentMatcher matcher, CatalogPublicationService publication)
    {
        _db = db;
        _matcher = matcher;
        _publication = publication;
    }

    public ILogger Logger { get; set; } = NullLogger.Instance;

    /// <summary>
    /// Clock used for timestamps and year bounds. Replaceable in tests.
    /// </summary>
    public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    public async Task<StageResult> StageAsync(
        int sourceId,
        CatalogImportKind kind,
        string fileName,
        string content,
        bool isFullSnapshot = false,
        bool force = false,
        int? retryOfBatchId = null,
        CancellationToken cancelToken = default)
    {
        Guard.NotNull(content);

        var source = await _db.CatalogSources().FirstOrDefaultAsync(x => x.Id == sourceId, cancelToken)
            ?? throw new ArgumentException($"Unknown catalog source {sourceId}.", nameof(sourceId));

        var hash = CatalogNormalizer.Sha256(content);
        if (!force)
        {
            var processed = await _db.CatalogImportBatches()
                .Where(x => x.SourceId == sourceId && x.Kind == kind && x.ContentHash == hash
                    && (x.Status == CatalogBatchStatus.Completed || x.Status == CatalogBatchStatus.CompletedWithErrors))
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(cancelToken);

            if (processed != null)
            {
                return new StageResult(processed, true);
            }
        }

        var now = UtcNow();
        var batch = new CatalogImportBatch
        {
            SourceId = sourceId,
            Kind = kind,
            FileName = fileName.Truncate(255),
            ContentHash = hash,
            RawContent = content,
            IsFullSnapshot = isFullSnapshot,
            Status = CatalogBatchStatus.Pending,
            StartedOnUtc = now,
            RetryOfBatchId = retryOfBatchId
        };

        _db.CatalogImportBatches().Add(batch);
        await _db.SaveChangesAsync(cancelToken);

        var issues = new List<ImportIssue>();
        var affectedPartIds = new HashSet<int>();
        string failure = null;

        if (content.Length > MaxContentLength)
        {
            failure = $"The file is larger than {MaxContentLength / 1024 / 1024} MB.";
        }
        else if (!source.AllowsStaging)
        {
            failure = $"Source permission is {source.PermissionStatus}; nothing is staged from it.";
        }
        else if (kind == CatalogImportKind.SupplierOffers && source.PermissionStatus != SourcePermissionStatus.Authorized)
        {
            failure = "Prices and stock are only accepted from an AUTHORIZED supplier source.";
        }

        if (failure == null)
        {
            IDbContextTransaction tx = null;
            try
            {
                if (_db.Database.IsRelational())
                {
                    tx = await _db.Database.BeginTransactionAsync(cancelToken);
                }

                switch (kind)
                {
                    case CatalogImportKind.Vehicles:
                        await StageVehiclesAsync(batch, source, issues, cancelToken);
                        break;
                    case CatalogImportKind.Parts:
                        await StagePartsAsync(batch, source, issues, affectedPartIds, cancelToken);
                        break;
                    case CatalogImportKind.SupplierOffers:
                        await StageOffersAsync(batch, source, issues, affectedPartIds, cancelToken);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(kind));
                }

                if (tx != null)
                {
                    await tx.CommitAsync(cancelToken);
                }
            }
            catch (FeedFormatException ex)
            {
                failure = ex.Message;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogError(ex, "Catalog import batch {BatchId} failed.", batch.Id);
                failure = "Unexpected error while staging. Nothing from this file was imported; see the system log.";
            }
            finally
            {
                if (tx != null)
                {
                    await tx.DisposeAsync();
                }
            }

            if (failure != null)
            {
                DiscardPendingChanges(batch);
                affectedPartIds.Clear();
            }
        }

        batch.Status = failure != null
            ? CatalogBatchStatus.Failed
            : issues.Any(x => x.Severity == IssueSeverity.Error) ? CatalogBatchStatus.CompletedWithErrors : CatalogBatchStatus.Completed;
        batch.FailureReason = failure.Truncate(1000);
        batch.IssuesJson = JsonSerializer.Serialize(issues.Take(MaxStoredIssues), JsonOptions);
        batch.CompletedOnUtc = UtcNow();
        // Keep the content of failed runs for retry only; successful runs only need the hash.
        batch.RawContent = failure != null || issues.Count > 0 ? content : null;
        await _db.SaveChangesAsync(cancelToken);

        if (affectedPartIds.Count > 0)
        {
            // MATCH and VERIFY run on committed data; they never decide approval.
            await _matcher.MatchPartsAsync(affectedPartIds, cancelToken);
            await _publication.RefreshAsync(affectedPartIds, cancelToken);
        }
        else if (kind == CatalogImportKind.Vehicles && failure == null)
        {
            await _matcher.MatchPartsAsync(null, cancelToken);
        }

        return new StageResult(batch, false);
    }

    /// <summary>
    /// Runs a failed or partly failed batch again from its stored content. Safe to repeat.
    /// </summary>
    public async Task<StageResult> RetryAsync(int batchId, CancellationToken cancelToken = default)
    {
        var batch = await _db.CatalogImportBatches().AsNoTracking().FirstOrDefaultAsync(x => x.Id == batchId, cancelToken)
            ?? throw new ArgumentException($"Unknown batch {batchId}.", nameof(batchId));

        if (batch.RawContent == null)
        {
            throw new InvalidOperationException("The batch has no stored content to retry.");
        }

        return await StageAsync(batch.SourceId, batch.Kind, batch.FileName, batch.RawContent, batch.IsFullSnapshot, true, batch.Id, cancelToken);
    }

    /// <summary>
    /// Applies the incoming changes of a reviewed part. Approved prices, rights and state stay as they are;
    /// the part is re-matched and its readiness re-checked.
    /// </summary>
    public async Task<bool> AcceptPendingChangesAsync(int partId, CancellationToken cancelToken = default)
    {
        var part = await _db.CatalogParts().FirstOrDefaultAsync(x => x.Id == partId, cancelToken);
        if (part?.PendingChangesJson == null)
        {
            return false;
        }

        var record = JsonSerializer.Deserialize<PartRecord>(part.PendingChangesJson, JsonOptions);
        await ApplyPartAsync(part, record, part.SourceId, part.LastBatchId, cancelToken);
        part.PendingChangesJson = null;
        part.HasPendingChanges = false;
        await _db.SaveChangesAsync(cancelToken);

        await _matcher.MatchPartsAsync([partId], cancelToken);
        await _publication.RefreshAsync([partId], cancelToken);
        return true;
    }

    public async Task<bool> DiscardPendingChangesAsync(int partId, CancellationToken cancelToken = default)
    {
        var part = await _db.CatalogParts().FirstOrDefaultAsync(x => x.Id == partId && x.HasPendingChanges, cancelToken);
        if (part == null)
        {
            return false;
        }

        part.PendingChangesJson = null;
        part.HasPendingChanges = false;
        await _db.SaveChangesAsync(cancelToken);
        return true;
    }

    /// <summary>
    /// Resolves the source a record names. Unknown keys fall back to the feed source with a warning;
    /// a source that does not allow staging rejects the record.
    /// </summary>
    private static CatalogSource ResolveSource(string sourceKey, CatalogSource feedSource, Dictionary<string, CatalogSource> sources, int row, string label, List<ImportIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(sourceKey))
        {
            return feedSource;
        }

        if (!sources.TryGetValue(sourceKey.Trim(), out var source))
        {
            issues.Add(new(row, label, "sourceKey", $"Unknown source {sourceKey}; the record is attributed to {feedSource.Key}.", IssueSeverity.Warning));
            return feedSource;
        }

        if (!source.AllowsStaging)
        {
            issues.Add(new(row, label, "sourceKey", $"Source {source.Key} is {source.PermissionStatus}; the record was not staged.", IssueSeverity.Error));
            return null;
        }

        return source;
    }

    #region Vehicles

    private async Task StageVehiclesAsync(CatalogImportBatch batch, CatalogSource source, List<ImportIssue> issues, CancellationToken cancelToken)
    {
        var rows = FeedReaders.ReadJson<VehicleRecord>(batch.RawContent);
        batch.TotalCount = rows.Count;

        var now = UtcNow();
        var sources = await _db.CatalogSources().ToDictionaryAsync(x => x.Key, StringComparer.OrdinalIgnoreCase, cancelToken);
        var valid = new Dictionary<string, (int Row, VehicleRecord Record, CatalogSource Source)>();

        foreach (var (row, input) in rows)
        {
            var result = CatalogValidator.ValidateVehicle(input, row, now);
            issues.AddRange(result.Issues);
            var recordSource = result.IsValid
                ? ResolveSource(input.SourceKey, source, sources, row, $"{result.Record.Make} {result.Record.Model}", issues)
                : null;

            if (recordSource == null)
            {
                batch.InvalidCount++;
                continue;
            }

            var key = VehicleNaturalKey(result.Record);
            if (valid.TryGetValue(key, out var first))
            {
                batch.DuplicateCount++;
                issues.Add(new(row, $"{result.Record.Make} {result.Record.Model}", null, $"Duplicate of row {first.Row}; the first record is kept.", IssueSeverity.Warning));
                continue;
            }

            valid[key] = (row, result.Record, recordSource);
        }

        var keys = valid.Keys.ToList();
        var existing = await _db.CatalogVehicleReferences()
            .Where(x => keys.Contains(x.NaturalKey))
            .ToDictionaryAsync(x => x.NaturalKey, cancelToken);

        foreach (var (key, (_, record, recordSource)) in valid)
        {
            var hash = CatalogNormalizer.Sha256(JsonSerializer.Serialize(record, JsonOptions));

            if (!existing.TryGetValue(key, out var entity))
            {
                entity = new CatalogVehicleReference { NaturalKey = key, CreatedOnUtc = now };
                ApplyVehicle(entity, record, hash, recordSource.Id, now);
                _db.CatalogVehicleReferences().Add(entity);
                batch.CreatedCount++;
            }
            else if (entity.ContentHash == hash || entity.Status == VehicleReferenceStatus.Rejected)
            {
                // A rejected record stays rejected: that is an admin decision.
                batch.UnchangedCount++;
            }
            else
            {
                var before = VehicleSignature(entity);
                ApplyVehicle(entity, record, hash, recordSource.Id, now);
                if (entity.MotorVehicleId.HasValue && VehicleSignature(entity) != before)
                {
                    entity.HasPendingChanges = true;
                }

                batch.UpdatedCount++;
            }

            entity.LastBatchId = batch.Id;
        }

        await _db.SaveChangesAsync(cancelToken);
    }

    private static void ApplyVehicle(CatalogVehicleReference entity, VehicleRecord r, string hash, int sourceId, DateTime now)
    {
        entity.Make = r.Make;
        entity.Model = r.Model;
        entity.Variant = r.Variant.Truncate(100);
        entity.ModelCode = r.ModelCode.Truncate(100);
        entity.Generation = r.Generation.Truncate(200);
        entity.YearFrom = r.YearFrom ?? r.GenerationStartYear;
        entity.YearTo = r.YearTo;
        entity.VerifiedYears = r.VerifiedYears.Count > 0 ? string.Join(',', r.VerifiedYears) : null;
        entity.EngineCc = r.EngineCc;
        entity.EngineConfiguration = r.EngineConfiguration.Truncate(200);
        entity.Transmission = r.Transmission.Truncate(200);
        entity.Category = r.Category.Truncate(50);
        entity.Market = r.Market.Truncate(50);
        entity.MarketNotes = r.MarketNotes.Truncate(1000);
        entity.SourcesJson = JsonSerializer.Serialize(r.Sources, JsonOptions);
        entity.UnknownFields = r.UnknownFields.Count > 0 ? string.Join(", ", r.UnknownFields).Truncate(500) : null;
        entity.Confidence = Enum.Parse<RecordConfidence>(r.Confidence);
        entity.Notes = r.Notes;
        entity.SeedAudit = r.SeedAudit == null ? null : $"{r.SeedAudit.Verdict}: {r.SeedAudit.SeedLabel} — {r.SeedAudit.Detail}".Truncate(1000);
        entity.ContentHash = hash;
        entity.SourceId = sourceId;
        entity.UpdatedOnUtc = now;

        var verifiable = entity.Confidence >= RecordConfidence.Medium
            && r.Sources.Any(x => x.IsOfficial)
            && entity.EngineCc.HasValue
            && (entity.YearFrom.HasValue || r.VerifiedYears.Count > 0);

        if (entity.Status != VehicleReferenceStatus.Rejected)
        {
            entity.Status = verifiable ? VehicleReferenceStatus.Verified : VehicleReferenceStatus.Candidate;
            entity.VerifiedOnUtc = verifiable ? now : null;
        }
    }

    /// <summary>
    /// Make, model, variant, first model year and market. Two generations of the same model differ by their first year;
    /// variants (e.g. ABS) are separate records.
    /// </summary>
    public static string VehicleNaturalKey(VehicleRecord r)
    {
        var firstYear = r.YearFrom ?? r.GenerationStartYear ?? (r.VerifiedYears.Count > 0 ? r.VerifiedYears.Min() : null);
        return string.Join('|',
            CatalogNormalizer.MakeKey(r.Make),
            CatalogNormalizer.ModelKey(r.Model),
            CatalogNormalizer.ModelKey(r.Variant),
            firstYear?.ToString(CultureInfo.InvariantCulture) ?? "-",
            CatalogNormalizer.Key(r.Market)).Truncate(450);
    }

    private static string VehicleSignature(CatalogVehicleReference x)
        => $"{x.Make}|{x.Model}|{x.Variant}|{x.YearFrom}|{x.YearTo}|{x.VerifiedYears}|{x.EngineCc}";

    #endregion

    #region Parts

    private async Task StagePartsAsync(CatalogImportBatch batch, CatalogSource source, List<ImportIssue> issues, HashSet<int> affectedPartIds, CancellationToken cancelToken)
    {
        var rows = FeedReaders.IsJson(batch.FileName, batch.RawContent)
            ? FeedReaders.ReadJson<PartRecord>(batch.RawContent)
            : FeedReaders.ReadPartsCsv(batch.RawContent);

        batch.TotalCount = rows.Count;

        var now = UtcNow();
        var sources = await _db.CatalogSources().ToDictionaryAsync(x => x.Key, StringComparer.OrdinalIgnoreCase, cancelToken);
        var valid = new Dictionary<string, (int Row, PartRecord Record, CatalogSource Source)>();

        foreach (var (row, input) in rows)
        {
            var recordSource = ResolveSource(input?.SourceKey, source, sources, row, $"{input?.Brand} {input?.Mpn}", issues);
            var result = recordSource == null
                ? null
                : CatalogValidator.ValidatePart(input, row, recordSource.PermissionStatus, now);

            if (result != null)
            {
                issues.AddRange(result.Issues);
            }

            if (result?.IsValid != true)
            {
                batch.InvalidCount++;
                continue;
            }

            var record = result.Record;
            var key = PartKey(record.Brand, record.Mpn);

            if (valid.TryGetValue(key, out var first))
            {
                // Same brand and part number twice: one product. Keep the first record, add the other's evidence.
                batch.DuplicateCount++;
                MergeEvidence(first.Record, record);
                issues.Add(new(row, $"{record.Brand} {record.Mpn}", "mpn", $"Duplicate of row {first.Row}; fitment and sources were merged into it.", IssueSeverity.Warning));
                continue;
            }

            valid[key] = (row, record, recordSource);
        }

        var brandKeys = valid.Values.Select(x => CatalogNormalizer.Key(x.Record.Brand)).Distinct().ToList();
        var mpnKeys = valid.Values.Select(x => CatalogNormalizer.MpnKey(x.Record.Mpn)).Distinct().ToList();
        var existing = (await _db.CatalogParts()
            .Where(x => brandKeys.Contains(x.BrandKey) && mpnKeys.Contains(x.MpnKey))
            .ToListAsync(cancelToken))
            .ToDictionary(x => $"{x.BrandKey}|{x.MpnKey}");

        var created = new List<(CatalogPart Part, PartRecord Record)>();

        foreach (var (key, (_, record, recordSource)) in valid)
        {
            var hash = PartContentHash(record);

            if (!existing.TryGetValue(key, out var part))
            {
                part = new CatalogPart
                {
                    State = PartPublicationState.ResearchCandidate,
                    FirstSeenOnUtc = now
                };
                ApplyPartFields(part, record, hash, recordSource.Id, now);
                _db.CatalogParts().Add(part);
                created.Add((part, record));
                batch.CreatedCount++;
            }
            else if (part.ContentHash == hash)
            {
                if (part.HasPendingChanges)
                {
                    // The source is back to the reviewed content.
                    part.PendingChangesJson = null;
                    part.HasPendingChanges = false;
                }

                batch.UnchangedCount++;
            }
            else if (part.State == PartPublicationState.ResearchCandidate)
            {
                await ApplyPartAsync(part, record, recordSource.Id, batch.Id, cancelToken);
                batch.UpdatedCount++;
            }
            else
            {
                part.PendingChangesJson = JsonSerializer.Serialize(record, JsonOptions);
                part.HasPendingChanges = true;
                issues.Add(new(0, $"{part.Brand} {part.Mpn}", null, "The record is already reviewed; incoming changes wait for admin review.", IssueSeverity.Warning));
                batch.UpdatedCount++;
            }

            part.LastBatchId = batch.Id;
            part.LastSeenOnUtc = now;
            part.IsMissingFromSource = false;
        }

        await _db.SaveChangesAsync(cancelToken);

        foreach (var (part, record) in created)
        {
            SyncFitment(part, record, []);
        }

        if (batch.IsFullSnapshot)
        {
            var seenIds = existing.Values.Select(x => x.Id).Concat(created.Select(x => x.Part.Id)).ToHashSet();
            var snapshotSourceIds = valid.Values.Select(x => x.Source.Id).Append(source.Id).Distinct().ToList();
            var missing = await _db.CatalogParts()
                .Where(x => snapshotSourceIds.Contains(x.SourceId) && !x.IsMissingFromSource && !seenIds.Contains(x.Id))
                .ToListAsync(cancelToken);

            missing.Each(x => x.IsMissingFromSource = true);
            batch.MissingCount = missing.Count;
        }

        await _db.SaveChangesAsync(cancelToken);

        affectedPartIds.UnionWith(existing.Values.Select(x => x.Id));
        affectedPartIds.UnionWith(created.Select(x => x.Part.Id));
    }

    private async Task ApplyPartAsync(CatalogPart part, PartRecord record, int sourceId, int? batchId, CancellationToken cancelToken)
    {
        ApplyPartFields(part, record, PartContentHash(record), sourceId, UtcNow());
        part.LastBatchId = batchId ?? part.LastBatchId;

        var fitments = await _db.CatalogPartFitments().Where(x => x.PartId == part.Id).ToListAsync(cancelToken);
        var approvedFitmentIds = await _db.CatalogFitmentMatches()
            .Where(x => x.PartId == part.Id && x.Status == FitmentMatchStatus.Approved)
            .Select(x => x.FitmentId)
            .Distinct()
            .ToListAsync(cancelToken);

        var removed = SyncFitment(part, record, fitments, approvedFitmentIds.ToHashSet());
        if (removed.Count > 0)
        {
            var removedIds = removed.Select(x => x.Id).ToList();
            _db.CatalogFitmentMatches().RemoveRange(await _db.CatalogFitmentMatches().Where(x => removedIds.Contains(x.FitmentId)).ToListAsync(cancelToken));
        }
    }

    private void ApplyPartFields(CatalogPart part, PartRecord r, string hash, int sourceId, DateTime now)
    {
        var firstSource = r.Sources.FirstOrDefault();

        part.Brand = r.Brand;
        part.BrandKey = CatalogNormalizer.Key(r.Brand);
        part.ProductName = r.ProductName;
        part.ProductType = r.ProductType;
        part.Mpn = r.Mpn;
        part.MpnKey = CatalogNormalizer.MpnKey(r.Mpn);
        part.SupersededBy = r.SupersededBy.Truncate(100);
        part.OemReferences = r.OemReferences.Count > 0 ? string.Join("; ", r.OemReferences).Truncate(1000) : null;
        part.Gtin = r.Gtin;
        part.Classification = Enum.Parse<PartClassification>(r.Classification);
        part.Condition = r.Condition.Truncate(20);
        part.SpecsJson = r.Specs.Count > 0 ? JsonSerializer.Serialize(r.Specs, JsonOptions) : null;
        part.PackageQuantity = r.PackageQuantity;
        part.SourceId = sourceId;
        part.SourceUrl = firstSource?.Url.Truncate(500);
        part.SourcesJson = JsonSerializer.Serialize(r.Sources, JsonOptions);
        part.AccessedOn = DateTime.TryParseExact(firstSource?.Accessed, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var accessed) ? accessed : null;
        part.RetrievalMethod = r.RetrievalMethod.Truncate(100);
        part.ImageRights = r.ImageRights.Truncate(1000);
        part.CandidateImageUrls = r.Images.Count > 0 ? string.Join('\n', r.Images) : null;
        part.VerificationStatus = Enum.Parse<RecordVerificationStatus>(r.VerificationStatus);
        part.Confidence = Enum.Parse<RecordConfidence>(r.Confidence);
        part.MissingData = r.MissingData.Count > 0 ? string.Join(", ", r.MissingData).Truncate(500) : null;
        part.Notes = r.Notes;
        part.ContentHash = hash;
        part.UpdatedOnUtc = now;
    }

    /// <summary>
    /// Brings the part's fitment statements in line with the record. Statements that disappeared are deleted,
    /// unless an admin approved a match for them: those are kept and flagged.
    /// </summary>
    /// <returns>The removed statements.</returns>
    private List<CatalogPartFitment> SyncFitment(CatalogPart part, PartRecord record, List<CatalogPartFitment> existing, HashSet<int> approvedFitmentIds = null)
    {
        var byKey = existing.ToDictionary(x => x.FitmentKey);
        var incomingKeys = new HashSet<string>();

        foreach (var f in record.Fitment)
        {
            var key = FitmentKey(f);
            if (!incomingKeys.Add(key))
            {
                continue;
            }

            if (byKey.TryGetValue(key, out var entity))
            {
                entity.SourceUrl = f.SourceUrl.Truncate(500);
                entity.Notes = f.Notes.Truncate(1000);
                entity.IsMissingFromSource = false;
            }
            else
            {
                _db.CatalogPartFitments().Add(new CatalogPartFitment
                {
                    PartId = part.Id,
                    FitmentKey = key,
                    Make = f.Make.Truncate(100),
                    Model = f.Model.Truncate(200),
                    Variant = f.Variant.Truncate(100),
                    YearFrom = f.YearFrom.Value,
                    YearTo = f.YearTo,
                    Position = f.Position.Truncate(50),
                    SourceUrl = f.SourceUrl.Truncate(500),
                    Notes = f.Notes.Truncate(1000)
                });
            }
        }

        var removed = new List<CatalogPartFitment>();
        foreach (var entity in existing.Where(x => !incomingKeys.Contains(x.FitmentKey)))
        {
            if (approvedFitmentIds?.Contains(entity.Id) == true)
            {
                entity.IsMissingFromSource = true;
            }
            else
            {
                _db.CatalogPartFitments().Remove(entity);
                removed.Add(entity);
            }
        }

        return removed;
    }

    private static void MergeEvidence(PartRecord target, PartRecord duplicate)
    {
        var fitmentKeys = target.Fitment.Select(FitmentKey).ToHashSet();
        target.Fitment.AddRange(duplicate.Fitment.Where(x => fitmentKeys.Add(FitmentKey(x))));

        var urls = target.Sources.Select(x => x.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);
        target.Sources.AddRange(duplicate.Sources.Where(x => urls.Add(x.Url)));
    }

    public static string PartKey(string brand, string mpn)
        => $"{CatalogNormalizer.Key(CatalogNormalizer.Brand(brand))}|{CatalogNormalizer.MpnKey(mpn)}";

    public static string FitmentKey(FitmentRecord f)
        => string.Join('|',
            CatalogNormalizer.MakeKey(f.Make),
            CatalogNormalizer.ModelKey(f.Model),
            CatalogNormalizer.ModelKey(f.Variant),
            f.YearFrom?.ToString(CultureInfo.InvariantCulture),
            f.YearTo?.ToString(CultureInfo.InvariantCulture) ?? "-",
            CatalogNormalizer.Key(f.Position)).Truncate(450);

    /// <summary>
    /// Hash of everything that describes the product technically. Unchanged hash = unchanged record.
    /// </summary>
    private static string PartContentHash(PartRecord r)
    {
        var canonical = new
        {
            r.Brand,
            r.ProductName,
            r.ProductType,
            r.Mpn,
            r.SupersededBy,
            OemReferences = r.OemReferences.OrderBy(x => x, StringComparer.Ordinal),
            r.Gtin,
            r.Classification,
            r.Condition,
            Specs = r.Specs.OrderBy(x => x.Key, StringComparer.Ordinal),
            r.PackageQuantity,
            Fitment = r.Fitment.Select(FitmentKey).OrderBy(x => x, StringComparer.Ordinal),
            Sources = r.Sources.Select(x => x.Url).OrderBy(x => x, StringComparer.Ordinal),
            r.VerificationStatus,
            r.Confidence,
            Images = r.Images.OrderBy(x => x, StringComparer.Ordinal)
        };

        return CatalogNormalizer.Sha256(JsonSerializer.Serialize(canonical, JsonOptions));
    }

    #endregion

    #region Supplier offers

    private async Task StageOffersAsync(CatalogImportBatch batch, CatalogSource source, List<ImportIssue> issues, HashSet<int> affectedPartIds, CancellationToken cancelToken)
    {
        var rows = FeedReaders.IsJson(batch.FileName, batch.RawContent)
            ? FeedReaders.ReadJson<OfferRecord>(batch.RawContent)
            : FeedReaders.ReadOffersCsv(batch.RawContent);

        batch.TotalCount = rows.Count;

        var now = UtcNow();
        var parts = await _db.CatalogParts()
            .Select(x => new { x.Id, x.BrandKey, x.MpnKey })
            .ToListAsync(cancelToken);
        var partIds = parts.ToDictionary(x => $"{x.BrandKey}|{x.MpnKey}", x => x.Id);

        var existing = await _db.CatalogSupplierOffers()
            .Where(x => x.SourceId == source.Id)
            .ToDictionaryAsync(x => x.SupplierSku, StringComparer.OrdinalIgnoreCase, cancelToken);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (row, r) in rows)
        {
            var label = $"{r?.Brand} {r?.Mpn}".Trim();
            var errors = new List<string>();

            if (r == null) errors.Add("Empty record.");
            else
            {
                if (string.IsNullOrWhiteSpace(r.Brand) || string.IsNullOrWhiteSpace(r.Mpn)) errors.Add("Brand and part number are required.");
                if (string.IsNullOrWhiteSpace(r.SupplierSku)) errors.Add("Supplier SKU is required.");
                if (!CurrencyRegex().IsMatch(CatalogNormalizer.Currency(r.Currency) ?? string.Empty)) errors.Add("Currency must be an ISO 4217 code such as TRY.");
                if (r.PurchaseCost < 0) errors.Add("Purchase cost cannot be negative.");
                if (r.StockQuantity < 0) errors.Add("Stock quantity cannot be negative.");
                if (r.LeadTimeDays < 0) errors.Add("Lead time cannot be negative.");
                if (r.VerifiedOn == null) errors.Add("verified_on (yyyy-MM-dd) is required: stock without a verification date is not used.");
                else if (r.VerifiedOn > now.AddDays(1)) errors.Add("verified_on lies in the future.");
            }

            int partId = 0;
            if (errors.Count == 0 && !partIds.TryGetValue(PartKey(r.Brand, r.Mpn), out partId))
            {
                errors.Add("No catalog part with this brand and part number. Offers never create parts.");
            }

            if (errors.Count == 0 && !seen.Add(r.SupplierSku.Trim()))
            {
                batch.DuplicateCount++;
                issues.Add(new(row, label, "supplier_sku", "Duplicate supplier SKU in this file; the first line is kept.", IssueSeverity.Warning));
                continue;
            }

            if (errors.Count > 0)
            {
                batch.InvalidCount++;
                issues.AddRange(errors.Select(e => new ImportIssue(row, label, null, e, IssueSeverity.Error)));
                continue;
            }

            var sku = r.SupplierSku.Trim().Truncate(100);
            if (!existing.TryGetValue(sku, out var offer))
            {
                offer = new CatalogSupplierOffer { SourceId = source.Id, SupplierSku = sku };
                _db.CatalogSupplierOffers().Add(offer);
                existing[sku] = offer;
                batch.CreatedCount++;
            }
            else if (offer.PartId == partId
                && offer.Currency == CatalogNormalizer.Currency(r.Currency)
                && offer.PurchaseCost == r.PurchaseCost
                && offer.StockQuantity == r.StockQuantity
                && offer.LeadTimeDays == r.LeadTimeDays
                && offer.VerifiedOnUtc == r.VerifiedOn.Value
                && offer.IsActive)
            {
                batch.UnchangedCount++;
                offer.LastBatchId = batch.Id;
                continue;
            }
            else
            {
                if (offer.PartId != partId)
                {
                    affectedPartIds.Add(offer.PartId);
                }

                batch.UpdatedCount++;
            }

            offer.PartId = partId;
            offer.Currency = CatalogNormalizer.Currency(r.Currency);
            offer.PurchaseCost = r.PurchaseCost;
            offer.StockQuantity = r.StockQuantity;
            offer.LeadTimeDays = r.LeadTimeDays;
            offer.VerifiedOnUtc = r.VerifiedOn.Value;
            offer.IsActive = true;
            offer.LastBatchId = batch.Id;
            offer.UpdatedOnUtc = now;
            affectedPartIds.Add(partId);
        }

        if (batch.IsFullSnapshot)
        {
            // An offer that left the supplier feed is deactivated, never deleted.
            foreach (var offer in existing.Values.Where(x => x.IsActive && !seen.Contains(x.SupplierSku)))
            {
                offer.IsActive = false;
                offer.UpdatedOnUtc = now;
                affectedPartIds.Add(offer.PartId);
                batch.MissingCount++;
            }
        }

        await _db.SaveChangesAsync(cancelToken);
    }

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyRegex();

    #endregion

    private void DiscardPendingChanges(CatalogImportBatch batch)
    {
        foreach (var entry in _db.ChangeTracker.Entries().ToList())
        {
            if (!ReferenceEquals(entry.Entity, batch) && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            {
                entry.State = EntityState.Detached;
            }
        }

        batch.CreatedCount = batch.UpdatedCount = batch.UnchangedCount = batch.MissingCount = 0;
    }
}
