using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MotorShop.CatalogImport.Domain;
using MotorShop.CatalogImport.Import;
using MotorShop.CatalogImport.Services;
using MotorShop.Fitment;
using NUnit.Framework;
using Smartstore;

namespace MotorShop.CatalogImport.Tests;

/// <summary>
/// Quality gate for the research datasets of the module (Research/2026-10).
/// These are the real records, not fixtures: every one must pass validation without errors.
/// The files are read from the repository, because release builds do not embed them.
/// </summary>
[TestFixture]
public class BundledDatasetTests : CatalogTestBase
{
    static readonly DateTime Now = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// The three motorcycles whose only source is ktm.com (RESTRICTED legal notice).
    /// </summary>
    static readonly string[] RestrictedOnly = ["KTM 125 Duke", "KTM 125 Duke", "KTM RC 390"];

    private class TestPublicationService(Smartstore.Core.Data.SmartDbContext db) : CatalogPublicationService(db, null)
    {
        protected override Task SaveSlugAsync<T>(T entity, string name) => Task.CompletedTask;
    }

    static string ReadFile(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Smartstore.sln")))
        {
            dir = dir.Parent;
        }

        Assert.That(dir, Is.Not.Null, "Repository root not found.");
        return File.ReadAllText(Path.Combine(dir.FullName, "src", "Smartstore.Modules", "MotorShop.CatalogImport", "Research", BundledResearchService.Edition, fileName));
    }

    static List<BundledResearchService.SourceRecord> Sources()
        => BundledResearchService.ParseSources(ReadFile("sources.json"));

    static Dictionary<string, SourcePermissionStatus> Permissions()
        => Sources().ToDictionary(x => x.Key, x => CatalogValidator.ParsePermission(x.PermissionStatus).Value, StringComparer.OrdinalIgnoreCase);

    static string Host(string url)
    {
        var host = new Uri(url).Host;
        return host.StartsWith("www.") ? host[4..] : host;
    }

    static bool SameSite(string citedUrl, string registerUrl)
    {
        string cited = Host(citedUrl), registered = Host(registerUrl);
        return cited == registered || cited.EndsWith("." + registered);
    }

    [Test]
    public void Every_vehicle_record_validates_without_errors()
    {
        var rows = FeedReaders.ReadJson<VehicleRecord>(ReadFile("vehicles.json"));
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
        var rows = FeedReaders.ReadJson<VehicleRecord>(ReadFile("vehicles.json"));
        var weak = rows.Where(r => r.Record.Confidence == "HIGH" && !r.Record.Sources.Any(s => s.IsOfficial)).ToList();
        Assert.That(weak, Is.Empty);
    }

    [Test]
    public void Every_vehicle_is_attributed_to_a_source_it_actually_cites()
    {
        var register = Sources().ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
        var rows = FeedReaders.ReadJson<VehicleRecord>(ReadFile("vehicles.json"));

        foreach (var (row, v) in rows)
        {
            var label = $"Row {row} {v.Make} {v.Model} {v.Variant}";
            Assert.That(v.SourceKey, Is.Not.Null.And.Not.Empty, $"{label}: no sourceKey, it would inherit the dataset's permission.");

            if (v.SourceKey == BundledResearchService.DatasetSourceKey)
            {
                // Only allowed when at least one cited page is outside the register (an official page checked by hand).
                Assert.That(v.Sources.Any(s => !register.Values.Any(r => r.Url.HasValue() && SameSite(s.Url, r.Url))), Is.True,
                    $"{label}: every cited site is registered, so its own register entry governs it.");
            }
            else
            {
                Assert.That(register.TryGetValue(v.SourceKey, out var entry), Is.True, $"{label}: unknown source '{v.SourceKey}'.");
                Assert.That(v.Sources.Any(s => SameSite(s.Url, entry.Url)), Is.True, $"{label}: '{v.SourceKey}' is not one of its cited sites.");
            }
        }
    }

    [Test]
    public void Only_the_three_restricted_only_motorcycles_are_attributed_to_a_source_that_forbids_staging()
    {
        var permissions = Permissions();
        var rows = FeedReaders.ReadJson<VehicleRecord>(ReadFile("vehicles.json"));

        var blocked = rows
            .Where(r => permissions.TryGetValue(r.Record.SourceKey, out var p) && p < SourcePermissionStatus.PermissionRequired)
            .Select(r => $"{r.Record.Make} {r.Record.Model}")
            .ToList();

        Assert.That(blocked, Is.EquivalentTo(RestrictedOnly));
    }

