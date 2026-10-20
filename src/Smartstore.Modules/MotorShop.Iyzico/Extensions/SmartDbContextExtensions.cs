using Microsoft.EntityFrameworkCore;
using MotorShop.Iyzico.Domain;
using Smartstore.Core.Data;

namespace MotorShop.Iyzico;

public static class SmartDbContextExtensions
{
    public static DbSet<IyzicoTransaction> IyzicoTransactions(this SmartDbContext db)
        => db.Set<IyzicoTransaction>();
}
