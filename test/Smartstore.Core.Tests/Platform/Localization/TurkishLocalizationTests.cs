using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NUnit.Framework;
using Smartstore.Core.Common;
using Smartstore.Core.Data.Migrations;
using Smartstore.Core.Installation;

namespace Smartstore.Core.Tests.Platform.Localization;

/// <summary>
/// MOTOR: checks the Turkish (tr-TR) store defaults and that the Turkish resource files
/// stay in sync with their English sources (same keys, same placeholders, same Liquid tokens).
/// </summary>
[TestFixture]
public class TurkishLocalizationTests
{
    private static readonly Regex PlaceholderRegex = new(@"\{\d+(:[^}]*)?\}", RegexOptions.Compiled);
    private static readonly Regex LiquidRegex = new(@"\{\{[\s\S]*?\}\}|\{%[\s\S]*?%\}", RegexOptions.Compiled);
    private static readonly Regex MotorDarkKeyRegex = new(@"T\(""(Themes\.MotorDark\.[A-Za-z0-9.]+)""\)", RegexOptions.Compiled);

    private static string _repoRoot;

    private static string RepoRoot
    {
        get
        {
            if (_repoRoot == null)
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Smartstore.sln")))
                {
                    dir = dir.Parent;
                }

                _repoRoot = dir?.FullName ?? throw new DirectoryNotFoundException("Repository root (Smartstore.sln) not found.");
            }

            return _repoRoot;
        }
    }

    private static string WebPath(params string[] parts)
        => Path.Combine([RepoRoot, "src", "Smartstore.Web", .. parts]);

    #region Store defaults

    [Test]
    public void Provinces_Contain_All_81_Plate_Codes_Once()
    {
        var provinces = TurkishStoreDefaults.Provinces;
        var expectedCodes = Enumerable.Range(1, 81).Select(x => x.ToString("00"));

        Assert.That(provinces, Has.Length.EqualTo(81));
        Assert.That(provinces.Select(x => x.Code), Is.EquivalentTo(expectedCodes));
        Assert.That(provinces.Select(x => x.Name).Distinct().Count(), Is.EqualTo(81));
        Assert.That(provinces.Any(x => x.Code == "34" && x.Name == "İstanbul"), Is.True);
    }

    [Test]
    public void CreateProvinces_Keeps_Turkish_Alphabetical_Display_Order()
    {
        var states = TurkishStoreDefaults.CreateProvinces();

        Assert.That(states, Has.Count.EqualTo(81));
        Assert.That(states.All(x => x.Published), Is.True);
        Assert.That(states.Select(x => x.DisplayOrder), Is.EqualTo(Enumerable.Range(0, 81)));

        // Turkish alphabet: Ç after C, I before İ, Ş after S.
        int Order(string name) => states.Single(x => x.Name == name).DisplayOrder;
        Assert.That(Order("Bursa"), Is.LessThan(Order("Çanakkale")));
        Assert.That(Order("Çorum"), Is.LessThan(Order("Denizli")));
        Assert.That(Order("Isparta"), Is.LessThan(Order("İstanbul")));
        Assert.That(Order("Kırşehir"), Is.LessThan(Order("Kilis")));
        Assert.That(Order("Sivas"), Is.LessThan(Order("Şanlıurfa")));
    }

    [Test]
    public void Tax_Categories_Are_Configurable_Defaults_With_Standard_Rate_First()
    {
        var categories = TurkishStoreDefaults.TaxCategories;

        Assert.That(categories[0].Rate, Is.EqualTo(20M));
        Assert.That(categories.Select(x => x.Rate), Is.EqualTo(new[] { 20M, 10M, 1M, 0M }));
        Assert.That(categories.Select(x => x.Name).Distinct().Count(), Is.EqualTo(categories.Length));
    }

    [TestCase(1234.56, "1.234,56 ₺")]
    [TestCase(0.5, "0,50 ₺")]
    [TestCase(1234567.891, "1.234.567,89 ₺")]
    [TestCase(19.995, "20,00 ₺")]
    [TestCase(-49.9, "-49,90 ₺")]
    public void Lira_Is_Formatted_The_Turkish_Way(decimal amount, string expected)
    {
        var lira = new Currency
        {
            CurrencyCode = TurkishStoreDefaults.CurrencyCode,
            DisplayLocale = TurkishStoreDefaults.Culture,
            CustomFormatting = TurkishStoreDefaults.CurrencyFormatting,
            RoundNumDecimals = 2,
            MidpointRounding = CurrencyMidpointRounding.AwayFromZero
        };

        Assert.That(new Money(amount, lira).ToString(), Is.EqualTo(expected));
    }

    #endregion

    #region Resource files

    [Test]
    public void App_Language_Pack_Matches_English_Keys_And_Placeholders()
    {
        var en = LoadResources(WebPath("App_Data", "Localization", "App", "en", "all.smres.xml"));
        var tr = LoadResources(WebPath("App_Data", "Localization", "App", "tr", "all.smres.xml"));

        Assert.That(tr.Keys, Is.EquivalentTo(en.Keys));
        AssertSamePlaceholders(en, tr);

        Assert.That(File.ReadAllText(WebPath("App_Data", "Localization", "App", "tr", "head.txt")).Trim(),
            Is.EqualTo(File.ReadAllText(WebPath("App_Data", "Localization", "App", "en", "head.txt")).Trim()));

        // Spot checks: customer facing texts are Turkish.
        Assert.That(tr["ShoppingCart.AddToCart"], Is.EqualTo("Sepete ekle"));
        Assert.That(tr["Address.Fields.City"], Is.EqualTo("İlçe"));
        Assert.That(tr["Address.Fields.StateProvince"], Is.EqualTo("İl"));
    }

    [Test]
    public void Installation_Resources_Match_English_Keys()
    {
        var en = LoadResources(WebPath("App_Data", "Localization", "Installation", "installation.en.xml"));
        var tr = LoadResources(WebPath("App_Data", "Localization", "Installation", "installation.tr.xml"));

        Assert.That(tr.Keys, Is.EquivalentTo(en.Keys));
        AssertSamePlaceholders(en, tr);
    }

    [Test]
    public void Module_Resources_Match_English_Keys()
    {
        var files = Directory.GetFiles(Path.Combine(RepoRoot, "src", "Smartstore.Modules"), "resources.tr-tr.xml", SearchOption.AllDirectories);

        Assert.That(files, Is.Not.Empty);

        foreach (var trFile in files)
        {
            var enFile = Path.Combine(Path.GetDirectoryName(trFile), "resources.en-us.xml");
            var en = LoadResources(enFile);
            var tr = LoadResources(trFile);

            Assert.That(tr.Keys, Is.EquivalentTo(en.Keys), trFile);
            AssertSamePlaceholders(en, tr, trFile);
        }
    }

    [Test]
    public void Email_Templates_Match_English_Templates()
    {
        var enDir = WebPath("App_Data", "EmailTemplates", "en");
        var trDir = WebPath("App_Data", "EmailTemplates", "tr");

        var enNames = Directory.GetFiles(enDir, "*.xml").Select(Path.GetFileName).ToArray();
        var trNames = Directory.GetFiles(trDir, "*.xml").Select(Path.GetFileName).ToArray();

        // The message template loader takes all templates from the "tr" folder, so it must be complete.
        Assert.That(trNames, Is.EquivalentTo(enNames));

        foreach (var name in enNames)
        {
            var en = XDocument.Load(Path.Combine(enDir, name)).Root;
            var tr = XDocument.Load(Path.Combine(trDir, name)).Root;

            Assert.That(tr.Elements().Select(x => x.Name.LocalName), Is.EqualTo(en.Elements().Select(x => x.Name.LocalName)), name);
            Assert.That(tr.Element("ModelTypes")?.Value, Is.EqualTo(en.Element("ModelTypes")?.Value), name);

            foreach (var element in new[] { "To", "Subject", "Body" })
            {
                Assert.That(
                    LiquidTokens(tr.Element(element)?.Value),
                    Is.EqualTo(LiquidTokens(en.Element(element)?.Value)),
                    $"{name} <{element}>");
            }
        }
    }

    [Test]
    public void MotorDark_Views_Only_Use_Resources_With_Turkish_Values()
    {
        var builder = new LocaleResourcesBuilder();
        new MotorTurkishLocalization().MigrateLocaleResources(builder);
        var entries = builder.Build().ToList();

        var viewsDir = WebPath("Themes", "MotorDark", "Views");
        var usedKeys = Directory.GetFiles(viewsDir, "*.cshtml", SearchOption.AllDirectories)
            .SelectMany(x => MotorDarkKeyRegex.Matches(File.ReadAllText(x)).Select(m => m.Groups[1].Value))
            .Distinct()
            .ToList();

        Assert.That(usedKeys, Is.Not.Empty);

        foreach (var key in usedKeys)
        {
            Assert.That(entries.Any(x => x.Key == key && x.Lang == null && !string.IsNullOrEmpty(x.Value)), Is.True, $"{key}: English value missing");
            Assert.That(entries.Any(x => x.Key == key && x.Lang == "tr" && !string.IsNullOrEmpty(x.Value)), Is.True, $"{key}: Turkish value missing");
        }
    }

    #endregion

    #region Utilities

    private static Dictionary<string, string> LoadResources(string path)
    {
        Assert.That(File.Exists(path), Is.True, path);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var doc = XDocument.Load(path);

        foreach (var node in doc.Descendants("LocaleResource"))
        {
            // Nested resources (<Children>) are identified by their full path.
            var names = node.AncestorsAndSelf("LocaleResource")
                .Reverse()
                .Select(x => (string)x.Attribute("Name"));

            result[string.Join(".", names)] = node.Element("Value")?.Value ?? string.Empty;
        }

        return result;
    }

    private static void AssertSamePlaceholders(Dictionary<string, string> en, Dictionary<string, string> tr, string context = null)
    {
        var mismatches = en
            .Where(x => tr.TryGetValue(x.Key, out var trValue) && !Placeholders(x.Value).SequenceEqual(Placeholders(trValue)))
            .Select(x => x.Key)
            .ToList();

        Assert.That(mismatches, Is.Empty, context);
    }

    private static IEnumerable<string> Placeholders(string value)
        => PlaceholderRegex.Matches(value ?? string.Empty).Select(x => x.Value).Order();

    private static List<string> LiquidTokens(string value)
        => LiquidRegex.Matches(value ?? string.Empty).Select(x => x.Value).ToList();

    #endregion
}
