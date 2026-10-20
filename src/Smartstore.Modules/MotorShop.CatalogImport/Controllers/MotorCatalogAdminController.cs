using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MotorShop.CatalogImport.Domain;
using MotorShop.CatalogImport.Import;
using MotorShop.CatalogImport.Models;
using MotorShop.CatalogImport.Services;
using MotorShop.Fitment;
using Smartstore;
using Smartstore.Core.Data;
using Smartstore.Core.Security;
using Smartstore.Web.Controllers;

namespace MotorShop.CatalogImport.Controllers;

/// <summary>
/// Review workflow for catalog sources, import batches, parts and motorcycle references.
/// Reading needs Catalog.Product.Read; every change needs Catalog.Product.Update, creating products Catalog.Product.Create.
/// </summary>
public class MotorCatalogAdminController : AdminController
{
    const string ResRoot = "Plugins.MotorShop.CatalogImport.";

    private readonly SmartDbContext _db;
    private readonly CatalogImportService _importService;
    private readonly CatalogPublicationService _publication;
    private readonly VehicleReferenceService _vehicles;

    public MotorCatalogAdminController(
        SmartDbContext db,
        CatalogImportService importService,
        CatalogPublicationService publication,
        VehicleReferenceService vehicles)
    {
        _db = db;
        _importService = importService;
        _publication = publication;
        _vehicles = vehicles;
    }

    private string CurrentUser
        => Services.WorkContext.CurrentCustomer?.Email ?? Services.WorkContext.CurrentCustomer?.Username ?? "admin";

    #region Dashboard, sources, batches

    [Permission(Permissions.Catalog.Product.Read)]
    public async Task<IActionResult> Index()
    {
        var model = new CatalogDashboardModel
        {
            Sources = await _db.CatalogSources().AsNoTracking().OrderByDescending(x => x.PermissionStatus).ThenBy(x => x.Name).ToListAsync(),
            Batches = await _db.CatalogImportBatches().AsNoTracking().OrderByDescending(x => x.Id).Take(25)
                .Select(x => new CatalogImportBatch
                {
                    Id = x.Id,
                    SourceId = x.SourceId,
                    Kind = x.Kind,
                    FileName = x.FileName,
                    Status = x.Status,
                    StartedOnUtc = x.StartedOnUtc,
                    TotalCount = x.TotalCount,
                    CreatedCount = x.CreatedCount,
                    UpdatedCount = x.UpdatedCount,
                    UnchangedCount = x.UnchangedCount,
                    InvalidCount = x.InvalidCount,
                    DuplicateCount = x.DuplicateCount,
                    MissingCount = x.MissingCount,
                    FailureReason = x.FailureReason,
                    RetryOfBatchId = x.RetryOfBatchId
                })
                .ToListAsync(),
            PartCounts = await _db.CatalogParts().GroupBy(x => x.State).Select(x => new { x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count),
            VehicleReferenceCount = await _db.CatalogVehicleReferences().CountAsync(),
            VerifiedVehicleCount = await _db.CatalogVehicleReferences().CountAsync(x => x.Status == VehicleReferenceStatus.Verified),
            PublishedVehicleCount = await _db.CatalogVehicleReferences().CountAsync(x => x.MotorVehicleId != null),
            PendingChangesCount = await _db.CatalogParts().CountAsync(x => x.HasPendingChanges),
            OpenMatchCount = await _db.CatalogFitmentMatches().CountAsync(x => x.Status == FitmentMatchStatus.Proposed && x.Kind == FitmentMatchKind.Exact)
        };

        return View(model);
    }

    [HttpPost]
    [Permission(Permissions.Catalog.Product.Update)]
    public async Task<IActionResult> Stage(int sourceId, CatalogImportKind kind, IFormFile file, bool isFullSnapshot)
    {
        if (file == null || file.Length == 0)
        {
            NotifyError(T(ResRoot + "Stage.NoFile"));
            return RedirectToAction(nameof(Index));
        }

        if (file.Length > CatalogImportService.MaxContentLength)
        {
            NotifyError(T(ResRoot + "Stage.TooLarge"));
            return RedirectToAction(nameof(Index));
        }

        string content;
        using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, true))
        {
            content = await reader.ReadToEndAsync();
        }

        var result = await _importService.StageAsync(sourceId, kind, Path.GetFileName(file.FileName), content, isFullSnapshot);
        if (result.AlreadyProcessed)
        {
            NotifyInfo(T(ResRoot + "Stage.AlreadyProcessed", result.Batch.Id));
        }

