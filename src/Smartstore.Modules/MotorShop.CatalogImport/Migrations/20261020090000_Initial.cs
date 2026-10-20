using System.Data;
using FluentMigrator;
using Microsoft.EntityFrameworkCore;
using MotorShop.CatalogImport.Domain;
using MotorShop.CatalogImport.Import;
using Smartstore.Core.Data;
using Smartstore.Core.Data.Migrations;
using Smartstore.Data.Migrations;
using Smartstore.Domain;

namespace MotorShop.CatalogImport.Migrations;

/// <summary>
/// Staging and provenance tables. Every reference column has a real foreign key, so a record can never point
/// at a missing source, product, part or motorcycle. Deleting a product or motorcycle never deletes research data:
/// the link is set to NULL (matches to a deleted motorcycle are removed, they mean nothing without it).
/// </summary>
[MigrationVersion("2026-10-20 09:00:00", "MotorShop.CatalogImport: Initial")]
internal class Initial : Migration, IDataSeeder<SmartDbContext>
{
    const string SourceTable = "MotorCatalogSource";
    const string BatchTable = "MotorCatalogImportBatch";
    const string VehicleRefTable = "MotorCatalogVehicleReference";
    const string PartTable = "MotorCatalogPart";
    const string FitmentTable = "MotorCatalogPartFitment";
    const string MatchTable = "MotorCatalogFitmentMatch";
    const string OfferTable = "MotorCatalogSupplierOffer";

    // Owned by MotorShop.Fitment / Smartstore core.
    const string MotorVehicleTable = "MotorVehicle";
    const string ProductTable = "Product";

    const string Id = nameof(BaseEntity.Id);

    public DataSeederStage Stage => DataSeederStage.Early;
    public bool AbortOnFailure => false;

    public Task SeedAsync(SmartDbContext context, CancellationToken cancelToken = default)
        => SeedManualSourceAsync(context, cancelToken);

    /// <summary>
    /// Idempotent. Also called on app installation, where this migration does not run.
    /// </summary>
    internal static async Task SeedManualSourceAsync(SmartDbContext context, CancellationToken cancelToken = default)
    {
        if (!await context.CatalogSources().AnyAsync(x => x.Key == CatalogSourceKeys.Manual, cancelToken))
        {
            context.CatalogSources().Add(new CatalogSource
            {
                Key = CatalogSourceKeys.Manual,
                Name = "MOTOR manual research",
                Organization = "MOTOR",
                SourceType = "OTHER",
                PermissionStatus = SourcePermissionStatus.PublicFactualReferenceOnly,
                AccessMethod = "Manual verification of public pages",
                DataTypes = "facts (names, part numbers, fitment statements)",
                Limitations = "Facts only. No prices, stock or images.",
                UpdatedOnUtc = DateTime.UtcNow
            });

            await context.SaveChangesAsync(cancelToken);
        }
    }

