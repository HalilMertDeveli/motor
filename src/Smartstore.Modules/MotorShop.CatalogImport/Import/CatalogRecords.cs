using System.Text.Json;
using System.Text.Json.Serialization;

namespace MotorShop.CatalogImport.Import;

public static class CatalogSourceKeys
{
    public const string Manual = "motor-manual";
}

/// <summary>
/// Evidence for one or more fields of a record.
/// </summary>
public class SourceRef
{
    public string Url { get; set; }
    public string Organization { get; set; }

    /// <summary>
    /// OFFICIAL_MANUFACTURER, OFFICIAL_DISTRIBUTOR or SECONDARY.
    /// </summary>
    public string Type { get; set; }

    public string Accessed { get; set; }
    public List<string> Supports { get; set; } = [];

    [JsonIgnore]
    public bool IsOfficial
        => Type != null && Type.StartsWith("OFFICIAL", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Motorcycle record as delivered by a research dataset.
/// </summary>
public class VehicleRecord
{
    /// <summary>
    /// Key of the CatalogSource the facts come from. Defaults to the source the feed is staged from.
    /// </summary>
    public string SourceKey { get; set; }
    public string Make { get; set; }
    public string Model { get; set; }
    public string Variant { get; set; }
    public string ModelCode { get; set; }
    public string Generation { get; set; }
    public int? YearFrom { get; set; }
    public int? YearTo { get; set; }
    public List<int> VerifiedYears { get; set; } = [];
    public int? GenerationStartYear { get; set; }
    public decimal? EngineCc { get; set; }
    public string EngineConfiguration { get; set; }
    public string Transmission { get; set; }
    public string Category { get; set; }
    public string Market { get; set; }
    public string MarketNotes { get; set; }
    public List<SourceRef> Sources { get; set; } = [];
    public string Confidence { get; set; }
    public List<string> UnknownFields { get; set; } = [];
    public string Notes { get; set; }
    public SeedAuditRecord SeedAudit { get; set; }
}

public class SeedAuditRecord
{
    public string SeedLabel { get; set; }
    public string Verdict { get; set; }
    public string Detail { get; set; }
}

/// <summary>
/// A fitment statement of the part manufacturer.
/// </summary>
public class FitmentRecord
{
    public string Make { get; set; }
    public string Model { get; set; }
    public string Variant { get; set; }
    public int? YearFrom { get; set; }
    public int? YearTo { get; set; }
    public string Position { get; set; }
    public string SourceUrl { get; set; }
    public string Notes { get; set; }
}

/// <summary>
/// Spare-part record as delivered by a research dataset or a parts CSV.
/// Commercial fields (price, stock) are deliberately absent: they only come from authorized supplier offer feeds.
/// </summary>
public class PartRecord
{
    /// <summary>
    /// Key of the CatalogSource the facts come from. Defaults to the source the feed is staged from.
    /// Its permission (managed by an admin) decides what may be done with the record, not <see cref="LicenseStatus"/>.
    /// </summary>
    public string SourceKey { get; set; }
    public string Brand { get; set; }
    public string ProductName { get; set; }
    public string ProductType { get; set; }
    public string Mpn { get; set; }
    public string SupersededBy { get; set; }
    public List<string> OemReferences { get; set; } = [];
    public string Gtin { get; set; }
    public string Classification { get; set; }
    public string Condition { get; set; }
    public Dictionary<string, string> Specs { get; set; } = [];
    public int? PackageQuantity { get; set; }
    public List<FitmentRecord> Fitment { get; set; } = [];
    public List<SourceRef> Sources { get; set; } = [];
    public string RetrievalMethod { get; set; }
    public string LicenseStatus { get; set; }
    public string ImageRights { get; set; }
    public List<string> Images { get; set; } = [];
    public string VerificationStatus { get; set; }
    public string Confidence { get; set; }
    public List<string> MissingData { get; set; } = [];
    public string Notes { get; set; }

    /// <summary>
    /// Fields the reader did not recognize, e.g. a "price" a research file should not carry.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; }
}

/// <summary>
/// Price/stock line of an authorized supplier feed.
/// </summary>
public class OfferRecord
{
    public string Brand { get; set; }
    public string Mpn { get; set; }
    public string SupplierSku { get; set; }
    public string Currency { get; set; }
    public decimal? PurchaseCost { get; set; }
    public int? StockQuantity { get; set; }
    public int? LeadTimeDays { get; set; }
    public DateTime? VerifiedOn { get; set; }
}

/// <summary>
/// A record together with its 1-based position in the feed.
/// </summary>
public record FeedRow<T>(int Row, T Record);

public enum IssueSeverity
{
    Warning,
    Error
}

public record ImportIssue(int Row, string Key, string Field, string Message, IssueSeverity Severity);
