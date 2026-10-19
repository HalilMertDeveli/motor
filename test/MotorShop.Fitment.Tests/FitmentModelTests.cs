using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using MotorShop.Fitment.Domain;
using NUnit.Framework;
using Smartstore.Core.Catalog.Products;

namespace MotorShop.Fitment.Tests;

/// <summary>
/// App installation creates the schema from the EF model, so the model itself must carry
/// the constraints that the migrations declare.
/// </summary>
[TestFixture]
public class FitmentModelTests : FitmentTestBase
{
    [TestCase(typeof(Product), nameof(ProductMotorVehicle.ProductId), "FK_ProductMotorVehicle_Product")]
    [TestCase(typeof(MotorVehicle), nameof(ProductMotorVehicle.MotorVehicleId), "FK_ProductMotorVehicle_MotorVehicle")]
    public void Mapping_has_cascading_foreign_key(System.Type principal, string column, string constraintName)
    {
        var fk = DbContext.Model.FindEntityType(typeof(ProductMotorVehicle))
            .GetForeignKeys()
            .SingleOrDefault(x => x.PrincipalEntityType.ClrType == principal);

        Assert.That(fk, Is.Not.Null);
        Assert.That(fk.Properties.Select(x => x.Name), Is.EqualTo(new[] { column }));
        Assert.That(fk.IsRequired, Is.True);
        Assert.That(fk.DeleteBehavior, Is.EqualTo(DeleteBehavior.Cascade));
        Assert.That(fk.GetConstraintName(), Is.EqualTo(constraintName));
    }

    [Test]
    public void Mapping_has_unique_product_vehicle_index()
    {
        var index = DbContext.Model.FindEntityType(typeof(ProductMotorVehicle))
            .GetIndexes()
            .SingleOrDefault(x => x.GetDatabaseName() == "IX_ProductMotorVehicle_Product_Vehicle");

        Assert.That(index, Is.Not.Null);
        Assert.That(index.IsUnique, Is.True);
        Assert.That(index.Properties.Select(x => x.Name), Is.EqualTo(new[] { nameof(ProductMotorVehicle.ProductId), nameof(ProductMotorVehicle.MotorVehicleId) }));
    }
}
