using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MotorShop.CatalogImport.Domain;
using MotorShop.CatalogImport.Import;
using MotorShop.CatalogImport.Services;
using MotorShop.Fitment;
using NUnit.Framework;

namespace MotorShop.CatalogImport.Tests;

/// <summary>
/// Quality gate for the research datasets bundled with the module (Research/2026-10).
/// These are the real records, not fixtures: every one must pass validation without errors.
/// </summary>
[TestFixture]
public class BundledDatasetTests : CatalogTestBase
{
    static readonly DateTime Now = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

    private class TestPublicationService(Smartstore.Core.Data.SmartDbContext db) : CatalogPublicationService(db, null)
    {
        protected override Task SaveSlugAsync<T>(T entity, string name) => Task.CompletedTask;
    }

    [Test]
    public void Every_vehicle_record_validates_without_errors()
    {
        var rows = FeedReaders.ReadJson<VehicleRecord>(BundledResearchService.ReadDataset("vehicles.json"));
        Assert.That(rows, Is.Not.Empty);

        var errors = rows
            .SelectMany(r => CatalogValidator.ValidateVehicle(r.Record, r.Row, Now).Issues)
            .Where(x => x.Severity == IssueSeverity.Error)
            .ToList();

        Assert.That(errors, Is.Empty, string.Join("\n", errors.Select(x => $"{x.Row} {x.Key} {x.Field}: {x.Message}")));
    }

    [Test]
    public void High_confidence_vehicles_rest_on_official_sources()
    {
        var rows = FeedReaders.ReadJson<VehicleRecord>(BundledResearchService.ReadDataset("vehicles.json"));
        var weak = rows.Where(r => r.Record.Confidence == "HIGH" && !r.Record.Sources.Any(s => s.IsOfficial)).ToList();
        Assert.That(weak, Is.Empty);
    }

    [Test]
    public void Every_part_validates_and_names_a_registered_source()
    {
        var sourceKeys = BundledResearchService.ReadSources().Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = FeedReaders.ReadJson<PartRecord>(BundledResearchService.ReadDataset("parts.json"));
        Assert.That(rows, Is.Not.Empty);

        foreach (var (row, record) in rows)
        {
            Assert.That(sourceKeys, Does.Contain(record.SourceKey), $"Row {row}: unknown source '{record.SourceKey}'.");
            Assert.That(record.Extra?.Keys.ToList() ?? [], Has.None.Matches<string>(k => k.Contains("price", StringComparison.OrdinalIgnoreCase)), $"Row {row} carries a price.");
            Assert.That(record.Images, Is.Empty, $"Row {row} carries images.");

            var errors = CatalogValidator.ValidatePart(record, row, SourcePermissionStatus.PermissionRequired, Now).Issues
                .Where(x => x.Severity == IssueSeverity.Error)
                .ToList();
            Assert.That(errors, Is.Empty, string.Join("\n", errors.Select(x => $"{x.Row} {x.Key} {x.Field}: {x.Message}")));
        }
    }

    [Test]
    public void Source_register_has_unique_keys_and_no_invented_agreements()
    {
        var sources = BundledResearchService.ReadSources();

        Assert.That(sources.Select(x => x.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(sources.Count));
        Assert.That(sources.Select(x => CatalogValidator.ParsePermission(x.PermissionStatus)), Has.None.Null);
        Assert.That(sources.Count(x => x.PermissionStatus == "AUTHORIZED"), Is.Zero, "MOTOR has no agreement with any source yet.");
    }

    [Test]
    public async Task Loading_twice_stages_once_and_publishes_nothing()
    {
        var db = DbContext;
        db.ChangeTracker.Clear();
        db.RemoveRange(await db.CatalogFitmentMatches().ToListAsync());
        db.RemoveRange(await db.CatalogPartFitments().ToListAsync());
        db.RemoveRange(await db.CatalogParts().ToListAsync());
        db.RemoveRange(await db.CatalogVehicleReferences().ToListAsync());
        db.RemoveRange(await db.CatalogImportBatches().ToListAsync());
        db.RemoveRange(await db.CatalogSources().ToListAsync());
        db.RemoveRange(await db.MotorVehicles().ToListAsync());
        db.RemoveRange(await db.Products.ToListAsync());
        await db.SaveChangesAsync();

        var publication = new TestPublicationService(db) { UtcNow = () => Now };
        var matcher = new FitmentMatcher(db);
        var import = new CatalogImportService(db, matcher, publication) { UtcNow = () => Now };
        var bundled = new BundledResearchService(db, import);

        var first = await bundled.LoadAsync();
        var second = await bundled.LoadAsync();

        Assert.That(first.Vehicles.Batch.Status, Is.Not.EqualTo(CatalogBatchStatus.Failed), first.Vehicles.Batch.FailureReason);
        Assert.That(first.Parts.Batch.Status, Is.EqualTo(CatalogBatchStatus.Completed), first.Parts.Batch.IssuesJson);
        Assert.That(first.Vehicles.Batch.InvalidCount + first.Parts.Batch.InvalidCount, Is.Zero);
        Assert.That(second.SourcesAdded, Is.Zero);
        Assert.That(second.Vehicles.AlreadyProcessed && second.Parts.AlreadyProcessed, Is.True);

        Assert.That(await db.CatalogVehicleReferences().CountAsync(), Is.EqualTo(first.Vehicles.Batch.TotalCount));
        Assert.That(await db.CatalogParts().CountAsync(), Is.EqualTo(first.Parts.Batch.TotalCount));
        Assert.That(await db.Products.CountAsync(), Is.Zero, "Loading research never creates products.");
        Assert.That(await db.MotorVehicles().CountAsync(), Is.Zero, "Loading research never fills the selector.");

        // Parts belong to sources whose permission is not settled; they cannot be shown even as references.
        var part = await db.CatalogParts().FirstAsync(x => x.VerificationStatus == RecordVerificationStatus.Verified);
        var ex = Assert.ThrowsAsync<PublicationException>(() => publication.ApproveReferenceAsync(part.Id, "test"));
        Assert.That(ex.Message, Does.Contain("SourcePermission"));

        // Publishing motorcycles adds only TR/EU/global records.
        var vehicles = new VehicleReferenceService(db, matcher);
        var published = await vehicles.PublishVerifiedAsync();
        var inSelector = await db.CatalogVehicleReferences().Where(x => x.MotorVehicleId != null).Select(x => x.Market).ToListAsync();
        Assert.That(published.Created, Is.GreaterThan(0));
        Assert.That(inSelector, Has.None.EqualTo("IN"));
        Assert.That(await db.CatalogFitmentMatches().CountAsync(x => x.Status != FitmentMatchStatus.Proposed), Is.Zero, "Matching never approves.");
    }
}
