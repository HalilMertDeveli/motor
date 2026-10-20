using System.IO;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MotorShop.CatalogImport.Domain;
using MotorShop.CatalogImport.Import;
using Smartstore;
using Smartstore.Core.Data;

namespace MotorShop.CatalogImport.Services;

public record BundledLoadResult(int SourcesAdded, StageResult Vehicles, StageResult Parts);

/// <summary>
/// The research datasets of the module (Research/2026-10): the source register, verified motorcycle
/// reference records and spare-part research candidates. Loading them only stages research data; nothing becomes
/// a selectable motorcycle or a product without the normal admin review.
/// The datasets are embedded only when the module is built with IncludeCatalogResearch=true: their part fitment
/// comes from catalogues that require permission, so release and Docker builds leave them out by default.
/// </summary>
public class BundledResearchService
{
    public const string Edition = "2026-10";
    public const string DatasetSourceKey = "motor-research-2026-10";

    const string ResourcePrefix = "MotorShop.CatalogImport.Research.";
    static readonly string[] DatasetFiles = ["sources.json", "vehicles.json", "parts.json"];

    private readonly SmartDbContext _db;
    private readonly CatalogImportService _importService;

    public BundledResearchService(SmartDbContext db, CatalogImportService importService)
    {
        _db = db;
        _importService = importService;
    }

    /// <summary>
    /// True when this build embeds the research datasets.
    /// </summary>
    public static bool IsBundled
        => DatasetFiles.All(x => typeof(BundledResearchService).Assembly.GetManifestResourceInfo(ResourcePrefix + x) != null);

    /// <summary>
    /// Reads a dataset by file name instead of the embedded resources (tests read the files from the repository).
    /// </summary>
    public Func<string, string> DatasetReader { get; set; }

    /// <summary>
    /// False in builds without the research datasets: the admin action is not offered then.
    /// </summary>
    public bool IsAvailable
        => DatasetReader != null || IsBundled;

    public static string ReadDataset(string fileName)
    {
        var assembly = typeof(BundledResearchService).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourcePrefix + fileName)
            ?? throw new FileNotFoundException($"Dataset '{fileName}' is not part of this build (build with IncludeCatalogResearch=true).");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private string Read(string fileName)
        => DatasetReader != null ? DatasetReader(fileName) : ReadDataset(fileName);

    public class SourceRecord
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string Url { get; set; }
        public string Organization { get; set; }
        public string SourceType { get; set; }
        public List<string> DataTypes { get; set; } = [];
        public string AccessMethod { get; set; }
        public string PermissionStatus { get; set; }
        public string TermsUrl { get; set; }
        public string TermsSummary { get; set; }
        public string ImageRights { get; set; }
        public string ImportReadiness { get; set; }
        public string Limitations { get; set; }
        public string ContactForPermission { get; set; }
        public string Accessed { get; set; }
    }

    public static List<SourceRecord> ParseSources(string json)
        => JsonSerializer.Deserialize<List<SourceRecord>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    /// <summary>
    /// Adds the sources of the register that do not exist yet. Existing sources are never changed:
    /// their permission status may have been set by an admin since.
    /// </summary>
    public async Task<int> LoadSourcesAsync(CancellationToken cancelToken = default)
    {
        EnsureAvailable();

        var existing = await _db.CatalogSources().Select(x => x.Key).ToListAsync(cancelToken);
        var keys = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;
        var added = 0;

        foreach (var s in ParseSources(Read("sources.json")))
        {
            var permission = CatalogValidator.ParsePermission(s.PermissionStatus);
            if (permission == null || !s.Key.HasValue() || !keys.Add(s.Key))
            {
                continue;
            }

            _db.CatalogSources().Add(new CatalogSource
            {
                Key = s.Key.Truncate(100),
                Name = s.Name.Truncate(200),
                // Rendered as links in the admin: only http(s) URLs are kept.
                Url = CatalogValidator.IsHttpUrl(s.Url) ? s.Url.Trim().Truncate(500) : null,
                Organization = s.Organization.Truncate(200),
                SourceType = s.SourceType.Truncate(50),
                // A register entry is research, never an agreement.
                PermissionStatus = permission == SourcePermissionStatus.Authorized ? SourcePermissionStatus.PermissionRequired : permission.Value,
                AccessMethod = s.AccessMethod.Truncate(200),
                DataTypes = string.Join(", ", s.DataTypes ?? []).Truncate(500),
                TermsUrl = CatalogValidator.IsHttpUrl(s.TermsUrl) ? s.TermsUrl.Trim().Truncate(500) : null,
                TermsSummary = s.TermsSummary,
                ImageRights = s.ImageRights.Truncate(1000),
                ImportReadiness = s.ImportReadiness.Truncate(50),
                Limitations = s.Limitations,
                ContactUrl = s.ContactForPermission.Truncate(500),
                VerifiedOnUtc = DateTime.TryParse(s.Accessed, out var accessed) ? DateTime.SpecifyKind(accessed, DateTimeKind.Utc) : null,
                UpdatedOnUtc = now
            });
            added++;
        }

        if (!keys.Contains(DatasetSourceKey))
        {
            _db.CatalogSources().Add(new CatalogSource
            {
                Key = DatasetSourceKey,
                Name = $"MOTOR research dataset {Edition} (motorcycle facts)",
                Organization = "MOTOR",
                SourceType = "OTHER",
                PermissionStatus = SourcePermissionStatus.PublicFactualReferenceOnly,
                AccessMethod = "Manual verification of official manufacturer and distributor pages, one page at a time",
                DataTypes = "make, model, generation, model years, displacement",
                Limitations = "Facts for the motorcycle selector only. Every record lists its official sources. No images, spec tables or texts were copied.",
                VerifiedOnUtc = new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc),
                UpdatedOnUtc = now
            });
            added++;
        }

        await _db.SaveChangesAsync(cancelToken);
        return added;
    }

    /// <summary>
    /// Adds missing sources, then stages the bundled motorcycles and parts. Safe to repeat: an unchanged dataset
    /// is recognized by its content hash and nothing is staged twice.
    /// </summary>
    public async Task<BundledLoadResult> LoadAsync(CancellationToken cancelToken = default)
    {
        EnsureAvailable();

        var added = await LoadSourcesAsync(cancelToken);
        var datasetSource = await _db.CatalogSources().FirstAsync(x => x.Key == DatasetSourceKey, cancelToken);

        // Every record names its own source (sourceKey); its permission, not the dataset's, governs it.
        // A motorcycle whose only source is RESTRICTED is therefore not staged at all.
        var vehicles = await _importService.StageAsync(datasetSource.Id, CatalogImportKind.Vehicles, $"research-{Edition}-vehicles.json", Read("vehicles.json"), cancelToken: cancelToken);
        var parts = await _importService.StageAsync(datasetSource.Id, CatalogImportKind.Parts, $"research-{Edition}-parts.json", Read("parts.json"), cancelToken: cancelToken);

        return new BundledLoadResult(added, vehicles, parts);
    }

    private void EnsureAvailable()
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("The research datasets are not part of this build.");
        }
    }
}
