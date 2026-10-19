using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MotorShop.CatalogImport.Domain;
using MotorShop.CatalogImport.Import;
using MotorShop.CatalogImport.Services;
using MotorShop.Fitment;
using MotorShop.Fitment.Domain;
using NUnit.Framework;
using Smartstore.Core.Catalog.Products;
using Smartstore.Core.Checkout.Tax;

namespace MotorShop.CatalogImport.Tests;

/// <summary>
/// End-to-end pipeline on the in-memory database: stage → match → review → product.
/// Every record here is a fixture (fictitious brand "Fixturebrand", make "Fixturemoto", example.org URLs).
/// </summary>
[TestFixture]
public class PipelineTests : CatalogTestBase
{
    static readonly DateTime Now = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Slugs need the URL service; not under test here.
    /// </summary>
    private class TestPublicationService(Smartstore.Core.Data.SmartDbContext db) : CatalogPublicationService(db, null)
    {
        protected override Task SaveSlugAsync<T>(T entity, string name) => Task.CompletedTask;
    }

    private CatalogPublicationService _publication;
    private FitmentMatcher _matcher;
    private CatalogImportService _import;
    private VehicleReferenceService _vehicles;
    private CatalogSource _research;
    private CatalogSource _supplier;

    [SetUp]
    public async Task ResetAsync()
    {
        var db = DbContext;
        db.ChangeTracker.Clear();
        db.RemoveRange(await db.CatalogFitmentMatches().ToListAsync());
        db.RemoveRange(await db.CatalogPartFitments().ToListAsync());
        db.RemoveRange(await db.CatalogSupplierOffers().ToListAsync());
        db.RemoveRange(await db.CatalogParts().ToListAsync());
        db.RemoveRange(await db.CatalogVehicleReferences().ToListAsync());
        db.RemoveRange(await db.CatalogImportBatches().ToListAsync());
        db.RemoveRange(await db.CatalogSources().ToListAsync());
        db.RemoveRange(await db.ProductMotorVehicles().ToListAsync());
        db.RemoveRange(await db.MotorVehicles().ToListAsync());
        db.RemoveRange(await db.ProductMediaFiles.ToListAsync());
        db.RemoveRange(await db.ProductManufacturers.ToListAsync());
        db.RemoveRange(await db.Products.ToListAsync());
        db.RemoveRange(await db.Manufacturers.ToListAsync());
        db.RemoveRange(await db.TaxCategories.ToListAsync());
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        _research = new CatalogSource { Key = "fixture-research", Name = "Fixture research", PermissionStatus = SourcePermissionStatus.PublicFactualReferenceOnly, UpdatedOnUtc = Now };
        _supplier = new CatalogSource { Key = "fixture-supplier", Name = "Fixture supplier", PermissionStatus = SourcePermissionStatus.Authorized, AgreementReference = "FIXTURE-1", UpdatedOnUtc = Now };
        db.CatalogSources().AddRange(_research, _supplier);
        await db.SaveChangesAsync();

        _publication = new TestPublicationService(db) { UtcNow = () => Now };
        _matcher = new FitmentMatcher(db);
        _import = new CatalogImportService(db, _matcher, _publication) { UtcNow = () => Now };
        _vehicles = new VehicleReferenceService(db, _matcher);
    }

    #region Fixtures

    static object VehicleJson(string model, int? yearFrom, int[] years, decimal? cc = 249, string variant = null, string confidence = "HIGH")
        => new
        {
            make = "Fixturemoto",
            model,
            variant,
            yearFrom,
            verifiedYears = years,
            engineCc = cc,
            category = "sport",
            market = "TR",
            confidence,
            sources = new[] { new { url = "https://fixture.example.org/" + model, organization = "Fixture OEM", type = "OFFICIAL_MANUFACTURER", accessed = "2026-10-19" } }
        };

