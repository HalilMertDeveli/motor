using FluentMigrator;
using MotorShop.Iyzico.Domain;
using Smartstore.Core.Data.Migrations;

namespace MotorShop.Iyzico.Migrations;

/// <summary>
/// Creates the transaction table. Uses provider-neutral FluentMigrator calls only, so it runs on
/// SQL Server, MySQL, PostgreSQL and SQLite.
/// </summary>
[MigrationVersion("2026-10-19 12:00:00", "MotorShop.Iyzico: Initial")]
internal class Initial : Migration
{
    const string TableName = "MotorShopIyzicoTransaction";

    public override void Up()
    {
        if (Schema.Table(TableName).Exists())
        {
            return;
        }

        Create.Table(TableName)
            .WithIdColumn()
            .WithColumn(nameof(IyzicoTransaction.OrderId)).AsInt32().NotNullable()
            .WithColumn(nameof(IyzicoTransaction.StoreId)).AsInt32().NotNullable()
            .WithColumn(nameof(IyzicoTransaction.Token)).AsString(200).NotNullable()
            .WithColumn(nameof(IyzicoTransaction.ConversationId)).AsString(64).NotNullable()
            .WithColumn(nameof(IyzicoTransaction.BasketId)).AsString(64).NotNullable()
            .WithColumn(nameof(IyzicoTransaction.PaymentId)).AsString(64).Nullable()
            .WithColumn(nameof(IyzicoTransaction.Status)).AsInt32().NotNullable()
            .WithColumn(nameof(IyzicoTransaction.Amount)).AsDecimal(18, 4).NotNullable()
            .WithColumn(nameof(IyzicoTransaction.Currency)).AsString(3).NotNullable()
            .WithColumn(nameof(IyzicoTransaction.PaidPrice)).AsDecimal(18, 4).Nullable()
            .WithColumn(nameof(IyzicoTransaction.Installment)).AsInt32().Nullable()
            .WithColumn(nameof(IyzicoTransaction.FraudStatus)).AsInt32().Nullable()
            .WithColumn(nameof(IyzicoTransaction.RefundedAmount)).AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
            .WithColumn(nameof(IyzicoTransaction.ItemTransactionsJson)).AsMaxString().Nullable()
            .WithColumn(nameof(IyzicoTransaction.LastErrorCode)).AsString(50).Nullable()
            .WithColumn(nameof(IyzicoTransaction.LastErrorMessage)).AsString(500).Nullable()
            .WithColumn(nameof(IyzicoTransaction.Version)).AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn(nameof(IyzicoTransaction.CreatedOnUtc)).AsDateTime2().NotNullable()
            .WithColumn(nameof(IyzicoTransaction.UpdatedOnUtc)).AsDateTime2().NotNullable()
            .WithColumn(nameof(IyzicoTransaction.PaidOnUtc)).AsDateTime2().Nullable();

        // The unique token index makes "one stored attempt per token" a database guarantee.
        Create.Index("IX_MotorShopIyzicoTransaction_Token")
            .OnTable(TableName)
            .OnColumn(nameof(IyzicoTransaction.Token)).Ascending()
            .WithOptions().Unique();

        Create.Index("IX_MotorShopIyzicoTransaction_ConversationId")
            .OnTable(TableName)
            .OnColumn(nameof(IyzicoTransaction.ConversationId)).Ascending()
            .WithOptions().Unique();

        Create.Index("IX_MotorShopIyzicoTransaction_OrderId")
            .OnTable(TableName)
            .OnColumn(nameof(IyzicoTransaction.OrderId)).Ascending();

        // Not unique: PaymentId is NULL until paid, and SQL Server allows only one NULL in a unique index.
        Create.Index("IX_MotorShopIyzicoTransaction_PaymentId")
            .OnTable(TableName)
            .OnColumn(nameof(IyzicoTransaction.PaymentId)).Ascending();

        Create.Index("IX_MotorShopIyzicoTransaction_Status")
            .OnTable(TableName)
            .OnColumn(nameof(IyzicoTransaction.Status)).Ascending()
            .OnColumn(nameof(IyzicoTransaction.CreatedOnUtc)).Ascending();
    }

    public override void Down()
    {
        // INFO: payment records are kept. Drop the table manually if really needed.
    }
}
