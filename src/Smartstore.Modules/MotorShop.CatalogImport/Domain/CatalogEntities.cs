using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Smartstore.Domain;

namespace MotorShop.CatalogImport.Domain;

/// <summary>
/// A place catalog data comes from, with what MOTOR is permitted to do with it.
/// </summary>
[Table("MotorCatalogSource")]
[Index(nameof(Key), IsUnique = true, Name = "IX_MotorCatalogSource_Key")]
public class CatalogSource : BaseEntity
{
    [Required, StringLength(100)]
    public string Key { get; set; }

    [Required, StringLength(200)]
    public string Name { get; set; }

    [StringLength(500)]
    public string Url { get; set; }

    [StringLength(200)]
    public string Organization { get; set; }

    /// <summary>
    /// OFFICIAL_MANUFACTURER, OFFICIAL_DISTRIBUTOR, DATA_AGGREGATOR, WHOLESALER, RETAILER, OTHER.
    /// </summary>
    [StringLength(50)]
    public string SourceType { get; set; }

    public SourcePermissionStatus PermissionStatus { get; set; }

    /// <summary>
    /// Reference to the written agreement (contract number, e-mail date). Required for <see cref="SourcePermissionStatus.Authorized"/>.
    /// </summary>
    [StringLength(500)]
    public string AgreementReference { get; set; }

    [StringLength(200)]
    public string AccessMethod { get; set; }

    [StringLength(500)]
    public string DataTypes { get; set; }

    [StringLength(500)]
    public string TermsUrl { get; set; }

    [MaxLength]
    public string TermsSummary { get; set; }

    [StringLength(1000)]
    public string ImageRights { get; set; }

    [StringLength(50)]
    public string ImportReadiness { get; set; }

    [MaxLength]
    public string Limitations { get; set; }

    [StringLength(500)]
    public string ContactUrl { get; set; }

    public DateTime? VerifiedOnUtc { get; set; }

    public DateTime UpdatedOnUtc { get; set; }

    [NotMapped]
    public bool AllowsStaging
        => PermissionStatus >= SourcePermissionStatus.PermissionRequired;
}

/// <summary>
/// One run of a feed through the pipeline. The raw content is kept so a run can be retried.
/// </summary>
[Table("MotorCatalogImportBatch")]
[Index(nameof(SourceId), nameof(Kind), nameof(ContentHash), Name = "IX_MotorCatalogImportBatch_Source_Kind_Hash")]
public class CatalogImportBatch : BaseEntity
{
    public int SourceId { get; set; }

    public CatalogImportKind Kind { get; set; }

    [StringLength(255)]
    public string FileName { get; set; }

    [Required, StringLength(64)]
    public string ContentHash { get; set; }

    [MaxLength]
    public string RawContent { get; set; }

    /// <summary>
    /// The feed is a complete snapshot of the source: records it does not contain are flagged as missing (never deleted).
    /// </summary>
    public bool IsFullSnapshot { get; set; }

    public CatalogBatchStatus Status { get; set; }

    public DateTime StartedOnUtc { get; set; }

    public DateTime? CompletedOnUtc { get; set; }

    public int TotalCount { get; set; }

    public int CreatedCount { get; set; }

    public int UpdatedCount { get; set; }

    public int UnchangedCount { get; set; }

    public int InvalidCount { get; set; }

    public int DuplicateCount { get; set; }

    public int MissingCount { get; set; }

    /// <summary>
    /// JSON array of <c>{ row, key, field, message, severity }</c>. Never contains stack traces.
    /// </summary>
    [MaxLength]
    public string IssuesJson { get; set; }

    [StringLength(1000)]
    public string FailureReason { get; set; }

    public int? RetryOfBatchId { get; set; }
}

/// <summary>
/// A researched motorcycle with provenance. Becomes a selectable <c>MotorVehicle</c> only when an admin publishes it.
/// </summary>
[Table("MotorCatalogVehicleReference")]
[Index(nameof(NaturalKey), IsUnique = true, Name = "IX_MotorCatalogVehicleReference_NaturalKey")]
[Index(nameof(MotorVehicleId), Name = "IX_MotorCatalogVehicleReference_MotorVehicle")]
[Index(nameof(SourceId), Name = "IX_MotorCatalogVehicleReference_Source")]
public class CatalogVehicleReference : BaseEntity
{
    [Required, StringLength(100)]
    public string Make { get; set; }

    [Required, StringLength(200)]
    public string Model { get; set; }

    [StringLength(100)]
    public string Variant { get; set; }

    [StringLength(100)]
    public string ModelCode { get; set; }

