using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MotorShop.CatalogImport.Domain;
using NUnit.Framework;

namespace MotorShop.CatalogImport.Tests;

/// <summary>
/// The whole pipeline suite again on a relational database (SQLite in memory), plus failure paths that only a
/// database with real transactions can show: EF InMemory ignores transactions, so a rollback there proves nothing.
/// </summary>
[TestFixture]
public class RelationalPipelineTests : PipelineTests
{
    protected override bool UseRelationalDatabase => true;

    [Test]
    public void Runs_on_a_relational_database()
    {
        Assert.That(DbContext.Database.IsRelational(), Is.True);
        Assert.That(DbContext.Database.ProviderName, Is.EqualTo("Microsoft.EntityFrameworkCore.Sqlite"));
    }

    [Test]
    public async Task Failure_between_the_two_part_saves_leaves_nothing_behind()
    {
        // Fail StagePartsAsync's second save: the parts were already written inside the transaction, their fitment lines not yet.
        Failures.FailWhen = ctx => ctx.ChangeTracker.Entries<CatalogPartFitment>().Any(e => e.State == EntityState.Added);

        var content = Json(PartJson("FX-100", null, ("FX 250", 2019, 2022)), PartJson("FX-200", null, ("FX 250", 2019, 2022)));
        var result = await _import.StageAsync(_research.Id, CatalogImportKind.Parts, "parts.json", content);

        Assert.That(Failures.Failures, Is.EqualTo(1), "The injected failure did not fire.");
        Assert.That(result.Batch.Status, Is.EqualTo(CatalogBatchStatus.Failed));
        Assert.That(result.Batch.CreatedCount, Is.Zero);

        // No stale entity survives in the change tracker (it would carry the id of a rolled back row).
        var tracked = DbContext.ChangeTracker.Entries().Where(e => e.Entity is not CatalogImportBatch).Select(e => e.Entity.GetType().Name).ToList();
        Assert.That(tracked, Is.Empty);

        // And nothing in the database.
        DbContext.ChangeTracker.Clear();
        Assert.That(await DbContext.CatalogParts().CountAsync(), Is.Zero);
        Assert.That(await DbContext.CatalogPartFitments().CountAsync(), Is.Zero);
        var batch = await DbContext.CatalogImportBatches().SingleAsync(x => x.Id == result.Batch.Id);
        Assert.That(batch.Status, Is.EqualTo(CatalogBatchStatus.Failed));
        Assert.That(batch.RawContent, Is.EqualTo(content));

        // The stored content can be retried cleanly.
        var retry = await _import.RetryAsync(batch.Id);
        Assert.That(retry.Batch.Status, Is.EqualTo(CatalogBatchStatus.Completed), retry.Batch.IssuesJson);
        DbContext.ChangeTracker.Clear();
        Assert.That(await DbContext.CatalogParts().CountAsync(), Is.EqualTo(2));
        Assert.That(await DbContext.CatalogPartFitments().CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task Failed_product_creation_is_rolled_back_completely()
    {
        var part = await StagePartAsync(PartJson("FX-100"));

        _publication.FailOnNextSlug = true;
        Assert.ThrowsAsync<InvalidOperationException>(() => _publication.ApproveReferenceAsync(part.Id, "fixture-admin"));

        Assert.That(DbContext.ChangeTracker.Entries().Count(), Is.Zero, "Rolled back entities must not stay tracked.");
        Assert.That(await DbContext.Products.CountAsync(), Is.Zero, "No orphan product.");
        part = await DbContext.CatalogParts().SingleAsync();
        Assert.That(part.ProductId, Is.Null);
        Assert.That(part.State, Is.EqualTo(PartPublicationState.ResearchCandidate));
    }
}
