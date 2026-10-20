using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MotorShop.Fitment.Domain;
using Smartstore.Core.Catalog.Products;

namespace MotorShop.CatalogImport.Domain;

// The relationships must be part of the EF model: on app installation the schema is created from the model and
// all migrations, including this module's, are only marked as applied. Constraint names match the Initial migration.

internal class CatalogImportBatchMap : IEntityTypeConfiguration<CatalogImportBatch>
{
    public void Configure(EntityTypeBuilder<CatalogImportBatch> builder)
    {
        builder.HasOne<CatalogSource>().WithMany().HasForeignKey(x => x.SourceId)
            .HasConstraintName("FK_MotorCatalogImportBatch_Source").OnDelete(DeleteBehavior.NoAction);
    }
}

internal class CatalogVehicleReferenceMap : IEntityTypeConfiguration<CatalogVehicleReference>
{
    public void Configure(EntityTypeBuilder<CatalogVehicleReference> builder)
    {
        builder.HasOne<CatalogSource>().WithMany().HasForeignKey(x => x.SourceId)
            .HasConstraintName("FK_MotorCatalogVehicleReference_Source").OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<MotorVehicle>().WithMany().HasForeignKey(x => x.MotorVehicleId)
            .HasConstraintName("FK_MotorCatalogVehicleReference_MotorVehicle").OnDelete(DeleteBehavior.SetNull);
    }
}

internal class CatalogPartMap : IEntityTypeConfiguration<CatalogPart>
{
    public void Configure(EntityTypeBuilder<CatalogPart> builder)
    {
        builder.HasOne<CatalogSource>().WithMany().HasForeignKey(x => x.SourceId)
            .HasConstraintName("FK_MotorCatalogPart_Source").OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId)
            .HasConstraintName("FK_MotorCatalogPart_Product").OnDelete(DeleteBehavior.SetNull);
    }
}

internal class CatalogPartFitmentMap : IEntityTypeConfiguration<CatalogPartFitment>
{
    public void Configure(EntityTypeBuilder<CatalogPartFitment> builder)
    {
        builder.HasOne<CatalogPart>().WithMany().HasForeignKey(x => x.PartId)
            .HasConstraintName("FK_MotorCatalogPartFitment_Part").OnDelete(DeleteBehavior.Cascade);
    }
}

internal class CatalogFitmentMatchMap : IEntityTypeConfiguration<CatalogFitmentMatch>
{
    public void Configure(EntityTypeBuilder<CatalogFitmentMatch> builder)
    {
        builder.HasOne<CatalogPartFitment>().WithMany().HasForeignKey(x => x.FitmentId)
            .HasConstraintName("FK_MotorCatalogFitmentMatch_Fitment").OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<MotorVehicle>().WithMany().HasForeignKey(x => x.MotorVehicleId)
            .HasConstraintName("FK_MotorCatalogFitmentMatch_MotorVehicle").OnDelete(DeleteBehavior.Cascade);
    }
}

internal class CatalogSupplierOfferMap : IEntityTypeConfiguration<CatalogSupplierOffer>
{
    public void Configure(EntityTypeBuilder<CatalogSupplierOffer> builder)
    {
        builder.HasOne<CatalogPart>().WithMany().HasForeignKey(x => x.PartId)
            .HasConstraintName("FK_MotorCatalogSupplierOffer_Part").OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<CatalogSource>().WithMany().HasForeignKey(x => x.SourceId)
            .HasConstraintName("FK_MotorCatalogSupplierOffer_Source").OnDelete(DeleteBehavior.NoAction);
    }
}
