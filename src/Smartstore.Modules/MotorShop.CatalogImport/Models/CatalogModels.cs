using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MotorShop.CatalogImport.Domain;
using MotorShop.CatalogImport.Import;
using MotorShop.CatalogImport.Services;
using MotorShop.Fitment.Domain;
using Smartstore;
using Smartstore.Core.Data;

namespace MotorShop.CatalogImport.Models;

/// <summary>
/// What a customer may see about a part: only data of reviewed parts (state B or C).
/// </summary>
public class PublicPartModel
{
    public string Brand { get; set; }
    public string Mpn { get; set; }
    public PartClassification Classification { get; set; }
    public PartPublicationState State { get; set; }
    public string OemReferences { get; set; }
    public int? PackageQuantity { get; set; }
    public Dictionary<string, string> Specs { get; set; } = [];
    public string SourceOrganization { get; set; }
    public string SourceUrl { get; set; }
    public DateTime? AccessedOn { get; set; }

    public bool IsReferenceOnly => State == PartPublicationState.VerifiedReference;

    public static async Task<PublicPartModel> LoadAsync(SmartDbContext db, int productId)
    {
        var part = await db.CatalogParts()
            .AsNoTracking()
            .Where(x => x.ProductId == productId && x.State >= PartPublicationState.VerifiedReference)
            .FirstOrDefaultAsync();

        if (part == null)
        {
            return null;
        }

        var sources = part.SourcesJson.HasValue()
            ? JsonSerializer.Deserialize<List<SourceRef>>(part.SourcesJson, CatalogImportService.JsonOptions)
            : [];
        var first = sources.FirstOrDefault();

        return new PublicPartModel
        {
            Brand = part.Brand,
            Mpn = part.Mpn,
            Classification = part.Classification,
            State = part.State,
            OemReferences = part.OemReferences,
            PackageQuantity = part.PackageQuantity,
            Specs = part.SpecsJson.HasValue()
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(part.SpecsJson, CatalogImportService.JsonOptions)
                : [],
            SourceOrganization = first?.Organization,
            SourceUrl = first?.Url,
            AccessedOn = part.AccessedOn
        };
    }
}

public class CatalogDashboardModel
{
    public List<CatalogSource> Sources { get; set; } = [];
    public List<CatalogImportBatch> Batches { get; set; } = [];
    public Dictionary<PartPublicationState, int> PartCounts { get; set; } = [];
    public int VehicleReferenceCount { get; set; }
    public int VerifiedVehicleCount { get; set; }
    public int PublishedVehicleCount { get; set; }
    public int PendingChangesCount { get; set; }
    public int OpenMatchCount { get; set; }
    public bool BundledResearchAvailable { get; set; }
}

public class SourceEditModel
{
    public int Id { get; set; }

    [Required, StringLength(100)]
    public string Key { get; set; }

    [Required, StringLength(200)]
    public string Name { get; set; }

    [StringLength(500)]
    public string Url { get; set; }

    [StringLength(200)]
    public string Organization { get; set; }

    [StringLength(50)]
    public string SourceType { get; set; }

    public SourcePermissionStatus PermissionStatus { get; set; } = SourcePermissionStatus.PermissionRequired;

    [StringLength(500)]
    public string AgreementReference { get; set; }

    [StringLength(500)]
    public string TermsUrl { get; set; }

    public string TermsSummary { get; set; }

    [StringLength(1000)]
    public string ImageRights { get; set; }

    public string Limitations { get; set; }

    /// <summary>
    /// Url and TermsUrl are rendered as links in the admin. Anything but an absolute http(s) URL
    /// (e.g. a javascript: URL) is refused, so a source can never carry script into an href.
    /// </summary>
    public IEnumerable<string> GetInvalidUrlFields()
    {
        if (Url.HasValue() && !CatalogValidator.IsHttpUrl(Url)) yield return nameof(Url);
        if (TermsUrl.HasValue() && !CatalogValidator.IsHttpUrl(TermsUrl)) yield return nameof(TermsUrl);
    }
}

public class BatchDetailModel
{
    public CatalogImportBatch Batch { get; set; }
    public CatalogSource Source { get; set; }
    public List<ImportIssue> Issues { get; set; } = [];
}

public class PartListModel
{
    public PartPublicationState? State { get; set; }
    public string ProductType { get; set; }
    public string Brand { get; set; }
    public string Query { get; set; }
    public bool PendingOnly { get; set; }
    public List<CatalogPart> Parts { get; set; } = [];
    public Dictionary<int, string> SourceNames { get; set; } = [];
    public List<string> Brands { get; set; } = [];
}

public class PartDetailModel
{
    public CatalogPart Part { get; set; }
    public CatalogSource Source { get; set; }
    public PartReadiness Readiness { get; set; }
    public List<ProductConflict> ProductConflicts { get; set; } = [];
    public List<SourceRef> Sources { get; set; } = [];
    public Dictionary<string, string> Specs { get; set; } = [];
    public List<FitmentRow> Fitment { get; set; } = [];
    public List<OfferRow> Offers { get; set; } = [];
    public PartRecord PendingChanges { get; set; }
    public List<(int Id, string Name)> TaxCategories { get; set; } = [];
    public List<(int Id, string Name)> Categories { get; set; } = [];

    public class FitmentRow
    {
        public CatalogPartFitment Fitment { get; set; }
        public List<(CatalogFitmentMatch Match, MotorVehicle Vehicle)> Matches { get; set; } = [];
    }

    public class OfferRow
    {
        public CatalogSupplierOffer Offer { get; set; }
        public string SourceName { get; set; }
        public SourcePermissionStatus SourcePermission { get; set; }
    }
}

public class VehiclesModel
{
    public List<CatalogVehicleReference> References { get; set; } = [];
    public List<VehicleAuditRow> Audit { get; set; } = [];
    public Dictionary<int, string> SourceNames { get; set; } = [];
    public Dictionary<int, SourcePermissionStatus> SourcePermissions { get; set; } = [];
}
