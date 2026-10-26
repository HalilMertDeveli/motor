using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using MotorShop.AdminPanel.Models;
using MotorShop.AdminPanel.Services;
using NUnit.Framework;
using Smartstore;
using Smartstore.Core;
using Smartstore.Core.Catalog.Categories;
using Smartstore.Core.Catalog.Products;
using Smartstore.Core.Checkout.Tax;
using Smartstore.Core.Content.Media;
using Smartstore.Core.Data;
using Smartstore.Core.Logging;
using Smartstore.Core.Seo;
using Smartstore.Data;
using Smartstore.Data.Providers;
using Smartstore.Test.Common;

namespace MotorShop.AdminPanel.Tests;

/// <summary>
/// The whole Smartstore model on a real SQLite database in memory (one per test): transactions, constraints and
/// SQL translation behave as in production. No engine runs, so Smartstore's save hooks do not either; what the
/// hooks add (audit dates, caches, the supplier sale guard) is covered by the runtime QA.
/// </summary>
public abstract class PanelDbTestBase
{
    private SqliteConnection _connection;

    protected SmartDbContext Db { get; private set; }

    [SetUp]
    public void CreateDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DataSettings.Instance = new DataSettings
        {
            AppVersion = SmartstoreVersion.Version,
            ConnectionString = "Data Source=:memory:",
            TenantName = "Default",
            DbFactory = new SqliteDbFactory(_connection)
        };
        DataSettings.SetTestMode(true);

        Db = NewContext();
        Db.Database.EnsureCreated();
    }

    [TearDown]
    public void DropDatabase()
    {
        Db?.Dispose();
        _connection?.Dispose();
        DataSettings.Reload();
    }

    /// <summary>
    /// A second context on the same database, e.g. to read what was really stored or to simulate another user.
    /// </summary>
    protected SmartDbContext NewContext()
    {
        var builder = new DbContextOptionsBuilder<SmartDbContext>()
            .UseDbFactory(b => b.AddModelAssemblies([typeof(SmartDbContext).Assembly]));

        return new SmartDbContext((DbContextOptions<SmartDbContext>)builder.Options);
    }

    protected async Task<Category> AddCategoryAsync(string name, int? parentId = null, bool deleted = false)
    {
        var category = new Category { Name = name, ParentId = parentId, Published = true, Deleted = deleted, CreatedOnUtc = DateTime.UtcNow, UpdatedOnUtc = DateTime.UtcNow };
        Db.Categories.Add(category);
        await Db.SaveChangesAsync();
        return category;
    }

    protected async Task<TaxCategory> AddTaxCategoryAsync(string name)
    {
        var tax = new TaxCategory { Name = name };
        Db.TaxCategories.Add(tax);
        await Db.SaveChangesAsync();
        return tax;
    }

    protected TestSimpleProductService CreateService(SubmissionRegistry registry = null, SmartDbContext db = null)
        => new(db ?? Db, registry ?? new SubmissionRegistry());

    protected static QuickProductModel Quick(string name, int categoryId, string price = "1.250,50", bool publish = false, string stock = "3")
        => new() { Name = name, CategoryId = categoryId, Price = price, Stock = stock, Publish = publish, SubmissionId = Guid.NewGuid().ToString("N") };

    // Minimal valid file headers.
    protected static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00];
    protected static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48];
    protected static readonly byte[] Pdf = "%PDF-1.7 fake"u8.ToArray();

    protected static IFormFile Photo(string fileName, byte[] content, string contentType = "image/jpeg", long? claimedLength = null)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, claimedLength ?? content.Length, "photos", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    protected static string Key(string key) => "Plugins.MotorShop.AdminPanel." + key;

    private sealed class SqliteDbFactory(SqliteConnection connection) : DbFactory
    {
        public override DbSystemType DbSystem { get; } = DbSystemType.SQLite;

        public override DbConnectionStringBuilder CreateConnectionStringBuilder(string connectionString)
            => new SqliteConnectionStringBuilder(connectionString);

        public override DbConnectionStringBuilder CreateConnectionStringBuilder(string server, string database, string userId, string password)
            => throw new NotSupportedException();

        public override DataProvider CreateDataProvider(DatabaseFacade database)
            => new TestDataProvider(database);

        public override TContext CreateDbContext<TContext>(string connectionString, int? commandTimeout = null)
            => (TContext)Activator.CreateInstance(typeof(TContext), [new DbContextOptionsBuilder<TContext>().UseSqlite(connection).Options]);

        public override DbContextOptionsBuilder ConfigureDbContext(DbContextOptionsBuilder builder, string connectionString)
            => builder.UseSqlite(connection);

        public override void ConfigureModelConventions(ModelConfigurationBuilder configurationBuilder)
        {
            // Same conventions as Smartstore.Data.Sqlite.
            configurationBuilder.Properties<decimal>().HaveConversion<double>();
            configurationBuilder.Properties<decimal?>().HaveConversion<double?>();
        }
    }
}

/// <summary>
/// The real service with slugs and media files kept in the test database instead of Smartstore's URL and media
/// services (which need the full engine). Everything else is the production code path.
/// </summary>
public class TestSimpleProductService : SimpleProductService
{
    private readonly SmartDbContext _db;

    public TestSimpleProductService(SmartDbContext db, SubmissionRegistry registry)
        : base(db, Mock.Of<IActivityLogger>(), Mock.Of<IUrlService>(), Mock.Of<IMediaService>(), registry)
    {
        _db = db;
    }

    public Dictionary<int, string> Slugs { get; } = [];
    public bool FailPhotoSave { get; set; }
    public int SlugCalls { get; private set; }

    protected override Task<string> SaveSlugAsync(Product product)
    {
        SlugCalls++;
        var slug = "urun-" + product.Id;
        Slugs[product.Id] = slug;
        return Task.FromResult(slug);
    }

    protected override Task<string> GetSlugAsync(int productId)
        => Task.FromResult(Slugs.GetValueOrDefault(productId));

    protected override async Task<int> SavePhotoFileAsync(IFormFile photo, string fileName)
    {
        if (FailPhotoSave)
        {
            throw new IOException("Disk full.");
        }

        var file = new MediaFile
        {
            Name = fileName,
            Extension = Path.GetExtension(fileName).TrimStart('.'),
            MimeType = photo.ContentType,
            MediaType = "image",
            Size = (int)photo.Length,
            CreatedOnUtc = DateTime.UtcNow,
            UpdatedOnUtc = DateTime.UtcNow
        };
        _db.MediaFiles.Add(file);
        await _db.SaveChangesAsync();
        return file.Id;
    }

    protected override async Task<List<ProductPhotoModel>> GetPhotosAsync(int productId)
    {
        var rows = await _db.ProductMediaFiles
            .AsNoTracking()
            .Where(x => x.ProductId == productId)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Id)
            .Select(x => new { x.MediaFileId, x.MediaFile.Name })
            .ToListAsync();

        return [.. rows.Select(x => new ProductPhotoModel { FileId = x.MediaFileId, Name = x.Name, ThumbUrl = "/media/" + x.MediaFileId })];
    }
}
