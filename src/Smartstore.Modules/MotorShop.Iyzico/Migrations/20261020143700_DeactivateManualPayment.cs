using System.Threading;
using FluentMigrator;
using MotorShop.Iyzico.Services;
using Smartstore.Core.Data;
using Smartstore.Core.Data.Migrations;
using Smartstore.Data.Migrations;

namespace MotorShop.Iyzico.Migrations;

/// <summary>
/// Removes "Payments.Manual" (card numbers stored in the shop database) from the active payment methods of existing
/// databases. Fresh installs only stamp module migrations, so <see cref="Module.InstallAsync"/> does the same there.
/// No schema change.
/// </summary>
[MigrationVersion("2026-10-20 14:37:00", "MotorShop.Iyzico: Deactivate manual credit card payment")]
internal class DeactivateManualPayment : Migration, IDataSeeder<SmartDbContext>
{
    public DataSeederStage Stage => DataSeederStage.Early;
    public bool AbortOnFailure => false;

    public override void Up()
    {
    }

    public override void Down()
    {
    }

    public Task SeedAsync(SmartDbContext context, CancellationToken cancelToken = default)
        => BlockedPaymentMethods.DeactivateAsync(context, cancelToken);
}