        return RedirectToAction(nameof(Batch), new { id = result.Batch.Id });
    }

    [Permission(Permissions.Catalog.Product.Read)]
    public async Task<IActionResult> Batch(int id)
    {
        var batch = await _db.CatalogImportBatches().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (batch == null)
        {
            return NotFound();
        }

        return View(new BatchDetailModel
        {
            Batch = batch,
            Source = await _db.CatalogSources().AsNoTracking().FirstOrDefaultAsync(x => x.Id == batch.SourceId),
            Issues = batch.IssuesJson.HasValue()
                ? JsonSerializer.Deserialize<List<ImportIssue>>(batch.IssuesJson, CatalogImportService.JsonOptions)
                : []
        });
    }

    [HttpPost]
    [Permission(Permissions.Catalog.Product.Update)]
    public async Task<IActionResult> Retry(int id)
    {
        try
        {
            var result = await _importService.RetryAsync(id);
            return RedirectToAction(nameof(Batch), new { id = result.Batch.Id });
        }
        catch (InvalidOperationException)
        {
            NotifyError(T(ResRoot + "Batch.NoContent"));
            return RedirectToAction(nameof(Batch), new { id });
        }
    }

    [Permission(Permissions.Catalog.Product.Read)]
    public async Task<IActionResult> Source(int id)
    {
        var source = await _db.CatalogSources().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        var model = source == null
            ? new SourceEditModel()
            : new SourceEditModel
            {
                Id = source.Id,
                Key = source.Key,
                Name = source.Name,
                Url = source.Url,
                Organization = source.Organization,
                SourceType = source.SourceType,
                PermissionStatus = source.PermissionStatus,
                AgreementReference = source.AgreementReference,
                TermsUrl = source.TermsUrl,
                TermsSummary = source.TermsSummary,
                ImageRights = source.ImageRights,
                Limitations = source.Limitations
            };

        return View(model);
    }

    [HttpPost]
    [Permission(Permissions.Catalog.Product.Update)]
    public async Task<IActionResult> Source(SourceEditModel model)
    {
        if (model.PermissionStatus == SourcePermissionStatus.Authorized && !model.AgreementReference.HasValue())
        {
            ModelState.AddModelError(nameof(model.AgreementReference), T(ResRoot + "Source.AgreementRequired"));
        }

        foreach (var field in model.GetInvalidUrlFields())
        {
            ModelState.AddModelError(field, T(ResRoot + "Source.InvalidUrl"));
        }

        if (await _db.CatalogSources().AnyAsync(x => x.Key == model.Key && x.Id != model.Id))
        {
            ModelState.AddModelError(nameof(model.Key), T(ResRoot + "Source.KeyExists"));
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var source = model.Id == 0
            ? _db.CatalogSources().Add(new CatalogSource()).Entity
            : await _db.CatalogSources().FirstOrDefaultAsync(x => x.Id == model.Id);

        if (source == null)
        {
            return NotFound();
        }

        source.Key = model.Key.Trim();
        source.Name = model.Name.Trim();
        source.Url = model.Url?.Trim();
        source.Organization = model.Organization?.Trim();
        source.SourceType = model.SourceType?.Trim();
        source.PermissionStatus = model.PermissionStatus;
        source.AgreementReference = model.AgreementReference?.Trim();
        source.TermsUrl = model.TermsUrl?.Trim();
        source.TermsSummary = model.TermsSummary?.Trim();
        source.ImageRights = model.ImageRights?.Trim();
        source.Limitations = model.Limitations?.Trim();
        source.VerifiedOnUtc = DateTime.UtcNow;
        source.UpdatedOnUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // A permission change can take away what a part for sale relies on.
        var partIds = await _db.CatalogParts().Where(x => x.SourceId == source.Id && x.ProductId != null).Select(x => x.Id).ToListAsync();
        var offerPartIds = await _db.CatalogSupplierOffers().Where(x => x.SourceId == source.Id).Select(x => x.PartId).ToListAsync();
        await _publication.RefreshAsync(partIds.Concat(offerPartIds));

        NotifySuccess(T("Admin.Common.DataSuccessfullySaved"));
        return RedirectToAction(nameof(Index));
    }

    #endregion

    #region Parts

    [Permission(Permissions.Catalog.Product.Read)]
    public async Task<IActionResult> Parts(PartListModel model)
    {
        var query = _db.CatalogParts().AsNoTracking();

        if (model.State.HasValue) query = query.Where(x => x.State == model.State);
        if (model.ProductType.HasValue()) query = query.Where(x => x.ProductType == model.ProductType);
        if (model.Brand.HasValue()) query = query.Where(x => x.Brand == model.Brand);
        if (model.PendingOnly) query = query.Where(x => x.HasPendingChanges);
        if (model.Query.HasValue())
        {
            var mpnKey = CatalogNormalizer.MpnKey(model.Query);
            query = query.Where(x => x.MpnKey.Contains(mpnKey) || x.ProductName.Contains(model.Query));
        }

        model.Parts = await query.OrderBy(x => x.Brand).ThenBy(x => x.Mpn).Take(500).ToListAsync();
        model.SourceNames = await _db.CatalogSources().ToDictionaryAsync(x => x.Id, x => x.Name);
        model.Brands = await _db.CatalogParts().Select(x => x.Brand).Distinct().OrderBy(x => x).ToListAsync();

        return View(model);
    }

    [Permission(Permissions.Catalog.Product.Read)]
    public async Task<IActionResult> Part(int id)
    {
        var part = await _db.CatalogParts().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (part == null)
        {
            return NotFound();
        }

        var fitments = await _db.CatalogPartFitments().AsNoTracking().Where(x => x.PartId == id)
            .OrderBy(x => x.Make).ThenBy(x => x.Model).ThenBy(x => x.YearFrom).ToListAsync();
        var matches = await _db.CatalogFitmentMatches().AsNoTracking().Where(x => x.PartId == id).ToListAsync();
        var vehicleIds = matches.Select(x => x.MotorVehicleId).Distinct().ToList();
        var vehicles = await _db.MotorVehicles().AsNoTracking().Where(x => vehicleIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var sources = await _db.CatalogSources().AsNoTracking().ToDictionaryAsync(x => x.Id);
        var offers = await _db.CatalogSupplierOffers().AsNoTracking().Where(x => x.PartId == id).OrderByDescending(x => x.IsActive).ToListAsync();

        var model = new PartDetailModel
        {
            Part = part,
            Source = sources.Get(part.SourceId),
            Readiness = await _publication.GetReadinessAsync(part),
            ProductConflicts = await _publication.FindConflictingProductsAsync(part),
            Sources = part.SourcesJson.HasValue() ? JsonSerializer.Deserialize<List<SourceRef>>(part.SourcesJson, CatalogImportService.JsonOptions) : [],
            Specs = part.SpecsJson.HasValue() ? JsonSerializer.Deserialize<Dictionary<string, string>>(part.SpecsJson, CatalogImportService.JsonOptions) : [],
            PendingChanges = part.PendingChangesJson.HasValue() ? JsonSerializer.Deserialize<PartRecord>(part.PendingChangesJson, CatalogImportService.JsonOptions) : null,
            Fitment = [.. fitments.Select(f => new PartDetailModel.FitmentRow
            {
                Fitment = f,
                Matches = [.. matches.Where(m => m.FitmentId == f.Id && vehicles.ContainsKey(m.MotorVehicleId)).Select(m => (m, vehicles[m.MotorVehicleId]))]
            })],
            Offers = [.. offers.Select(o => new PartDetailModel.OfferRow
            {
                Offer = o,
                SourceName = sources.Get(o.SourceId)?.Name,
                SourcePermission = sources.Get(o.SourceId)?.PermissionStatus ?? SourcePermissionStatus.Rejected
            })],
            TaxCategories = [.. (await _db.TaxCategories.AsNoTracking().OrderBy(x => x.DisplayOrder).Select(x => new { x.Id, x.Name }).ToListAsync()).Select(x => (x.Id, x.Name))],
            Categories = [.. (await _db.Categories.AsNoTracking().Where(x => !x.Deleted).OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync()).Select(x => (x.Id, x.Name))]
        };

        return View(model);
    }

    [HttpPost]
    [Permission(Permissions.Catalog.Product.Create)]
    public Task<IActionResult> ApproveReference(int id)
        => RunPartActionAsync(id, () => _publication.ApproveReferenceAsync(id, CurrentUser));

    [HttpPost]
    [Permission(Permissions.Catalog.Product.Create)]
    public Task<IActionResult> ApproveSale(int id)
        => RunPartActionAsync(id, () => _publication.ApproveSaleAsync(id, CurrentUser));

    [HttpPost]
    [Permission(Permissions.Catalog.Product.Update)]
    public Task<IActionResult> Withdraw(int id)
        => RunPartActionAsync(id, () => _publication.WithdrawAsync(id));

    [HttpPost]
    [Permission(Permissions.Catalog.Product.Update)]
    public Task<IActionResult> Reject(int id, string reason)
        => RunPartActionAsync(id, () => _publication.RejectAsync(id, reason));

    [HttpPost]
    [Permission(Permissions.Catalog.Product.Update)]
    public Task<IActionResult> Commerce(int id, decimal? approvedPrice, int? taxCategoryId, int? categoryId, bool imageRightsApproved, string imageRightsNote)
        => RunPartActionAsync(id, () => _publication.UpdateCommerceAsync(id, approvedPrice, taxCategoryId, categoryId, imageRightsApproved, imageRightsNote));

    [HttpPost]
    [Permission(Permissions.Catalog.Product.Update)]
    public Task<IActionResult> AcceptChanges(int id)
        => RunPartActionAsync(id, () => _importService.AcceptPendingChangesAsync(id));

    [HttpPost]
    [Permission(Permissions.Catalog.Product.Update)]
    public Task<IActionResult> DiscardChanges(int id)
        => RunPartActionAsync(id, () => _importService.DiscardPendingChangesAsync(id));

    [HttpPost]
    [Permission(Permissions.Catalog.Product.Update)]
    public async Task<IActionResult> ApproveMatch(int id, int matchId)
        => await RunPartActionAsync(id, () => _publication.ApproveMatchAsync(id, matchId, CurrentUser));

    [HttpPost]
    [Permission(Permissions.Catalog.Product.Update)]
    public async Task<IActionResult> RejectMatch(int id, int matchId)
        => await RunPartActionAsync(id, () => _publication.RejectMatchAsync(id, matchId, CurrentUser));

    [HttpPost]
    [Permission(Permissions.Catalog.Product.Update)]
    public async Task<IActionResult> ApproveExactMatches(int id)
    {
        var matchIds = await _db.CatalogFitmentMatches()
            .Where(x => x.PartId == id && x.Status == FitmentMatchStatus.Proposed && x.Kind == FitmentMatchKind.Exact)
            .Select(x => x.Id)
            .ToListAsync();

        return await RunPartActionAsync(id, async () =>
        {
            foreach (var matchId in matchIds)
            {
                await _publication.ApproveMatchAsync(id, matchId, CurrentUser);
            }
        });
    }

    [HttpPost]
    [Permission(Permissions.Catalog.Product.Update)]
    public async Task<IActionResult> Rematch()
    {
        var result = await _vehicles.RematchAsync();
        NotifySuccess(T(ResRoot + "Rematch.Done", result.Added));
        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> RunPartActionAsync(int id, Func<Task> action)
    {
        try
        {
            await action();
            NotifySuccess(T("Admin.Common.TaskSuccessfullyProcessed"));
        }
        catch (PublicationException ex)
        {
            var missing = ex.Message.Split(", ", StringSplitOptions.RemoveEmptyEntries)
                .Select(x => T(ResRoot + "Requirement." + x).Value);
            NotifyError(T(ResRoot + "Requirement.Missing", string.Join("; ", missing)));
        }

        return RedirectToAction(nameof(Part), new { id });
    }

    #endregion

    #region Vehicles

    [Permission(Permissions.Catalog.Product.Read)]
    public async Task<IActionResult> Vehicles()
    {
        var model = new VehiclesModel
        {
            References = await _db.CatalogVehicleReferences().AsNoTracking()
                .OrderBy(x => x.Make).ThenBy(x => x.Model).ThenBy(x => x.YearFrom).ToListAsync(),
            Audit = await _vehicles.GetAuditAsync(),
            SourceNames = await _db.CatalogSources().ToDictionaryAsync(x => x.Id, x => x.Name)
        };

        return View(model);
    }

    [HttpPost]
    [Permission(Permissions.Catalog.Product.Update)]
    public async Task<IActionResult> PublishVehicles()
    {
        var result = await _vehicles.PublishVerifiedAsync();
        NotifySuccess(T(ResRoot + "Vehicles.Published", result.Created, result.Linked, result.Skipped));
        return RedirectToAction(nameof(Vehicles));
    }

    [HttpPost]
    [Permission(Permissions.Catalog.Product.Update)]
    public async Task<IActionResult> ResyncVehicle(int id)
    {
        if (await _vehicles.ResyncAsync(id))
        {
            NotifySuccess(T("Admin.Common.TaskSuccessfullyProcessed"));
        }

        return RedirectToAction(nameof(Vehicles));
    }

    [HttpPost]
    [Permission(Permissions.Catalog.Product.Delete)]
    public async Task<IActionResult> RetireUnverified()
    {
        var removed = await _vehicles.RetireUnverifiedAsync();
        NotifySuccess(T(ResRoot + "Vehicles.Retired", removed));
        return RedirectToAction(nameof(Vehicles));
    }

    #endregion
}
