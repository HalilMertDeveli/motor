using System.Text.RegularExpressions;
using MotorShop.CatalogImport.Domain;
using Smartstore;

namespace MotorShop.CatalogImport.Import;

public class ValidationResult<T>(T record, List<ImportIssue> issues)
{
    /// <summary>
    /// The normalized record, or null if it was rejected.
    /// </summary>
    public T Record { get; } = record;

    public List<ImportIssue> Issues { get; } = issues;

    public bool IsValid => Record != null;
}

/// <summary>
/// Validates and normalizes incoming records. Errors reject a record; warnings drop a single value
/// (an invalid GTIN, an image without rights, a fitment line without evidence) and keep the rest.
/// Nothing is ever filled in: a missing value stays missing.
/// </summary>
public static partial class CatalogValidator
{
    public const int MinYear = 1950;

    /// <summary>
    /// Model years run ahead of the calendar; allow the next two.
    /// </summary>
    public static int MaxYear(DateTime utcNow) => utcNow.Year + 2;

    private static readonly HashSet<string> _vehicleCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "sport", "naked", "scooter", "adventure", "touring", "commuter", "offroad", "cruiser", "other"
    };

    private static readonly string[] _commercialKeys = ["price", "retailprice", "saleprice", "msrp", "stock", "stockquantity", "availability", "cost"];

    public static ValidationResult<VehicleRecord> ValidateVehicle(VehicleRecord r, int row, DateTime utcNow)
    {
        var issues = new List<ImportIssue>();
        if (r == null)
        {
            issues.Add(new(row, null, null, "Empty record.", IssueSeverity.Error));
            return new(default, issues);
        }

        var make = CatalogNormalizer.Make(r.Make);
        var model = CatalogNormalizer.CollapseWhitespace(r.Model);
        var key = $"{make} {model}".Trim();
        var maxYear = MaxYear(utcNow);

        void Error(string field, string message) => issues.Add(new(row, key, field, message, IssueSeverity.Error));
        void Warn(string field, string message) => issues.Add(new(row, key, field, message, IssueSeverity.Warning));

        if (make == null) Error("make", "Make is required.");
        else if (make.Length > 100) Error("make", "Make is longer than 100 characters.");
        if (model == null) Error("model", "Model is required.");
        else if (model.Length > 200) Error("model", "Model is longer than 200 characters.");

        if (r.YearFrom is int from && (from < MinYear || from > maxYear)) Error("yearFrom", $"Year {from} is outside {MinYear}-{maxYear}.");
        if (r.YearTo is int to && (to < MinYear || to > maxYear)) Error("yearTo", $"Year {to} is outside {MinYear}-{maxYear}.");
        if (r.YearFrom.HasValue && r.YearTo.HasValue && r.YearTo < r.YearFrom) Error("yearTo", $"Year range {r.YearFrom}-{r.YearTo} is reversed.");

        var verifiedYears = (r.VerifiedYears ?? []).Distinct().OrderBy(x => x).ToList();
        foreach (var year in verifiedYears)
        {
            if (year < MinYear || year > maxYear
                || (r.YearFrom.HasValue && year < r.YearFrom)
                || (r.YearTo.HasValue && year > r.YearTo))
            {
                Error("verifiedYears", $"Verified year {year} is outside the record's year range.");
            }
        }

        if (!r.YearFrom.HasValue && verifiedYears.Count == 0)
        {
            Warn("years", "No verified year. The record stays a candidate and cannot be published as a selectable motorcycle.");
        }

        if (r.EngineCc is decimal cc && (cc < 25 || cc > 3000)) Error("engineCc", $"Engine displacement {cc} cc is not plausible.");

        var category = CatalogNormalizer.CollapseWhitespace(r.Category);
        if (category != null && !_vehicleCategories.Contains(category))
        {
            Warn("category", $"Unknown category '{category}' recorded as 'other'.");
            category = "other";
        }

        var sources = ValidateSources(r.Sources, Warn);
        if (sources.Count == 0) Error("sources", "At least one source with an http(s) URL is required.");

        var confidence = ParseConfidence(r.Confidence, Warn);
        if (confidence == RecordConfidence.High && !sources.Any(x => x.IsOfficial))
        {
            Warn("confidence", "HIGH confidence needs an official source; lowered to MEDIUM.");
            confidence = RecordConfidence.Medium;
        }

        if (issues.Any(x => x.Severity == IssueSeverity.Error))
        {
            return new(default, issues);
        }

        return new(new VehicleRecord
        {
            Make = make,
            Model = model,
            Variant = CatalogNormalizer.CollapseWhitespace(r.Variant),
            ModelCode = CatalogNormalizer.CollapseWhitespace(r.ModelCode),
            Generation = CatalogNormalizer.CollapseWhitespace(r.Generation),
            YearFrom = r.YearFrom,
            YearTo = r.YearTo,
            VerifiedYears = verifiedYears,
            GenerationStartYear = r.GenerationStartYear,
            EngineCc = r.EngineCc,
            EngineConfiguration = CatalogNormalizer.CollapseWhitespace(r.EngineConfiguration),
            Transmission = CatalogNormalizer.CollapseWhitespace(r.Transmission),
            Category = category?.ToLowerInvariant(),
            Market = CatalogNormalizer.CollapseWhitespace(r.Market)?.ToUpperInvariant(),
            MarketNotes = CatalogNormalizer.CollapseWhitespace(r.MarketNotes),
            Sources = sources,
            Confidence = confidence.ToString(),
            UnknownFields = r.UnknownFields ?? [],
            Notes = r.Notes?.Trim(),
            SeedAudit = r.SeedAudit
        }, issues);
    }

    /// <param name="sourcePermission">Permission of the source the feed is staged from. Images are only kept for authorized sources.</param>
    public static ValidationResult<PartRecord> ValidatePart(PartRecord r, int row, SourcePermissionStatus sourcePermission, DateTime utcNow)
    {
        var issues = new List<ImportIssue>();
        if (r == null)
        {
            issues.Add(new(row, null, null, "Empty record.", IssueSeverity.Error));
            return new(default, issues);
        }

        var brand = CatalogNormalizer.Brand(r.Brand);
        var mpn = r.Mpn?.Trim();
        var key = $"{brand} {mpn}".Trim();
        var maxYear = MaxYear(utcNow);

        void Error(string field, string message) => issues.Add(new(row, key, field, message, IssueSeverity.Error));
        void Warn(string field, string message) => issues.Add(new(row, key, field, message, IssueSeverity.Warning));

        if (brand == null) Error("brand", "Brand is required.");
        else if (brand.Length > 100) Error("brand", "Brand is longer than 100 characters.");

        if (string.IsNullOrEmpty(mpn)) Error("mpn", "Manufacturer part number is required.");
        else if (mpn.Length > 100 || !MpnRegex().IsMatch(mpn)) Error("mpn", "Part number contains characters a manufacturer part number cannot have.");

        var productName = CatalogNormalizer.CollapseWhitespace(r.ProductName);
        if (productName == null) Error("productName", "Product name is required.");
        else if (productName.Length > 400) Error("productName", "Product name is longer than 400 characters.");

        var productType = CatalogNormalizer.ProductType(r.ProductType);
        if (productType == null) Error("productType", $"Unsupported product type '{r.ProductType}'.");

        var classification = (r.Classification?.Trim().ToUpperInvariant()) switch
        {
            "OEM" or "GENUINE" => PartClassification.Oem,
            "AFTERMARKET" => PartClassification.Aftermarket,
            _ => PartClassification.Unknown
        };
        if (classification == PartClassification.Unknown) Warn("classification", "OEM/aftermarket classification is unknown.");

        var gtin = r.Gtin?.Trim();
        if (!string.IsNullOrEmpty(gtin) && !IsValidGtin(gtin))
        {
            Warn("gtin", $"GTIN '{gtin}' has an invalid length or check digit and was dropped.");
            gtin = null;
        }

        if (r.PackageQuantity is int qty && qty <= 0)
        {
            Warn("packageQuantity", "Package quantity must be positive and was dropped.");
        }

        var sources = ValidateSources(r.Sources, Warn);
        if (sources.Count == 0) Error("sources", "At least one source with an http(s) URL is required.");

        var fitment = new List<FitmentRecord>();
        foreach (var f in r.Fitment ?? [])
        {
            var fMake = CatalogNormalizer.Make(f?.Make);
            var fModel = CatalogNormalizer.CollapseWhitespace(f?.Model);
            var label = $"{fMake} {fModel} {f?.YearFrom}-{f?.YearTo}";

            if (fMake == null || fModel == null)
            {
                Warn("fitment", $"Fitment line '{label}' has no make or model and was dropped.");
            }
            else if (f.YearFrom is not int yFrom || yFrom < MinYear || yFrom > maxYear)
            {
                Warn("fitment", $"Fitment line '{label}' has no valid start year and was dropped.");
            }
            else if (f.YearTo is int yTo && (yTo < yFrom || yTo > maxYear))
            {
                Warn("fitment", $"Fitment line '{label}' has an invalid year range and was dropped.");
            }
            else if (!IsHttpUrl(f.SourceUrl))
            {
                Warn("fitment", $"Fitment line '{label}' has no source URL and was dropped.");
            }
            else
            {
                fitment.Add(new FitmentRecord
                {
                    Make = fMake,
                    Model = fModel,
                    Variant = CatalogNormalizer.CollapseWhitespace(f.Variant),
                    YearFrom = f.YearFrom,
                    YearTo = f.YearTo,
                    Position = CatalogNormalizer.CollapseWhitespace(f.Position)?.ToLowerInvariant(),
                    SourceUrl = f.SourceUrl.Trim(),
                    Notes = f.Notes?.Trim()
                });
            }
        }

        var images = new List<string>();
        if (r.Images?.Count > 0)
        {
            if (sourcePermission == SourcePermissionStatus.Authorized)
            {
                images.AddRange(r.Images.Where(IsHttpUrl).Select(x => x.Trim()));
            }
            else
            {
                Warn("images", "Images were dropped: the source is not authorized to supply product images.");
            }
        }

        if (r.Extra != null)
        {
            var commercial = r.Extra.Keys.Where(k => _commercialKeys.Contains(CatalogNormalizer.Key(k))).ToList();
            if (commercial.Count > 0)
            {
                Warn("commercial", $"Ignored {string.Join(", ", commercial)}: prices and stock are only taken from authorized supplier offer feeds.");
            }
        }

        if (issues.Any(x => x.Severity == IssueSeverity.Error))
        {
            return new(default, issues);
        }

        return new(new PartRecord
        {
            Brand = brand,
            ProductName = productName,
            ProductType = productType,
            Mpn = mpn,
            SupersededBy = r.SupersededBy?.Trim().NullEmpty(),
            OemReferences = [.. (r.OemReferences ?? []).Select(x => x?.Trim()).Where(x => !string.IsNullOrEmpty(x)).Distinct()],
            Gtin = gtin,
            Classification = classification.ToString(),
            Condition = r.Condition?.Trim().ToLowerInvariant().NullEmpty(),
            Specs = (r.Specs ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.Value))
                .ToDictionary(x => CatalogNormalizer.CollapseWhitespace(x.Key), x => CatalogNormalizer.CollapseWhitespace(x.Value)),
            PackageQuantity = r.PackageQuantity > 0 ? r.PackageQuantity : null,
            Fitment = fitment,
            Sources = sources,
            RetrievalMethod = r.RetrievalMethod?.Trim(),
            LicenseStatus = r.LicenseStatus?.Trim(),
            ImageRights = r.ImageRights?.Trim(),
            Images = images,
            VerificationStatus = ParseVerification(r.VerificationStatus).ToString(),
            Confidence = ParseConfidence(r.Confidence, Warn).ToString(),
            MissingData = r.MissingData ?? [],
            Notes = r.Notes?.Trim()
        }, issues);
    }

    public static bool IsHttpUrl(string value)
        => Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    /// <summary>
    /// GTIN-8/12/13/14 with a valid GS1 check digit.
    /// </summary>
    public static bool IsValidGtin(string gtin)
    {
        if (gtin is not { Length: 8 or 12 or 13 or 14 } || !gtin.All(char.IsAsciiDigit))
        {
            return false;
        }

        var sum = 0;
        for (var i = gtin.Length - 2; i >= 0; i--)
        {
            var digit = gtin[i] - '0';
            // Weights 3,1,3,1... starting from the digit next to the check digit.
            sum += (gtin.Length - 2 - i) % 2 == 0 ? digit * 3 : digit;
        }

        return (10 - (sum % 10)) % 10 == gtin[^1] - '0';
    }

    public static RecordVerificationStatus ParseVerification(string value)
        => value?.Trim().ToUpperInvariant() switch
        {
            "VERIFIED" => RecordVerificationStatus.Verified,
            "PARTIAL" => RecordVerificationStatus.Partial,
            _ => RecordVerificationStatus.Unverified
        };

    public static RecordConfidence ParseConfidence(string value, Action<string, string> warn = null)
    {
        switch (value?.Trim().ToUpperInvariant())
        {
            case "HIGH": return RecordConfidence.High;
            case "MEDIUM": return RecordConfidence.Medium;
            case "LOW": return RecordConfidence.Low;
            default:
                warn?.Invoke("confidence", "Confidence missing or unknown; recorded as LOW.");
                return RecordConfidence.Low;
        }
    }

    public static SourcePermissionStatus? ParsePermission(string value)
        => value?.Trim().ToUpperInvariant() switch
        {
            "AUTHORIZED" => SourcePermissionStatus.Authorized,
            "PUBLIC_FACTUAL_REFERENCE_ONLY" => SourcePermissionStatus.PublicFactualReferenceOnly,
            "PERMISSION_REQUIRED" => SourcePermissionStatus.PermissionRequired,
            "RESTRICTED" => SourcePermissionStatus.Restricted,
            "REJECTED" => SourcePermissionStatus.Rejected,
            _ => null
        };

    private static List<SourceRef> ValidateSources(List<SourceRef> sources, Action<string, string> warn)
    {
        var result = new List<SourceRef>();
        foreach (var s in sources ?? [])
        {
            if (s == null || !IsHttpUrl(s.Url))
            {
                warn("sources", $"Source '{s?.Url}' is not an http(s) URL and was dropped.");
                continue;
            }

            result.Add(new SourceRef
            {
                Url = s.Url.Trim(),
                Organization = CatalogNormalizer.CollapseWhitespace(s.Organization),
                Type = s.Type?.Trim().ToUpperInvariant(),
                Accessed = s.Accessed?.Trim(),
                Supports = s.Supports ?? []
            });
        }

        return result;
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9 .\-/+]*$")]
    private static partial Regex MpnRegex();
}