    [StringLength(200)]
    public string Generation { get; set; }

    public int? YearFrom { get; set; }

    /// <summary>
    /// Null when no official source states the end of production.
    /// </summary>
    public int? YearTo { get; set; }

    /// <summary>
    /// Comma-separated model years an official source shows explicitly.
    /// </summary>
    [StringLength(500)]
    public string VerifiedYears { get; set; }

    [Precision(7, 2)]
    public decimal? EngineCc { get; set; }

    [StringLength(200)]
    public string EngineConfiguration { get; set; }

    [StringLength(200)]
    public string Transmission { get; set; }

    [StringLength(50)]
    public string Category { get; set; }

    [StringLength(50)]
    public string Market { get; set; }

    [StringLength(1000)]
    public string MarketNotes { get; set; }

    [MaxLength]
    public string SourcesJson { get; set; }

    [StringLength(500)]
    public string UnknownFields { get; set; }

    public RecordConfidence Confidence { get; set; }

    public VehicleReferenceStatus Status { get; set; }

    [MaxLength]
    public string Notes { get; set; }

    [StringLength(1000)]
    public string SeedAudit { get; set; }

    [Required, StringLength(450)]
    public string NaturalKey { get; set; }

    [Required, StringLength(64)]
    public string ContentHash { get; set; }

    public int SourceId { get; set; }

    public int? MotorVehicleId { get; set; }

    /// <summary>
    /// The record changed after it was published to Fitment; the vehicle row needs a re-sync.
    /// </summary>
    public bool HasPendingChanges { get; set; }

    public int? LastBatchId { get; set; }

    public DateTime? VerifiedOnUtc { get; set; }

    public DateTime CreatedOnUtc { get; set; }

    public DateTime UpdatedOnUtc { get; set; }
}

/// <summary>
/// A spare part identified by brand + manufacturer part number, with research provenance,
/// review state and the commercial data an admin approved.
/// </summary>
[Table("MotorCatalogPart")]
[Index(nameof(BrandKey), nameof(MpnKey), IsUnique = true, Name = "IX_MotorCatalogPart_Brand_Mpn")]
[Index(nameof(ProductId), Name = "IX_MotorCatalogPart_Product")]
[Index(nameof(State), Name = "IX_MotorCatalogPart_State")]
[Index(nameof(SourceId), Name = "IX_MotorCatalogPart_Source")]
public class CatalogPart : BaseEntity
{
    [Required, StringLength(100)]
    public string Brand { get; set; }

    [Required, StringLength(100)]
    public string BrandKey { get; set; }

    [Required, StringLength(400)]
    public string ProductName { get; set; }

    [Required, StringLength(50)]
    public string ProductType { get; set; }

    /// <summary>
    /// Part number exactly as the manufacturer prints it. Never rewritten.
    /// </summary>
    [Required, StringLength(100)]
    public string Mpn { get; set; }

    /// <summary>
    /// Upper-case MPN without separators. Used only for duplicate detection.
    /// </summary>
    [Required, StringLength(100)]
    public string MpnKey { get; set; }

    [StringLength(100)]
    public string SupersededBy { get; set; }

    [StringLength(1000)]
    public string OemReferences { get; set; }

    [StringLength(50)]
    public string Gtin { get; set; }

    public PartClassification Classification { get; set; }

    [StringLength(20)]
    public string Condition { get; set; }

    [MaxLength]
    public string SpecsJson { get; set; }

    public int? PackageQuantity { get; set; }

    public int SourceId { get; set; }

    [StringLength(500)]
    public string SourceUrl { get; set; }

    [MaxLength]
    public string SourcesJson { get; set; }

    public DateTime? AccessedOn { get; set; }

    [StringLength(100)]
    public string RetrievalMethod { get; set; }

    [StringLength(1000)]
    public string ImageRights { get; set; }

    /// <summary>
    /// Image URLs offered by an authorized source. Never downloaded automatically.
    /// </summary>
    [MaxLength]
    public string CandidateImageUrls { get; set; }

    public RecordVerificationStatus VerificationStatus { get; set; }

    public RecordConfidence Confidence { get; set; }

    [StringLength(500)]
    public string MissingData { get; set; }

    [MaxLength]
    public string Notes { get; set; }

    public PartPublicationState State { get; set; }

    [Required, StringLength(64)]
    public string ContentHash { get; set; }

    /// <summary>
    /// Incoming data for an already reviewed record, waiting for an admin to accept it.
    /// </summary>
    [MaxLength]
    public string PendingChangesJson { get; set; }

    public bool HasPendingChanges { get; set; }