    static object PartJson(string mpn, string name = null, params (string Model, int From, int? To)[] fitment)
        => new
        {
            brand = "Fixturebrand",
            productName = name ?? $"{mpn} Oil Filter",
            productType = "oil-filter",
            mpn,
            classification = "AFTERMARKET",
            verificationStatus = "VERIFIED",
            confidence = "HIGH",
            sources = new[] { new { url = "https://fixture.example.org/parts/" + mpn, organization = "Fixturebrand", type = "OFFICIAL_MANUFACTURER", accessed = "2026-10-19" } },
            fitment = fitment.Select(f => new { make = "Fixturemoto", model = f.Model, yearFrom = f.From, yearTo = f.To, sourceUrl = "https://fixture.example.org/fit/" + mpn }).ToArray()
        };

    static string Json(params object[] records) => JsonSerializer.Serialize(records);

    private async Task<int> StageVehiclesAndPublishAsync(params object[] vehicles)
    {
        var result = await _import.StageAsync(_research.Id, CatalogImportKind.Vehicles, "vehicles.json", Json(vehicles));
        Assert.That(result.Batch.Status, Is.EqualTo(CatalogBatchStatus.Completed), result.Batch.IssuesJson);
        var published = await _vehicles.PublishVerifiedAsync();
        return published.Created;
    }

    private async Task<CatalogPart> StagePartAsync(object part)
    {
        var result = await _import.StageAsync(_research.Id, CatalogImportKind.Parts, "parts.json", Json(part));
        Assert.That(result.Batch.Status, Is.Not.EqualTo(CatalogBatchStatus.Failed), result.Batch.FailureReason);
        DbContext.ChangeTracker.Clear();
        return await DbContext.CatalogParts().SingleAsync();
    }

    private async Task MakeSellableAsync(CatalogPart part, int stock = 5)
    {
        var tax = new TaxCategory { Name = "Fixture tax" };
        DbContext.TaxCategories.Add(tax);
        await DbContext.SaveChangesAsync();

        var offers = $"brand,mpn,supplier_sku,currency,purchase_cost,stock_quantity,lead_time_days,verified_on\nFixturebrand,{part.Mpn},SUP-{part.Mpn},TRY,100.00,{stock},,2026-10-18\n";
        var offerBatch = await _import.StageAsync(_supplier.Id, CatalogImportKind.SupplierOffers, "offers.csv", offers);
        Assert.That(offerBatch.Batch.Status, Is.EqualTo(CatalogBatchStatus.Completed), offerBatch.Batch.IssuesJson);

        await _publication.UpdateCommerceAsync(part.Id, 249.90m, tax.Id, null, true, "Fixture: written permission");

        DbContext.ProductMediaFiles.Add(new ProductMediaFile { ProductId = part.ProductId.Value, MediaFileId = 1 });
        await DbContext.SaveChangesAsync();
    }

    #endregion

    #region Staging

    [Test]
    public async Task Valid_parts_are_staged_as_hidden_research_candidates()
    {
        var result = await _import.StageAsync(_research.Id, CatalogImportKind.Parts, "parts.json",
            Json(PartJson("FX-100"), PartJson("FX-200"), new { brand = "Fixturebrand", productType = "oil-filter" }));

        Assert.That(result.Batch.Status, Is.EqualTo(CatalogBatchStatus.CompletedWithErrors));
        Assert.That(result.Batch.CreatedCount, Is.EqualTo(2));
        Assert.That(result.Batch.InvalidCount, Is.EqualTo(1));
        Assert.That(result.Batch.IssuesJson, Does.Contain("mpn"));

        var parts = await DbContext.CatalogParts().ToListAsync();
        Assert.That(parts.All(x => x.State == PartPublicationState.ResearchCandidate && x.ProductId == null));
        Assert.That(await DbContext.Products.CountAsync(), Is.Zero, "Staging never creates products.");
        Assert.That(parts.All(x => x.ApprovedPrice == null), "Staging never creates prices.");
    }

