using System;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MotorShop.Iyzico.Domain;
using NUnit.Framework;

namespace MotorShop.Iyzico.Tests;

/// <summary>
/// A fresh Smartstore install creates tables from the EF model and only stamps module migrations as applied.
/// These tests build the table from the EF mapping alone on a real SQLite database, so the idempotency
/// constraints must come from the entity mapping, not from the FluentMigrator migration.
/// </summary>
[TestFixture]
public class IyzicoSchemaTests
{
    private sealed class SchemaContext(DbContextOptions<SchemaContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<IyzicoTransaction>();
    }

    private static (SqliteConnection, SchemaContext) Create()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var db = new SchemaContext(new DbContextOptionsBuilder<SchemaContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();

        return (connection, db);
    }

    private static IyzicoTransaction NewTx(string token, string conversationId) => new()
    {
        OrderId = 1,
        StoreId = 1,
        Token = token,
        ConversationId = conversationId,
        BasketId = Guid.NewGuid().ToString(),
        Currency = "TRY",
        Amount = 10m,
        CreatedOnUtc = DateTime.UtcNow,
        UpdatedOnUtc = DateTime.UtcNow
    };

    [Test]
    public void Model_declares_the_same_unique_indexes_as_the_migration()
    {
        var (connection, db) = Create();
        using (connection)
        using (db)
        {
            var entity = db.Model.FindEntityType(typeof(IyzicoTransaction));
            Assert.That(entity.GetTableName(), Is.EqualTo("MotorShopIyzicoTransaction"));

            var unique = entity.GetIndexes().Where(x => x.IsUnique).Select(x => x.GetDatabaseName()).OrderBy(x => x).ToArray();
            Assert.That(unique, Is.EqualTo(new[] { "IX_MotorShopIyzicoTransaction_ConversationId", "IX_MotorShopIyzicoTransaction_Token" }));

            Assert.That(entity.FindProperty(nameof(IyzicoTransaction.Token)).IsNullable, Is.False);
            Assert.That(entity.FindProperty(nameof(IyzicoTransaction.Token)).GetMaxLength(), Is.EqualTo(200));
            Assert.That(entity.FindProperty(nameof(IyzicoTransaction.Version)).IsConcurrencyToken, Is.True);
        }
    }

    [Test]
    public void Duplicate_token_is_rejected_by_the_database()
    {
        var (connection, db) = Create();
        using (connection)
        using (db)
        {
            db.Add(NewTx("tok-1", "conv-1"));
            db.SaveChanges();

            db.Add(NewTx("tok-1", "conv-2"));
            Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        }
    }

    [Test]
    public void Duplicate_conversation_id_is_rejected_by_the_database()
    {
        var (connection, db) = Create();
        using (connection)
        using (db)
        {
            db.Add(NewTx("tok-1", "conv-1"));
            db.SaveChanges();

            db.Add(NewTx("tok-2", "conv-1"));
            Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        }
    }

    [Test]
    public void Stale_update_is_rejected_by_concurrency_token()
    {
        var (connection, db) = Create();
        using (connection)
        using (db)
        {
            db.Add(NewTx("tok-1", "conv-1"));
            db.SaveChanges();

            using var other = new SchemaContext(new DbContextOptionsBuilder<SchemaContext>().UseSqlite(connection).Options);
            var mine = db.Set<IyzicoTransaction>().Single();
            var theirs = other.Set<IyzicoTransaction>().Single();

            theirs.Status = IyzicoTransactionStatus.Paid;
            theirs.Version++;
            other.SaveChanges();

            mine.Status = IyzicoTransactionStatus.Failed;
            mine.Version++;
            Assert.Throws<DbUpdateConcurrencyException>(() => db.SaveChanges());
        }
    }
}