    [Test]
    public void Every_part_validates_and_names_a_registered_source()
    {
        var sourceKeys = Sources().Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = FeedReaders.ReadJson<PartRecord>(ReadFile("parts.json"));
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
    public void Part_license_status_never_claims_more_than_its_source()
    {
        var permissions = Permissions();
        var rows = FeedReaders.ReadJson<PartRecord>(ReadFile("parts.json"));

        foreach (var (row, record) in rows)
        {
            var license = CatalogValidator.ParsePermission(record.LicenseStatus);
            Assert.That(license, Is.Not.Null, $"Row {row}: unknown licenseStatus '{record.LicenseStatus}'.");
            Assert.That(license, Is.LessThanOrEqualTo(permissions[record.SourceKey]), $"Row {row} {record.Brand} {record.Mpn}: licenseStatus {record.LicenseStatus} exceeds its source.");
        }
    }

    [Test]
    public void Source_register_has_unique_keys_and_no_invented_agreements()
    {
        var sources = Sources();

        Assert.That(sources.Select(x => x.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(sources.Count));
        Assert.That(sources.Select(x => CatalogValidator.ParsePermission(x.PermissionStatus)), Has.None.Null);
        Assert.That(sources.Count(x => x.PermissionStatus == "AUTHORIZED"), Is.Zero, "MOTOR has no agreement with any source yet.");
        Assert.That(sources.Where(x => x.Url.HasValue()).Select(x => x.Url), Has.All.Matches<string>(CatalogValidator.IsHttpUrl));
    }

    [Test]
    public void Embedded_research_matches_the_build_setting()
    {
        var setting = typeof(BundledResearchService).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(x => x.Key == "IncludeCatalogResearch")?.Value;

        Assert.That(setting, Is.Not.Null, "The module must record the IncludeCatalogResearch setting.");
        Assert.That(BundledResearchService.IsBundled, Is.EqualTo(setting == "true"));
    }

    [Test]
    public void Without_embedded_research_loading_is_refused()
    {
        if (BundledResearchService.IsBundled)
        {
            Assert.Ignore("This build embeds the research (IncludeCatalogResearch=true).");
        }

        var db = DbContext;
        var publication = new TestPublicationService(db) { UtcNow = () => Now };
        var import = new CatalogImportService(db, new FitmentMatcher(db), publication) { UtcNow = () => Now };
        var bundled = new BundledResearchService(db, import);

        Assert.That(bundled.IsAvailable, Is.False);
        Assert.ThrowsAsync<InvalidOperationException>(() => bundled.LoadAsync());
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
        var bundled = new BundledResearchService(db, import) { DatasetReader = ReadFile };

        var first = await bundled.LoadAsync();
        var second = await bundled.LoadAsync();

        // The three motorcycles that rely only on a RESTRICTED source are rejected; everything else is staged.
        Assert.That(first.Vehicles.Batch.Status, Is.EqualTo(CatalogBatchStatus.CompletedWithErrors), first.Vehicles.Batch.FailureReason);
        Assert.That(first.Vehicles.Batch.InvalidCount, Is.EqualTo(RestrictedOnly.Length), first.Vehicles.Batch.IssuesJson);
        Assert.That(first.Vehicles.Batch.IssuesJson, Does.Contain("ktm-legal-notice"));
        Assert.That(first.Parts.Batch.Status, Is.EqualTo(CatalogBatchStatus.Completed), first.Parts.Batch.IssuesJson);
        Assert.That(first.Parts.Batch.InvalidCount, Is.Zero);
        Assert.That(second.SourcesAdded, Is.Zero);
        Assert.That(second.Vehicles.AlreadyProcessed && second.Parts.AlreadyProcessed, Is.True);

        Assert.That(await db.CatalogVehicleReferences().CountAsync(), Is.EqualTo(first.Vehicles.Batch.TotalCount - RestrictedOnly.Length));
        Assert.That(await db.CatalogVehicleReferences().CountAsync(x => x.Make == "KTM" && x.Model == "125 Duke"), Is.Zero);
        Assert.That(await db.CatalogVehicleReferences().CountAsync(x => x.Make == "KTM" && x.Model == "RC 390" && x.YearFrom == 2022), Is.Zero,
            "The 2022 RC 390 record cites only ktm.com; the older RC 390 record has an unrestricted source and stays.");
        Assert.That(await db.CatalogParts().CountAsync(), Is.EqualTo(first.Parts.Batch.TotalCount));
        Assert.That(await db.Products.CountAsync(), Is.Zero, "Loading research never creates products.");
        Assert.That(await db.MotorVehicles().CountAsync(), Is.Zero, "Loading research never fills the selector.");

        // Records keep their own source, not the dataset's.
        var sourceIds = await db.CatalogSources().ToDictionaryAsync(x => x.Id, x => x.Key);
        var attributed = await db.CatalogVehicleReferences().Select(x => x.SourceId).ToListAsync();
        Assert.That(attributed.Select(x => sourceIds[x]), Has.Some.Not.EqualTo(BundledResearchService.DatasetSourceKey));

        // Parts belong to sources whose permission is not settled; they cannot be shown even as references.
        var part = await db.CatalogParts().FirstAsync(x => x.VerificationStatus == RecordVerificationStatus.Verified);
        var ex = Assert.ThrowsAsync<PublicationException>(() => publication.ApproveReferenceAsync(part.Id, "test"));
        Assert.That(ex.Message, Does.Contain("SourcePermission"));

        // Publishing motorcycles adds only TR/EU/global records whose source permits factual use.
        var vehicles = new VehicleReferenceService(db, matcher, publication);
        var published = await vehicles.PublishVerifiedAsync();
        var permissions = await db.CatalogSources().ToDictionaryAsync(x => x.Id, x => x.PermissionStatus);
        var inSelector = await db.CatalogVehicleReferences().Where(x => x.MotorVehicleId != null).ToListAsync();
        Assert.That(published.Created, Is.GreaterThan(0));
        Assert.That(inSelector.Select(x => x.Market), Has.None.EqualTo("IN"));
        Assert.That(inSelector.Select(x => permissions[x.SourceId]), Has.All.GreaterThanOrEqualTo(SourcePermissionStatus.PublicFactualReferenceOnly));
        Assert.That(await db.CatalogFitmentMatches().CountAsync(x => x.Status != FitmentMatchStatus.Proposed), Is.Zero, "Matching never approves.");

        TestContext.Out.WriteLine($"Vehicles staged: {attributed.Count}, published to the selector: {published.Created} created, {published.Linked} linked, {published.Skipped} skipped; parts staged: {first.Parts.Batch.TotalCount}; fitment proposals: {await db.CatalogFitmentMatches().CountAsync()}.");
    }
}
