using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Smartstore.Data;
using Smartstore.Data.Providers;
using Smartstore.Test.Common;

namespace MotorShop.CatalogImport.Tests;

/// <summary>
/// A real relational database (SQLite in memory) for the whole Smartstore model. Unlike EF InMemory it has
/// transactions, rollbacks and constraints, so failure paths behave as in production.
/// The connection stays open for the fixture: an in-memory SQLite database lives as long as its connection.
/// </summary>
internal sealed class SqliteTestDbFactory(SqliteConnection connection) : DbFactory
{
    public override DbSystemType DbSystem { get; } = DbSystemType.SQLite;

    public override DbConnectionStringBuilder CreateConnectionStringBuilder(string connectionString)
        => new SqliteConnectionStringBuilder(connectionString);

    public override DbConnectionStringBuilder CreateConnectionStringBuilder(string server, string database, string userId, string password)
        => throw new NotSupportedException();

    public override DataProvider CreateDataProvider(DatabaseFacade database)
        => new TestDataProvider(database);

    public override TContext CreateDbContext<TContext>(string connectionString, int? commandTimeout = null)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TContext>().UseSqlite(connection);
        return (TContext)Activator.CreateInstance(typeof(TContext), [optionsBuilder.Options]);
    }

    public override DbContextOptionsBuilder ConfigureDbContext(DbContextOptionsBuilder builder, string connectionString)
        => builder.UseSqlite(connection);

    public override void ConfigureModelConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Same conventions as Smartstore.Data.Sqlite.
        configurationBuilder.Properties<decimal>().HaveConversion<double>();
        configurationBuilder.Properties<decimal?>().HaveConversion<double?>();
    }
}

/// <summary>
/// Throws on the first SaveChanges for which <see cref="FailWhen"/> returns true, to simulate a failure at an exact point.
/// </summary>
public sealed class FailureInterceptor : SaveChangesInterceptor
{
    public Func<DbContext, bool> FailWhen { get; set; }

    public int Failures { get; private set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Check(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Check(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void Check(DbContext context)
    {
        if (context != null && FailWhen?.Invoke(context) == true)
        {
            FailWhen = null;
            Failures++;
            throw new InvalidOperationException("Injected test failure.");
        }
    }
}
