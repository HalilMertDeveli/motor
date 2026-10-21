namespace MotorShop.CatalogImport.Domain;

/// <summary>
/// What MOTOR may do with data from a source. Only <see cref="Authorized"/> backs commercial use
/// (prices, stock, images) and it requires a recorded agreement.
/// </summary>
public enum SourcePermissionStatus
{
    Rejected = -20,
    Restricted = -10,
    PermissionRequired = 0,
    PublicFactualReferenceOnly = 10,
    Authorized = 20
}

public enum CatalogImportKind
{
    Vehicles = 1,
    Parts = 2,
    SupplierOffers = 3
}

public enum CatalogBatchStatus
{
    Failed = -1,
    Pending = 0,
    Completed = 10,
    CompletedWithErrors = 15
}

public enum RecordConfidence
{
    Low = 0,
    Medium = 10,
    High = 20
}

public enum RecordVerificationStatus
{
    Unverified = 0,
    Partial = 10,
    Verified = 20
}

/// <summary>
/// Publication state of a spare-part record.
/// A: research candidate (no product), B: verified reference (visible, not purchasable),
/// C: ready for sale (purchasable).
/// </summary>
public enum PartPublicationState
{
    Rejected = -1,
    ResearchCandidate = 0,
    VerifiedReference = 10,
    ReadyForSale = 20
}

public enum PartClassification
{
    Unknown = 0,
    Oem = 1,
    Aftermarket = 2
}

public enum VehicleReferenceStatus
{
    Rejected = -1,
    Candidate = 0,
    Verified = 10
}

public enum FitmentMatchKind
{
    /// <summary>
    /// The vehicle's whole year range lies inside the year range the part manufacturer states.
    /// </summary>
    Exact = 10,

    /// <summary>
    /// Ranges only overlap. Never approvable: it would claim fitment for years the source does not state.
    /// </summary>
    Partial = 5
}

public enum FitmentMatchStatus
{
    Rejected = -1,
    Proposed = 0,
    Approved = 10
}
