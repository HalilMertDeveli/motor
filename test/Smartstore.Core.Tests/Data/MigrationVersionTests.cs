using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Smartstore.Core.Tests.Data;

/// <summary>
/// MOTOR: migration versions are global across core and all modules (one __MigrationVersionInfo table).
/// Two migrations with the same [MigrationVersion] date make every fresh installation fail with
/// "UNIQUE constraint failed: __MigrationVersionInfo.Version". Scans the sources, because module
/// assemblies are not referenced by this test project.
/// </summary>
[TestFixture]
public class MigrationVersionTests
{
    private static readonly Regex VersionRegex = new(@"\[MigrationVersion\(""(?<date>[^""]+)""\s*,\s*""(?<desc>[^""]*)""", RegexOptions.Compiled);

    [Test]
    public void Migration_versions_are_unique_across_core_and_modules()
    {
        var src = Path.Combine(FindRepoRoot(), "src");
        var separator = Path.DirectorySeparatorChar;

        var migrations = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(x => !x.Contains($"{separator}bin{separator}") && !x.Contains($"{separator}obj{separator}"))
            .SelectMany(file => VersionRegex.Matches(File.ReadAllText(file)).Select(m => new
            {
                File = Path.GetRelativePath(src, file),
                Date = DateTime.Parse(m.Groups["date"].Value, CultureInfo.InvariantCulture),
                Description = m.Groups["desc"].Value
            }))
            .ToList();

        Assert.That(migrations, Is.Not.Empty, "No [MigrationVersion] attributes found; the source scan is broken.");

        var duplicates = migrations
            .GroupBy(x => x.Date)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key:yyyy-MM-dd HH:mm:ss}: " + string.Join(", ", g.Select(x => $"'{x.Description}' ({x.File})")))
            .ToList();

        Assert.That(duplicates, Is.Empty, "Duplicate migration versions:" + Environment.NewLine + string.Join(Environment.NewLine, duplicates));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Smartstore.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root (Smartstore.sln) not found.");
    }
}
