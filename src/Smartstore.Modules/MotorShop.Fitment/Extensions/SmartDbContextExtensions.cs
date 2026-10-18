using Microsoft.EntityFrameworkCore;
using MotorShop.Fitment.Domain;
using Smartstore.Core.Data;

namespace MotorShop.Fitment;

public static class SmartDbContextExtensions
{
    public static DbSet<MotorVehicle> MotorVehicles(this SmartDbContext db)
        => db.Set<MotorVehicle>();

    public static DbSet<ProductMotorVehicle> ProductMotorVehicles(this SmartDbContext db)
        => db.Set<ProductMotorVehicle>();
}