    [Test]
    public async Task Same_feed_twice_creates_no_duplicates()
    {
        var json = Json(PartJson("FX-100", null, ("FX 250", 2019, 2022)));

        var first = await _import.StageAsync(_research.Id, CatalogImportKind.Parts, "parts.json", json);
        var second = await _import.StageAsync(_research.Id, CatalogImportKind.Parts, "parts.json", json);
        var forced = await _import.StageAsync(_research.Id, CatalogImportKind.Parts, "parts.json", json, force: true);

        Assert.That(second.AlreadyProcessed, Is.True);
        Assert.That(second.Batch.Id, Is.EqualTo(first.Batch.Id));
        Assert.That(forced.Batch.UnchangedCount, Is.EqualTo(1));
        Assert.That(forced.Batch.CreatedCount, Is.Zero);
        Assert.That(await DbContext.CatalogParts().CountAsync(), Is.EqualTo(1));
        Assert.That(await DbContext.CatalogPartFitments().CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task Duplicate_part_numbers_in_one_feed_are_merged_into_one_part()
    {
        var result = await _import.StageAsync(_research.Id, CatalogImportKind.Parts, "parts.json",
            Json(PartJson("FX-100", null, ("FX 250", 2019, 2022)), PartJson("fx 100", null, ("FX 300", 2020, 2023))));

        Assert.That(result.Batch.DuplicateCount, Is.EqualTo(1));
        Assert.That(await DbContext.CatalogParts().CountAsync(), Is.EqualTo(1));
        Assert.That(await DbContext.CatalogPartFitments().CountAsync(), Is.EqualTo(2), "Fitment of the duplicate is kept as evidence.");
    }

    [Test]
    public async Task Distinct_part_numbers_are_not_collapsed()
    {
        await _import.StageAsync(_research.Id, CatalogImportKind.Parts, "parts.json", Json(PartJson("FX-100"), PartJson("FX-100RC")));
        Assert.That(await DbContext.CatalogParts().CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task Changes_to_a_candidate_are_applied()
    {
        await StagePartAsync(PartJson("FX-100", "Old name"));
        var result = await _import.StageAsync(_research.Id, CatalogImportKind.Parts, "parts.json", Json(PartJson("FX-100", "New name")));

        Assert.That(result.Batch.UpdatedCount, Is.EqualTo(1));
        DbContext.ChangeTracker.Clear();
        Assert.That((await DbContext.CatalogParts().SingleAsync()).ProductName, Is.EqualTo("New name"));
    }

    [Test]
    public async Task Changes_to_a_reviewed_part_wait_for_review()
    {
        var part = await StagePartAsync(PartJson("FX-100", "Reviewed name"));
        await _publication.ApproveReferenceAsync(part.Id, "fixture-admin");

        await _import.StageAsync(_research.Id, CatalogImportKind.Parts, "parts.json", Json(PartJson("FX-100", "Changed upstream")));
        DbContext.ChangeTracker.Clear();
        part = await DbContext.CatalogParts().SingleAsync();

        Assert.That(part.ProductName, Is.EqualTo("Reviewed name"));
        Assert.That(part.HasPendingChanges, Is.True);
        Assert.That(part.State, Is.EqualTo(PartPublicationState.VerifiedReference));

        Assert.That(await _import.AcceptPendingChangesAsync(part.Id), Is.True);
        DbContext.ChangeTracker.Clear();
        part = await DbContext.CatalogParts().SingleAsync();
        Assert.That(part.ProductName, Is.EqualTo("Changed upstream"));
        Assert.That(part.HasPendingChanges, Is.False);
        Assert.That((await DbContext.Products.SingleAsync()).Name, Is.EqualTo("Changed upstream"));
    }

    [Test]
    public async Task Records_missing_from_a_full_snapshot_are_flagged_not_deleted()
    {
        await _import.StageAsync(_research.Id, CatalogImportKind.Parts, "a.json", Json(PartJson("FX-100"), PartJson("FX-200")), isFullSnapshot: true);
        var result = await _import.StageAsync(_research.Id, CatalogImportKind.Parts, "b.json", Json(PartJson("FX-100")), isFullSnapshot: true);

        Assert.That(result.Batch.MissingCount, Is.EqualTo(1));
        DbContext.ChangeTracker.Clear();
        var missing = await DbContext.CatalogParts().SingleAsync(x => x.Mpn == "FX-200");
        Assert.That(missing.IsMissingFromSource, Is.True);
    }

    [Test]
    public async Task Restricted_source_stages_nothing()
    {
        var restricted = new CatalogSource { Key = "fixture-restricted", Name = "Restricted", PermissionStatus = SourcePermissionStatus.Restricted, UpdatedOnUtc = Now };
        DbContext.CatalogSources().Add(restricted);
        await DbContext.SaveChangesAsync();

        var result = await _import.StageAsync(restricted.Id, CatalogImportKind.Parts, "parts.json", Json(PartJson("FX-100")));

        Assert.That(result.Batch.Status, Is.EqualTo(CatalogBatchStatus.Failed));
        Assert.That(await DbContext.CatalogParts().CountAsync(), Is.Zero);
    }

    [Test]
    public async Task Malformed_file_fails_the_batch_and_can_be_retried_safely()
    {
        var result = await _import.StageAsync(_research.Id, CatalogImportKind.Parts, "parts.json", "[{ \"brand\": ");
        Assert.That(result.Batch.Status, Is.EqualTo(CatalogBatchStatus.Failed));
        Assert.That(result.Batch.RawContent, Is.Not.Null);

        var retry = await _import.RetryAsync(result.Batch.Id);
        Assert.That(retry.Batch.RetryOfBatchId, Is.EqualTo(result.Batch.Id));
        Assert.That(retry.Batch.Status, Is.EqualTo(CatalogBatchStatus.Failed));
        Assert.That(await DbContext.CatalogParts().CountAsync(), Is.Zero);
    }

    [Test]
    public async Task Record_can_name_its_own_source()
    {
        var part = JsonSerializer.SerializeToNode(PartJson("FX-100"))!.AsObject();
        part["sourceKey"] = _supplier.Key;
        await _import.StageAsync(_research.Id, CatalogImportKind.Parts, "parts.json", $"[{part.ToJsonString()}]");

        Assert.That((await DbContext.CatalogParts().SingleAsync()).SourceId, Is.EqualTo(_supplier.Id));
    }

    #endregion

    #region Supplier offers

    [Test]
    public async Task Offers_need_an_authorized_source()
    {
        await StagePartAsync(PartJson("FX-100"));
        var csv = "brand,mpn,supplier_sku,currency,purchase_cost,stock_quantity,verified_on\nFixturebrand,FX-100,S-1,TRY,10,5,2026-10-18\n";

        var result = await _import.StageAsync(_research.Id, CatalogImportKind.SupplierOffers, "offers.csv", csv);

        Assert.That(result.Batch.Status, Is.EqualTo(CatalogBatchStatus.Failed));
        Assert.That(await DbContext.CatalogSupplierOffers().CountAsync(), Is.Zero);
    }

    [Test]
    public async Task Offers_never_create_parts_and_need_a_verification_date()
    {
        await StagePartAsync(PartJson("FX-100"));
        var csv = "brand,mpn,supplier_sku,currency,purchase_cost,stock_quantity,verified_on\n" +
                  "Fixturebrand,FX-999,S-1,TRY,10,5,2026-10-18\n" +
                  "Fixturebrand,FX-100,S-2,TRY,10,5,\n" +
                  "Fixturebrand,FX-100,S-3,try,10,5,2026-10-18\n";

        var result = await _import.StageAsync(_supplier.Id, CatalogImportKind.SupplierOffers, "offers.csv", csv);

        Assert.That(result.Batch.InvalidCount, Is.EqualTo(2));
        Assert.That(result.Batch.CreatedCount, Is.EqualTo(1));
        Assert.That(await DbContext.CatalogParts().CountAsync(), Is.EqualTo(1));
        Assert.That((await DbContext.CatalogSupplierOffers().SingleAsync()).Currency, Is.EqualTo("TRY"));
    }

    [Test]
    public async Task Offer_missing_from_snapshot_is_deactivated_not_deleted()
    {
        await StagePartAsync(PartJson("FX-100"));
        var header = "brand,mpn,supplier_sku,currency,purchase_cost,stock_quantity,verified_on\n";
        await _import.StageAsync(_supplier.Id, CatalogImportKind.SupplierOffers, "a.csv", header + "Fixturebrand,FX-100,S-1,TRY,10,5,2026-10-18\n", isFullSnapshot: true);
        await _import.StageAsync(_supplier.Id, CatalogImportKind.SupplierOffers, "b.csv", header, isFullSnapshot: true);

        DbContext.ChangeTracker.Clear();
        var offer = await DbContext.CatalogSupplierOffers().SingleAsync();
        Assert.That(offer.IsActive, Is.False);
    }

    #endregion

    #region Vehicles

    [Test]
    public async Task Vehicle_variants_and_generations_stay_distinct()
    {
        var result = await _import.StageAsync(_research.Id, CatalogImportKind.Vehicles, "v.json", Json(
            VehicleJson("FX 250", 2019, [2019, 2021]),
            VehicleJson("FX 250", 2019, [2019, 2021], variant: "ABS"),
            VehicleJson("FX 250", 2023, [2023]),
            VehicleJson("FX-250", 2019, [2019])));

        Assert.That(result.Batch.CreatedCount, Is.EqualTo(3));
        Assert.That(result.Batch.DuplicateCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Publishing_vehicles_is_idempotent_and_reuses_identical_rows()
    {
        DbContext.MotorVehicles().Add(new MotorVehicle { Make = "Fixturemoto", Model = "FX 250", YearFrom = 2019, YearTo = 2021, EngineCc = 249 });
        await DbContext.SaveChangesAsync();

        var created = await StageVehiclesAndPublishAsync(VehicleJson("FX 250", 2019, [2019, 2021]), VehicleJson("FX 300", 2020, [2020]));
        var again = await _vehicles.PublishVerifiedAsync();

        Assert.That(created, Is.EqualTo(1), "FX 250 2019-2021 already existed and is linked.");
        Assert.That(again.Created + again.Linked, Is.Zero);
        Assert.That(await DbContext.MotorVehicles().CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task Vehicle_without_verified_years_or_cc_is_not_published()
    {
        await _import.StageAsync(_research.Id, CatalogImportKind.Vehicles, "v.json", Json(
            VehicleJson("FX 250", null, []),
            VehicleJson("FX 300", 2020, [2020], cc: null)));

        var result = await _vehicles.PublishVerifiedAsync();

        Assert.That(result.Created, Is.Zero);
        Assert.That(await DbContext.MotorVehicles().CountAsync(), Is.Zero);
    }

    [Test]
    public async Task Retire_removes_only_unverified_unmapped_vehicles()
    {
        await StageVehiclesAndPublishAsync(VehicleJson("FX 250", 2019, [2019, 2021]));
        var seedUnmapped = new MotorVehicle { Make = "Fixturemoto", Model = "Seed A", YearFrom = 2010, YearTo = 2025 };
        var seedMapped = new MotorVehicle { Make = "Fixturemoto", Model = "Seed B", YearFrom = 2010, YearTo = 2025 };
        var product = new Product { Name = "Fixture merchant product" };
        DbContext.AddRange(seedUnmapped, seedMapped, product);
        await DbContext.SaveChangesAsync();
        DbContext.ProductMotorVehicles().Add(new ProductMotorVehicle { ProductId = product.Id, MotorVehicleId = seedMapped.Id });
        await DbContext.SaveChangesAsync();

        var audit = await _vehicles.GetAuditAsync();
        Assert.That(audit.Count(x => x.Reference == null), Is.EqualTo(2));

        var removed = await _vehicles.RetireUnverifiedAsync();

        Assert.That(removed, Is.EqualTo(1));
        var models = await DbContext.MotorVehicles().Select(x => x.Model).ToListAsync();
        Assert.That(models, Is.EquivalentTo(new[] { "FX 250", "Seed B" }));
    }

    #endregion

    #region Matching

    [Test]
    public async Task Only_verified_vehicles_with_equal_model_are_matched()
    {
        await StageVehiclesAndPublishAsync(VehicleJson("FX 250", 2019, [2019, 2021]), VehicleJson("FX 300", 2019, [2019, 2021]));
        // Same make, same displacement, no verified reference: must never be matched.
        DbContext.MotorVehicles().Add(new MotorVehicle { Make = "Fixturemoto", Model = "FX 250R", YearFrom = 2019, YearTo = 2021, EngineCc = 249 });
        await DbContext.SaveChangesAsync();

        await StagePartAsync(PartJson("FX-100", null, ("fx-250", 2019, 2022)));

        var matches = await DbContext.CatalogFitmentMatches().ToListAsync();
        var vehicle = await DbContext.MotorVehicles().SingleAsync(x => x.Model == "FX 250");
        Assert.That(matches, Has.Count.EqualTo(1));
        Assert.That(matches[0].MotorVehicleId, Is.EqualTo(vehicle.Id));
        Assert.That(matches[0].Kind, Is.EqualTo(FitmentMatchKind.Exact));
        Assert.That(matches[0].Status, Is.EqualTo(FitmentMatchStatus.Proposed), "Matching never approves.");
    }

    [Test]
    public async Task Partial_year_overlap_cannot_be_approved()
    {
        await StageVehiclesAndPublishAsync(VehicleJson("FX 250", 2019, [2019, 2023]));
        var part = await StagePartAsync(PartJson("FX-100", null, ("FX 250", 2019, 2021)));

        var match = await DbContext.CatalogFitmentMatches().SingleAsync();
        Assert.That(match.Kind, Is.EqualTo(FitmentMatchKind.Partial));
        Assert.ThrowsAsync<PublicationException>(() => _publication.ApproveMatchAsync(match.Id, "fixture-admin"));
    }

    [Test]
    public async Task Vehicles_published_later_are_matched_to_staged_parts()
    {
        await StagePartAsync(PartJson("FX-100", null, ("FX 250", 2019, 2022)));
        Assert.That(await DbContext.CatalogFitmentMatches().CountAsync(), Is.Zero);

        await StageVehiclesAndPublishAsync(VehicleJson("FX 250", 2019, [2019, 2021]));

        Assert.That(await DbContext.CatalogFitmentMatches().CountAsync(), Is.EqualTo(1));
    }

    #endregion

    #region Publication

    [Test]
    public async Task Reference_product_is_visible_but_not_purchasable()
    {
        var part = await StagePartAsync(PartJson("FX-100"));
        await _publication.ApproveReferenceAsync(part.Id, "fixture-admin");

        var product = await DbContext.Products.SingleAsync();
        Assert.That(product.Published, Is.True);
        Assert.That(product.DisableBuyButton, Is.True);
        Assert.That(product.CallForPrice, Is.True);
        Assert.That(product.Price, Is.Zero);
        Assert.That(product.ManageInventoryMethod, Is.EqualTo(ManageInventoryMethod.DontManageStock));
        Assert.That(product.DisplayStockAvailability, Is.False);
        Assert.That(product.ManufacturerPartNumber, Is.EqualTo("FX-100"));
        Assert.That((await DbContext.Manufacturers.SingleAsync()).Published, Is.False, "New brand pages wait for the admin.");
    }

    [Test]
    public async Task Unverified_part_cannot_become_a_reference()
    {
        var json = JsonSerializer.SerializeToNode(PartJson("FX-100"))!.AsObject();
        json["verificationStatus"] = "PARTIAL";
        await _import.StageAsync(_research.Id, CatalogImportKind.Parts, "parts.json", $"[{json.ToJsonString()}]");
        var part = await DbContext.CatalogParts().SingleAsync();

        var ex = Assert.ThrowsAsync<PublicationException>(() => _publication.ApproveReferenceAsync(part.Id, "fixture-admin"));
        Assert.That(ex.Message, Does.Contain("VerifiedData"));
        Assert.That(await DbContext.Products.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task Sale_needs_every_commercial_requirement()
    {
        var part = await StagePartAsync(PartJson("FX-100"));
        await _publication.ApproveReferenceAsync(part.Id, "fixture-admin");

        var ex = Assert.ThrowsAsync<PublicationException>(() => _publication.ApproveSaleAsync(part.Id, "fixture-admin"));
        foreach (var requirement in new[] { "AuthorizedSupply", "ApprovedPrice", "TaxCategory", "ImageRights", "ProductImage" })
        {
            Assert.That(ex.Message, Does.Contain(requirement));
        }

        Assert.That((await DbContext.Products.SingleAsync()).DisableBuyButton, Is.True);
    }

    [Test]
    public async Task Approved_sale_uses_approved_price_and_supplier_stock_only()
    {
        var part = await StagePartAsync(PartJson("FX-100"));
        await _publication.ApproveReferenceAsync(part.Id, "fixture-admin");
        DbContext.ChangeTracker.Clear();
        part = await DbContext.CatalogParts().SingleAsync();
        await MakeSellableAsync(part, stock: 7);

        await _publication.ApproveSaleAsync(part.Id, "fixture-admin");

        var product = await DbContext.Products.SingleAsync();
        Assert.That(product.DisableBuyButton, Is.False);
        Assert.That(product.CallForPrice, Is.False);
        Assert.That(product.Price, Is.EqualTo(249.90m), "Selling price is the approved price, never the purchase cost.");
        Assert.That(product.ManageInventoryMethod, Is.EqualTo(ManageInventoryMethod.ManageStock));
        Assert.That(product.StockQuantity, Is.EqualTo(7));
    }

    [Test]
    public async Task Sale_falls_back_to_reference_when_supply_disappears()
    {
        var part = await StagePartAsync(PartJson("FX-100"));
        await _publication.ApproveReferenceAsync(part.Id, "fixture-admin");
        DbContext.ChangeTracker.Clear();
        part = await DbContext.CatalogParts().SingleAsync();
        await MakeSellableAsync(part);
        await _publication.ApproveSaleAsync(part.Id, "fixture-admin");

        // The supplier's next complete feed no longer lists the part.
        await _import.StageAsync(_supplier.Id, CatalogImportKind.SupplierOffers, "empty.csv",
            "brand,mpn,supplier_sku,currency,purchase_cost,stock_quantity,verified_on\n", isFullSnapshot: true);

        DbContext.ChangeTracker.Clear();
        Assert.That((await DbContext.CatalogParts().SingleAsync()).State, Is.EqualTo(PartPublicationState.VerifiedReference));
        var product = await DbContext.Products.SingleAsync();
        Assert.That(product.DisableBuyButton, Is.True);
        Assert.That(product.ManageInventoryMethod, Is.EqualTo(ManageInventoryMethod.DontManageStock));
    }

    [Test]
    public async Task Rejected_part_unpublishes_its_product()
    {
        var part = await StagePartAsync(PartJson("FX-100"));
        await _publication.ApproveReferenceAsync(part.Id, "fixture-admin");
        await _publication.RejectAsync(part.Id, "Fixture: wrong part number");

        var product = await DbContext.Products.SingleAsync();
        Assert.That(product.Published, Is.False);
        Assert.That(product.DisableBuyButton, Is.True);
    }

    [Test]
    public async Task Approved_matches_become_product_compatibility_and_rejection_removes_it()
    {
        await StageVehiclesAndPublishAsync(VehicleJson("FX 250", 2019, [2019, 2021]), VehicleJson("FX 300", 2019, [2019, 2021]));
        var part = await StagePartAsync(PartJson("FX-100", null, ("FX 250", 2019, 2022), ("FX 300", 2018, 2022)));
        var matches = await DbContext.CatalogFitmentMatches().ToListAsync();
        Assert.That(matches, Has.Count.EqualTo(2));

        // Approving before the part is a product creates no mapping yet.
        await _publication.ApproveMatchAsync(matches[0].Id, "fixture-admin");
        Assert.That(await DbContext.ProductMotorVehicles().CountAsync(), Is.Zero);

        await _publication.ApproveMatchAsync(matches[1].Id, "fixture-admin");
        await _publication.ApproveReferenceAsync(part.Id, "fixture-admin");

        var product = await DbContext.Products.SingleAsync();
        var mapped = await DbContext.ProductMotorVehicles().Where(x => x.ProductId == product.Id).Select(x => x.MotorVehicleId).ToListAsync();
        Assert.That(mapped, Is.EquivalentTo(matches.Select(x => x.MotorVehicleId)), "One product fits several motorcycles.");

        await _publication.RejectMatchAsync(matches[0].Id, "fixture-admin");
        mapped = await DbContext.ProductMotorVehicles().Select(x => x.MotorVehicleId).ToListAsync();
        Assert.That(mapped, Is.EquivalentTo(new[] { matches[1].MotorVehicleId }));
    }

    [Test]
    public async Task Hand_made_mappings_are_never_removed()
    {
        await StageVehiclesAndPublishAsync(VehicleJson("FX 250", 2019, [2019, 2021]));
        var part = await StagePartAsync(PartJson("FX-100", null, ("FX 250", 2019, 2022)));
        await _publication.ApproveReferenceAsync(part.Id, "fixture-admin");

        var product = await DbContext.Products.SingleAsync();
        var vehicle = await DbContext.MotorVehicles().SingleAsync();
        DbContext.ProductMotorVehicles().Add(new ProductMotorVehicle { ProductId = product.Id, MotorVehicleId = vehicle.Id });
        await DbContext.SaveChangesAsync();

        var match = await DbContext.CatalogFitmentMatches().SingleAsync();
        await _publication.RejectMatchAsync(match.Id, "fixture-admin");

        Assert.That(await DbContext.ProductMotorVehicles().CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task Open_fitment_proposals_block_the_sale()
    {
        await StageVehiclesAndPublishAsync(VehicleJson("FX 250", 2019, [2019, 2021]));
        var part = await StagePartAsync(PartJson("FX-100", null, ("FX 250", 2019, 2022)));
        await _publication.ApproveReferenceAsync(part.Id, "fixture-admin");
        DbContext.ChangeTracker.Clear();
        part = await DbContext.CatalogParts().SingleAsync();
        await MakeSellableAsync(part);

        var ex = Assert.ThrowsAsync<PublicationException>(() => _publication.ApproveSaleAsync(part.Id, "fixture-admin"));
        Assert.That(ex.Message, Does.Contain("FitmentReviewed"));
    }

    [Test]
    public async Task Image_rights_approval_needs_a_record_of_who_granted_them()
    {
        var part = await StagePartAsync(PartJson("FX-100"));
        var ex = Assert.ThrowsAsync<PublicationException>(() => _publication.UpdateCommerceAsync(part.Id, 10m, null, null, true, " "));
        Assert.That(ex.Message, Is.EqualTo("ImageRightsNote"));
    }

    #endregion
}