    public override void Up()
    {
        if (!Schema.Table(SourceTable).Exists())
        {
            Create.Table(SourceTable)
                .WithIdColumn()
                .WithColumn(nameof(CatalogSource.Key)).AsString(100).NotNullable()
                .WithColumn(nameof(CatalogSource.Name)).AsString(200).NotNullable()
                .WithColumn(nameof(CatalogSource.Url)).AsString(500).Nullable()
                .WithColumn(nameof(CatalogSource.Organization)).AsString(200).Nullable()
                .WithColumn(nameof(CatalogSource.SourceType)).AsString(50).Nullable()
                .WithColumn(nameof(CatalogSource.PermissionStatus)).AsInt32().NotNullable()
                .WithColumn(nameof(CatalogSource.AgreementReference)).AsString(500).Nullable()
                .WithColumn(nameof(CatalogSource.AccessMethod)).AsString(200).Nullable()
                .WithColumn(nameof(CatalogSource.DataTypes)).AsString(500).Nullable()
                .WithColumn(nameof(CatalogSource.TermsUrl)).AsString(500).Nullable()
                .WithColumn(nameof(CatalogSource.TermsSummary)).AsMaxString().Nullable()
                .WithColumn(nameof(CatalogSource.ImageRights)).AsString(1000).Nullable()
                .WithColumn(nameof(CatalogSource.ImportReadiness)).AsString(50).Nullable()
                .WithColumn(nameof(CatalogSource.Limitations)).AsMaxString().Nullable()
                .WithColumn(nameof(CatalogSource.ContactUrl)).AsString(500).Nullable()
                .WithColumn(nameof(CatalogSource.VerifiedOnUtc)).AsDateTime2().Nullable()
                .WithColumn(nameof(CatalogSource.UpdatedOnUtc)).AsDateTime2().NotNullable();

            Create.Index("IX_MotorCatalogSource_Key")
                .OnTable(SourceTable)
                .OnColumn(nameof(CatalogSource.Key)).Ascending()
                .WithOptions().Unique();
        }

        if (!Schema.Table(BatchTable).Exists())
        {
            Create.Table(BatchTable)
                .WithIdColumn()
                .WithColumn(nameof(CatalogImportBatch.SourceId)).AsInt32().NotNullable()
                    .ForeignKey("FK_MotorCatalogImportBatch_Source", SourceTable, Id).OnDelete(Rule.None)
                .WithColumn(nameof(CatalogImportBatch.Kind)).AsInt32().NotNullable()
                .WithColumn(nameof(CatalogImportBatch.FileName)).AsString(255).Nullable()
                .WithColumn(nameof(CatalogImportBatch.ContentHash)).AsString(64).NotNullable()
                .WithColumn(nameof(CatalogImportBatch.RawContent)).AsMaxString().Nullable()
                .WithColumn(nameof(CatalogImportBatch.IsFullSnapshot)).AsBoolean().NotNullable()
                .WithColumn(nameof(CatalogImportBatch.Status)).AsInt32().NotNullable()
                .WithColumn(nameof(CatalogImportBatch.StartedOnUtc)).AsDateTime2().NotNullable()
                .WithColumn(nameof(CatalogImportBatch.CompletedOnUtc)).AsDateTime2().Nullable()
                .WithColumn(nameof(CatalogImportBatch.TotalCount)).AsInt32().NotNullable()
                .WithColumn(nameof(CatalogImportBatch.CreatedCount)).AsInt32().NotNullable()
                .WithColumn(nameof(CatalogImportBatch.UpdatedCount)).AsInt32().NotNullable()
                .WithColumn(nameof(CatalogImportBatch.UnchangedCount)).AsInt32().NotNullable()
                .WithColumn(nameof(CatalogImportBatch.InvalidCount)).AsInt32().NotNullable()
                .WithColumn(nameof(CatalogImportBatch.DuplicateCount)).AsInt32().NotNullable()
                .WithColumn(nameof(CatalogImportBatch.MissingCount)).AsInt32().NotNullable()
                .WithColumn(nameof(CatalogImportBatch.IssuesJson)).AsMaxString().Nullable()
                .WithColumn(nameof(CatalogImportBatch.FailureReason)).AsString(1000).Nullable()
                .WithColumn(nameof(CatalogImportBatch.RetryOfBatchId)).AsInt32().Nullable();

            Create.Index("IX_MotorCatalogImportBatch_Source_Kind_Hash")
                .OnTable(BatchTable)
                .OnColumn(nameof(CatalogImportBatch.SourceId)).Ascending()
                .OnColumn(nameof(CatalogImportBatch.Kind)).Ascending()
                .OnColumn(nameof(CatalogImportBatch.ContentHash)).Ascending();
        }

        if (!Schema.Table(VehicleRefTable).Exists())
        {
            Create.Table(VehicleRefTable)
                .WithIdColumn()
                .WithColumn(nameof(CatalogVehicleReference.Make)).AsString(100).NotNullable()
                .WithColumn(nameof(CatalogVehicleReference.Model)).AsString(200).NotNullable()
                .WithColumn(nameof(CatalogVehicleReference.Variant)).AsString(100).Nullable()
                .WithColumn(nameof(CatalogVehicleReference.ModelCode)).AsString(100).Nullable()
                .WithColumn(nameof(CatalogVehicleReference.Generation)).AsString(200).Nullable()
                .WithColumn(nameof(CatalogVehicleReference.YearFrom)).AsInt32().Nullable()
                .WithColumn(nameof(CatalogVehicleReference.YearTo)).AsInt32().Nullable()
                .WithColumn(nameof(CatalogVehicleReference.VerifiedYears)).AsString(500).Nullable()
                .WithColumn(nameof(CatalogVehicleReference.EngineCc)).AsDecimal(7, 2).Nullable()
                .WithColumn(nameof(CatalogVehicleReference.EngineConfiguration)).AsString(200).Nullable()
                .WithColumn(nameof(CatalogVehicleReference.Transmission)).AsString(200).Nullable()
                .WithColumn(nameof(CatalogVehicleReference.Category)).AsString(50).Nullable()
                .WithColumn(nameof(CatalogVehicleReference.Market)).AsString(50).Nullable()
                .WithColumn(nameof(CatalogVehicleReference.MarketNotes)).AsString(1000).Nullable()
                .WithColumn(nameof(CatalogVehicleReference.SourcesJson)).AsMaxString().Nullable()
                .WithColumn(nameof(CatalogVehicleReference.UnknownFields)).AsString(500).Nullable()
                .WithColumn(nameof(CatalogVehicleReference.Confidence)).AsInt32().NotNullable()
                .WithColumn(nameof(CatalogVehicleReference.Status)).AsInt32().NotNullable()
                .WithColumn(nameof(CatalogVehicleReference.Notes)).AsMaxString().Nullable()
                .WithColumn(nameof(CatalogVehicleReference.SeedAudit)).AsString(1000).Nullable()
                .WithColumn(nameof(CatalogVehicleReference.NaturalKey)).AsString(450).NotNullable()
                .WithColumn(nameof(CatalogVehicleReference.ContentHash)).AsString(64).NotNullable()
                .WithColumn(nameof(CatalogVehicleReference.SourceId)).AsInt32().NotNullable()
                    .ForeignKey("FK_MotorCatalogVehicleReference_Source", SourceTable, Id).OnDelete(Rule.None)
                .WithColumn(nameof(CatalogVehicleReference.MotorVehicleId)).AsInt32().Nullable()
                    .ForeignKey("FK_MotorCatalogVehicleReference_MotorVehicle", MotorVehicleTable, Id).OnDelete(Rule.SetNull)
                .WithColumn(nameof(CatalogVehicleReference.HasPendingChanges)).AsBoolean().NotNullable()
                .WithColumn(nameof(CatalogVehicleReference.LastBatchId)).AsInt32().Nullable()
                .WithColumn(nameof(CatalogVehicleReference.VerifiedOnUtc)).AsDateTime2().Nullable()
                .WithColumn(nameof(CatalogVehicleReference.CreatedOnUtc)).AsDateTime2().NotNullable()
                .WithColumn(nameof(CatalogVehicleReference.UpdatedOnUtc)).AsDateTime2().NotNullable();

            Create.Index("IX_MotorCatalogVehicleReference_NaturalKey")
                .OnTable(VehicleRefTable)
                .OnColumn(nameof(CatalogVehicleReference.NaturalKey)).Ascending()
                .WithOptions().Unique();

            Create.Index("IX_MotorCatalogVehicleReference_MotorVehicle")
                .OnTable(VehicleRefTable)
                .OnColumn(nameof(CatalogVehicleReference.MotorVehicleId)).Ascending();

            Create.Index("IX_MotorCatalogVehicleReference_Source")
                .OnTable(VehicleRefTable)
                .OnColumn(nameof(CatalogVehicleReference.SourceId)).Ascending();
        }

        if (!Schema.Table(PartTable).Exists())
        {
            Create.Table(PartTable)
                .WithIdColumn()
                .WithColumn(nameof(CatalogPart.Brand)).AsString(100).NotNullable()
                .WithColumn(nameof(CatalogPart.BrandKey)).AsString(100).NotNullable()
                .WithColumn(nameof(CatalogPart.ProductName)).AsString(400).NotNullable()
                .WithColumn(nameof(CatalogPart.ProductType)).AsString(50).NotNullable()
                .WithColumn(nameof(CatalogPart.Mpn)).AsString(100).NotNullable()
                .WithColumn(nameof(CatalogPart.MpnKey)).AsString(100).NotNullable()
                .WithColumn(nameof(CatalogPart.SupersededBy)).AsString(100).Nullable()
                .WithColumn(nameof(CatalogPart.OemReferences)).AsString(1000).Nullable()
                .WithColumn(nameof(CatalogPart.Gtin)).AsString(50).Nullable()
                .WithColumn(nameof(CatalogPart.Classification)).AsInt32().NotNullable()
                .WithColumn(nameof(CatalogPart.Condition)).AsString(20).Nullable()
                .WithColumn(nameof(CatalogPart.SpecsJson)).AsMaxString().Nullable()
                .WithColumn(nameof(CatalogPart.PackageQuantity)).AsInt32().Nullable()
                .WithColumn(nameof(CatalogPart.SourceId)).AsInt32().NotNullable()
                    .ForeignKey("FK_MotorCatalogPart_Source", SourceTable, Id).OnDelete(Rule.None)
                .WithColumn(nameof(CatalogPart.SourceUrl)).AsString(500).Nullable()
                .WithColumn(nameof(CatalogPart.SourcesJson)).AsMaxString().Nullable()
                .WithColumn(nameof(CatalogPart.AccessedOn)).AsDateTime2().Nullable()
                .WithColumn(nameof(CatalogPart.RetrievalMethod)).AsString(100).Nullable()
                .WithColumn(nameof(CatalogPart.ImageRights)).AsString(1000).Nullable()
                .WithColumn(nameof(CatalogPart.CandidateImageUrls)).AsMaxString().Nullable()
                .WithColumn(nameof(CatalogPart.VerificationStatus)).AsInt32().NotNullable()
                .WithColumn(nameof(CatalogPart.Confidence)).AsInt32().NotNullable()
                .WithColumn(nameof(CatalogPart.MissingData)).AsString(500).Nullable()
                .WithColumn(nameof(CatalogPart.Notes)).AsMaxString().Nullable()
                .WithColumn(nameof(CatalogPart.State)).AsInt32().NotNullable()
                .WithColumn(nameof(CatalogPart.ContentHash)).AsString(64).NotNullable()
                .WithColumn(nameof(CatalogPart.PendingChangesJson)).AsMaxString().Nullable()
                .WithColumn(nameof(CatalogPart.HasPendingChanges)).AsBoolean().NotNullable()
                .WithColumn(nameof(CatalogPart.IsMissingFromSource)).AsBoolean().NotNullable()
                .WithColumn(nameof(CatalogPart.LastBatchId)).AsInt32().Nullable()
                .WithColumn(nameof(CatalogPart.ProductId)).AsInt32().Nullable()
                    .ForeignKey("FK_MotorCatalogPart_Product", ProductTable, Id).OnDelete(Rule.SetNull)
                .WithColumn(nameof(CatalogPart.CategoryId)).AsInt32().Nullable()
                .WithColumn(nameof(CatalogPart.ApprovedPrice)).AsDecimal(18, 4).Nullable()
                .WithColumn(nameof(CatalogPart.TaxCategoryId)).AsInt32().Nullable()
                .WithColumn(nameof(CatalogPart.ImageRightsApproved)).AsBoolean().NotNullable()
                .WithColumn(nameof(CatalogPart.ImageRightsNote)).AsString(1000).Nullable()
                .WithColumn(nameof(CatalogPart.ReferenceApprovedOnUtc)).AsDateTime2().Nullable()
                .WithColumn(nameof(CatalogPart.ReferenceApprovedBy)).AsString(200).Nullable()
                .WithColumn(nameof(CatalogPart.SaleApprovedOnUtc)).AsDateTime2().Nullable()
                .WithColumn(nameof(CatalogPart.SaleApprovedBy)).AsString(200).Nullable()
                .WithColumn(nameof(CatalogPart.RejectionReason)).AsString(1000).Nullable()
                .WithColumn(nameof(CatalogPart.FirstSeenOnUtc)).AsDateTime2().NotNullable()
                .WithColumn(nameof(CatalogPart.LastSeenOnUtc)).AsDateTime2().NotNullable()
                .WithColumn(nameof(CatalogPart.UpdatedOnUtc)).AsDateTime2().NotNullable();

            Create.Index("IX_MotorCatalogPart_Brand_Mpn")
                .OnTable(PartTable)
                .OnColumn(nameof(CatalogPart.BrandKey)).Ascending()
                .OnColumn(nameof(CatalogPart.MpnKey)).Ascending()
                .WithOptions().Unique();

            Create.Index("IX_MotorCatalogPart_Product")
                .OnTable(PartTable)
                .OnColumn(nameof(CatalogPart.ProductId)).Ascending();

            Create.Index("IX_MotorCatalogPart_State")
                .OnTable(PartTable)
                .OnColumn(nameof(CatalogPart.State)).Ascending();

            Create.Index("IX_MotorCatalogPart_Source")
                .OnTable(PartTable)
                .OnColumn(nameof(CatalogPart.SourceId)).Ascending();
        }

        if (!Schema.Table(FitmentTable).Exists())
        {
            Create.Table(FitmentTable)
                .WithIdColumn()
                .WithColumn(nameof(CatalogPartFitment.PartId)).AsInt32().NotNullable()
                    .ForeignKey("FK_MotorCatalogPartFitment_Part", PartTable, Id).OnDelete(Rule.Cascade)
                .WithColumn(nameof(CatalogPartFitment.FitmentKey)).AsString(450).NotNullable()
                .WithColumn(nameof(CatalogPartFitment.Make)).AsString(100).NotNullable()
                .WithColumn(nameof(CatalogPartFitment.Model)).AsString(200).NotNullable()
                .WithColumn(nameof(CatalogPartFitment.Variant)).AsString(100).Nullable()
                .WithColumn(nameof(CatalogPartFitment.YearFrom)).AsInt32().NotNullable()
                .WithColumn(nameof(CatalogPartFitment.YearTo)).AsInt32().Nullable()
                .WithColumn(nameof(CatalogPartFitment.Position)).AsString(50).Nullable()
                .WithColumn(nameof(CatalogPartFitment.SourceUrl)).AsString(500).NotNullable()
                .WithColumn(nameof(CatalogPartFitment.Notes)).AsString(1000).Nullable()
                .WithColumn(nameof(CatalogPartFitment.IsMissingFromSource)).AsBoolean().NotNullable();

            Create.Index("IX_MotorCatalogPartFitment_Part_Key")
                .OnTable(FitmentTable)
                .OnColumn(nameof(CatalogPartFitment.PartId)).Ascending()
                .OnColumn(nameof(CatalogPartFitment.FitmentKey)).Ascending()
                .WithOptions().Unique();
        }

        if (!Schema.Table(MatchTable).Exists())
        {
            // PartId is denormalized for queries; the FK chain Part -> Fitment -> Match already cascades,
            // so PartId gets no second cascading FK (SQL Server rejects multiple cascade paths).
            Create.Table(MatchTable)
                .WithIdColumn()
                .WithColumn(nameof(CatalogFitmentMatch.FitmentId)).AsInt32().NotNullable()
                    .ForeignKey("FK_MotorCatalogFitmentMatch_Fitment", FitmentTable, Id).OnDelete(Rule.Cascade)
                .WithColumn(nameof(CatalogFitmentMatch.PartId)).AsInt32().NotNullable()
                .WithColumn(nameof(CatalogFitmentMatch.MotorVehicleId)).AsInt32().NotNullable()
                    .ForeignKey("FK_MotorCatalogFitmentMatch_MotorVehicle", MotorVehicleTable, Id).OnDelete(Rule.Cascade)
                .WithColumn(nameof(CatalogFitmentMatch.Kind)).AsInt32().NotNullable()
                .WithColumn(nameof(CatalogFitmentMatch.Status)).AsInt32().NotNullable()
                .WithColumn(nameof(CatalogFitmentMatch.CreatedMapping)).AsBoolean().NotNullable()
                .WithColumn(nameof(CatalogFitmentMatch.ReviewedOnUtc)).AsDateTime2().Nullable()
                .WithColumn(nameof(CatalogFitmentMatch.ReviewedBy)).AsString(200).Nullable();

            Create.Index("IX_MotorCatalogFitmentMatch_Fitment_Vehicle")
                .OnTable(MatchTable)
                .OnColumn(nameof(CatalogFitmentMatch.FitmentId)).Ascending()
                .OnColumn(nameof(CatalogFitmentMatch.MotorVehicleId)).Ascending()
                .WithOptions().Unique();

            Create.Index("IX_MotorCatalogFitmentMatch_Vehicle")
                .OnTable(MatchTable)
                .OnColumn(nameof(CatalogFitmentMatch.MotorVehicleId)).Ascending();
        }

        if (!Schema.Table(OfferTable).Exists())
        {
            Create.Table(OfferTable)
                .WithIdColumn()
                .WithColumn(nameof(CatalogSupplierOffer.PartId)).AsInt32().NotNullable()
                    .ForeignKey("FK_MotorCatalogSupplierOffer_Part", PartTable, Id).OnDelete(Rule.Cascade)
                .WithColumn(nameof(CatalogSupplierOffer.SourceId)).AsInt32().NotNullable()
                    .ForeignKey("FK_MotorCatalogSupplierOffer_Source", SourceTable, Id).OnDelete(Rule.None)
                .WithColumn(nameof(CatalogSupplierOffer.SupplierSku)).AsString(100).NotNullable()
                .WithColumn(nameof(CatalogSupplierOffer.Currency)).AsString(3).NotNullable()
                .WithColumn(nameof(CatalogSupplierOffer.PurchaseCost)).AsDecimal(18, 4).Nullable()
                .WithColumn(nameof(CatalogSupplierOffer.StockQuantity)).AsInt32().Nullable()
                .WithColumn(nameof(CatalogSupplierOffer.LeadTimeDays)).AsInt32().Nullable()
                .WithColumn(nameof(CatalogSupplierOffer.VerifiedOnUtc)).AsDateTime2().NotNullable()
                .WithColumn(nameof(CatalogSupplierOffer.IsActive)).AsBoolean().NotNullable()
                .WithColumn(nameof(CatalogSupplierOffer.LastBatchId)).AsInt32().Nullable()
                .WithColumn(nameof(CatalogSupplierOffer.UpdatedOnUtc)).AsDateTime2().NotNullable();

            Create.Index("IX_MotorCatalogSupplierOffer_Source_Sku")
                .OnTable(OfferTable)
                .OnColumn(nameof(CatalogSupplierOffer.SourceId)).Ascending()
                .OnColumn(nameof(CatalogSupplierOffer.SupplierSku)).Ascending()
                .WithOptions().Unique();

            Create.Index("IX_MotorCatalogSupplierOffer_Part")
                .OnTable(OfferTable)
                .OnColumn(nameof(CatalogSupplierOffer.PartId)).Ascending();

            Create.Index("IX_MotorCatalogSupplierOffer_Source")
                .OnTable(OfferTable)
                .OnColumn(nameof(CatalogSupplierOffer.SourceId)).Ascending();
        }
    }

    public override void Down()
    {
        // INFO: no down initial migration. Leave schema as it is or ask merchant to delete it.
    }
}