    /// <summary>
    /// The last full snapshot of the source did not contain this record. The record is kept.
    /// </summary>
    public bool IsMissingFromSource { get; set; }

    public int? LastBatchId { get; set; }

    public int? ProductId { get; set; }

    public int? CategoryId { get; set; }

    [Precision(18, 4)]
    public decimal? ApprovedPrice { get; set; }

    public int? TaxCategoryId { get; set; }

    public bool ImageRightsApproved { get; set; }

    [StringLength(1000)]
    public string ImageRightsNote { get; set; }

    public DateTime? ReferenceApprovedOnUtc { get; set; }

    [StringLength(200)]
    public string ReferenceApprovedBy { get; set; }

    public DateTime? SaleApprovedOnUtc { get; set; }

    [StringLength(200)]
    public string SaleApprovedBy { get; set; }

    [StringLength(1000)]
    public string RejectionReason { get; set; }

    public DateTime FirstSeenOnUtc { get; set; }

    public DateTime LastSeenOnUtc { get; set; }

    public DateTime UpdatedOnUtc { get; set; }
}

/// <summary>
/// One fitment statement of the part manufacturer, e.g. "Yamaha MT-07 2014-2020".
/// </summary>
[Table("MotorCatalogPartFitment")]
[Index(nameof(PartId), nameof(FitmentKey), IsUnique = true, Name = "IX_MotorCatalogPartFitment_Part_Key")]
public class CatalogPartFitment : BaseEntity
{
    public int PartId { get; set; }

    [Required, StringLength(450)]
    public string FitmentKey { get; set; }

    [Required, StringLength(100)]
    public string Make { get; set; }

    [Required, StringLength(200)]
    public string Model { get; set; }

    [StringLength(100)]
    public string Variant { get; set; }

    public int YearFrom { get; set; }

    /// <summary>
    /// Null means the manufacturer states "from <see cref="YearFrom"/> on".
    /// </summary>
    public int? YearTo { get; set; }

    [StringLength(50)]
    public string Position { get; set; }

    [Required, StringLength(500)]
    public string SourceUrl { get; set; }

    [StringLength(1000)]
    public string Notes { get; set; }

    /// <summary>
    /// The statement is no longer in the latest source data, but a reviewed match exists.
    /// </summary>
    public bool IsMissingFromSource { get; set; }
}

/// <summary>
/// A proposed or reviewed link between a fitment statement and a verified <c>MotorVehicle</c>.
/// </summary>
[Table("MotorCatalogFitmentMatch")]
[Index(nameof(FitmentId), nameof(MotorVehicleId), IsUnique = true, Name = "IX_MotorCatalogFitmentMatch_Fitment_Vehicle")]
[Index(nameof(MotorVehicleId), Name = "IX_MotorCatalogFitmentMatch_Vehicle")]
public class CatalogFitmentMatch : BaseEntity
{
    public int FitmentId { get; set; }

    public int PartId { get; set; }

    public int MotorVehicleId { get; set; }

    public FitmentMatchKind Kind { get; set; }

    public FitmentMatchStatus Status { get; set; }

    /// <summary>
    /// This match inserted the ProductMotorVehicle row, so rejecting it may remove that row again.
    /// </summary>
    public bool CreatedMapping { get; set; }

    public DateTime? ReviewedOnUtc { get; set; }

    [StringLength(200)]
    public string ReviewedBy { get; set; }
}

/// <summary>
/// Price and availability of a part from an authorized supplier feed. Purchase cost is never a selling price.
/// </summary>
[Table("MotorCatalogSupplierOffer")]
[Index(nameof(SourceId), nameof(SupplierSku), IsUnique = true, Name = "IX_MotorCatalogSupplierOffer_Source_Sku")]
[Index(nameof(PartId), Name = "IX_MotorCatalogSupplierOffer_Part")]
[Index(nameof(SourceId), Name = "IX_MotorCatalogSupplierOffer_Source")]
public class CatalogSupplierOffer : BaseEntity
{
    public int PartId { get; set; }

    public int SourceId { get; set; }

    [Required, StringLength(100)]
    public string SupplierSku { get; set; }

    [Required, StringLength(3)]
    public string Currency { get; set; }

    [Precision(18, 4)]
    public decimal? PurchaseCost { get; set; }

    public int? StockQuantity { get; set; }

    public int? LeadTimeDays { get; set; }

    public DateTime VerifiedOnUtc { get; set; }

    public bool IsActive { get; set; }

    public int? LastBatchId { get; set; }

    public DateTime UpdatedOnUtc { get; set; }
}
