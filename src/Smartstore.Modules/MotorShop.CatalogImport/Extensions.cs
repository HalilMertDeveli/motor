using Microsoft.EntityFrameworkCore;
using MotorShop.CatalogImport.Domain;
using Smartstore.Core.Data;

namespace MotorShop.CatalogImport;

public static class SmartDbContextExtensions
{
    public static DbSet<CatalogSource> CatalogSources(this SmartDbContext db)
        => db.Set<CatalogSource>();

    public static DbSet<CatalogImportBatch> CatalogImportBatches(this SmartDbContext db)
        => db.Set<CatalogImportBatch>();

    public static DbSet<CatalogVehicleReference> CatalogVehicleReferences(this SmartDbContext db)
        => db.Set<CatalogVehicleReference>();

    public static DbSet<CatalogPart> CatalogParts(this SmartDbContext db)
        => db.Set<CatalogPart>();

    public static DbSet<CatalogPartFitment> CatalogPartFitments(this SmartDbContext db)
        => db.Set<CatalogPartFitment>();

    public static DbSet<CatalogFitmentMatch> CatalogFitmentMatches(this SmartDbContext db)
        => db.Set<CatalogFitmentMatch>();

    public static DbSet<CatalogSupplierOffer> CatalogSupplierOffers(this SmartDbContext db)
        => db.Set<CatalogSupplierOffer>();
}
