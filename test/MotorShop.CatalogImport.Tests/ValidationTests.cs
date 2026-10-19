using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MotorShop.CatalogImport.Domain;
using MotorShop.CatalogImport.Hooks;
using MotorShop.CatalogImport.Import;
using MotorShop.CatalogImport.Services;
using NUnit.Framework;

namespace MotorShop.CatalogImport.Tests;

/// <summary>
/// Pure validation, normalization and parsing rules. All records are fixtures with fictitious part numbers
/// and example.org URLs; they are not catalog data.
/// </summary>
[TestFixture]
public class ValidationTests
{
    static readonly DateTime Now = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

    static SourceRef Official => new() { Url = "https://fixture.example.org/spec", Organization = "Fixture OEM", Type = "OFFICIAL_MANUFACTURER", Accessed = "2026-10-19" };

    static VehicleRecord Vehicle(Action<VehicleRecord> configure = null)
    {
        var r = new VehicleRecord
        {
            Make = "Fixturemoto",
            Model = "FX 250",
            YearFrom = 2019,
            VerifiedYears = [2019, 2021],
            EngineCc = 249,
            Category = "sport",
            Market = "TR",
            Confidence = "HIGH",
            Sources = [Official]
        };
        configure?.Invoke(r);
        return r;
    }

    static PartRecord Part(Action<PartRecord> configure = null)
    {
        var r = new PartRecord
        {
            Brand = "Fixturebrand",
            ProductName = "FX-100 Oil Filter",
            ProductType = "oil filter",
            Mpn = "FX-100",
            Classification = "AFTERMARKET",
            VerificationStatus = "VERIFIED",
            Confidence = "HIGH",
            Sources = [Official],
            Fitment = [new FitmentRecord { Make = "Fixturemoto", Model = "FX 250", YearFrom = 2019, YearTo = 2022, SourceUrl = "https://fixture.example.org/fit" }]
        };
        configure?.Invoke(r);
        return r;
    }

    #region Normalizer

    [TestCase("hiflo", "Hiflofiltro")]
    [TestCase("HiFlo Filtro", "Hiflofiltro")]
    [TestCase("EBC", "EBC Brakes")]
    [TestCase("Niterra", "NGK")]
    [TestCase("d.i.d", "D.I.D")]
    [TestCase("Some  New   Brand", "Some New Brand")]
    public void Brand_aliases_are_canonical(string input, string expected)
        => Assert.That(CatalogNormalizer.Brand(input), Is.EqualTo(expected));

    [Test]
    public void Mpn_key_ignores_separators_but_display_value_is_untouched()
    {
        Assert.That(CatalogNormalizer.MpnKey("hf-204"), Is.EqualTo(CatalogNormalizer.MpnKey("HF 204")));
        Assert.That(CatalogNormalizer.MpnKey("CR8E"), Is.Not.EqualTo(CatalogNormalizer.MpnKey("CR8EH")));

        var result = CatalogValidator.ValidatePart(Part(x => x.Mpn = " fx-100/a "), 1, SourcePermissionStatus.PublicFactualReferenceOnly, Now);
        Assert.That(result.Record.Mpn, Is.EqualTo("fx-100/a"));
    }

    [Test]
    public void Model_key_equates_spelling_variants_only()
    {
        Assert.That(CatalogNormalizer.ModelKey("YZF-R25"), Is.EqualTo(CatalogNormalizer.ModelKey("yzf r25")));
        Assert.That(CatalogNormalizer.ModelKey("NC750X"), Is.Not.EqualTo(CatalogNormalizer.ModelKey("NC750S")));
        Assert.That(CatalogNormalizer.ModelKey("Ninja 400"), Is.Not.EqualTo(CatalogNormalizer.ModelKey("Ninja 400 ABS")));
    }

    [TestCase("oil filter", "oil-filter")]
    [TestCase("Fren Balatası", "brake-pad")]
    [TestCase("buji", "spark-plug")]
    [TestCase("exhaust", null)]
    public void Product_types_are_normalized(string input, string expected)
        => Assert.That(CatalogNormalizer.ProductType(input), Is.EqualTo(expected));

    [TestCase("4006381333931", true)]
    [TestCase("4006381333932", false)]
    [TestCase("12345", false)]
    [TestCase("96385074", true)]
    public void Gtin_check_digit(string gtin, bool valid)
        => Assert.That(CatalogValidator.IsValidGtin(gtin), Is.EqualTo(valid));

    #endregion

    #region Vehicles

    [Test]
    public void Valid_vehicle_is_normalized()
    {
        var result = CatalogValidator.ValidateVehicle(Vehicle(x => { x.Make = "cf moto"; x.Model = "  FX   250 "; }), 1, Now);

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Record.Make, Is.EqualTo("CFMOTO"));
        Assert.That(result.Record.Model, Is.EqualTo("FX 250"));
        Assert.That(result.Record.Confidence, Is.EqualTo("High"));
    }

    [Test]
    public void Reversed_year_range_is_rejected()
    {
        var result = CatalogValidator.ValidateVehicle(Vehicle(x => { x.YearFrom = 2022; x.YearTo = 2019; x.VerifiedYears = []; }), 1, Now);
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Issues.Any(x => x.Field == "yearTo"));
    }

    [TestCase(1949)]
    [TestCase(2029)]
    public void Implausible_years_are_rejected(int year)
        => Assert.That(CatalogValidator.ValidateVehicle(Vehicle(x => { x.YearFrom = year; x.VerifiedYears = []; }), 1, Now).IsValid, Is.False);

    [Test]
    public void Verified_year_outside_range_is_rejected()
        => Assert.That(CatalogValidator.ValidateVehicle(Vehicle(x => { x.YearTo = 2020; x.VerifiedYears = [2021]; }), 1, Now).IsValid, Is.False);

    [Test]
    public void Vehicle_without_source_url_is_rejected()
        => Assert.That(CatalogValidator.ValidateVehicle(Vehicle(x => x.Sources = [new SourceRef { Url = "not a url" }]), 1, Now).IsValid, Is.False);

    [TestCase(10)]
    [TestCase(5000)]
    public void Implausible_displacement_is_rejected(int cc)
        => Assert.That(CatalogValidator.ValidateVehicle(Vehicle(x => x.EngineCc = cc), 1, Now).IsValid, Is.False);

    [Test]
    public void High_confidence_without_official_source_is_lowered()
    {
        var result = CatalogValidator.ValidateVehicle(Vehicle(x => x.Sources = [new SourceRef { Url = "https://fixture.example.org/wiki", Type = "SECONDARY" }]), 1, Now);
        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Record.Confidence, Is.EqualTo("Medium"));
    }

    [Test]
    public void Vehicle_without_any_year_is_kept_with_warning()
    {
        var result = CatalogValidator.ValidateVehicle(Vehicle(x => { x.YearFrom = null; x.VerifiedYears = []; }), 1, Now);
        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Issues.Single().Severity, Is.EqualTo(IssueSeverity.Warning));
    }

    #endregion

    #region Parts

    [Test]
    public void Part_without_mpn_is_rejected()
        => Assert.That(CatalogValidator.ValidatePart(Part(x => x.Mpn = " "), 1, SourcePermissionStatus.PublicFactualReferenceOnly, Now).IsValid, Is.False);

    [Test]
    public void Part_with_unsupported_type_is_rejected()
        => Assert.That(CatalogValidator.ValidatePart(Part(x => x.ProductType = "helmet"), 1, SourcePermissionStatus.PublicFactualReferenceOnly, Now).IsValid, Is.False);

    [Test]
    public void Invalid_gtin_is_dropped_not_guessed()
    {
        var result = CatalogValidator.ValidatePart(Part(x => x.Gtin = "4006381333932"), 1, SourcePermissionStatus.PublicFactualReferenceOnly, Now);
        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Record.Gtin, Is.Null);
        Assert.That(result.Issues.Any(x => x.Field == "gtin" && x.Severity == IssueSeverity.Warning));
    }

    [Test]
    public void Fitment_lines_without_evidence_or_valid_years_are_dropped()
    {
        var result = CatalogValidator.ValidatePart(Part(x => x.Fitment =
        [
            new FitmentRecord { Make = "Fixturemoto", Model = "FX 250", YearFrom = 2019, YearTo = 2022, SourceUrl = "https://fixture.example.org/fit" },
            new FitmentRecord { Make = "Fixturemoto", Model = "FX 250", YearFrom = 2019, YearTo = 2022 },
            new FitmentRecord { Make = "Fixturemoto", Model = "FX 300", YearFrom = 2022, YearTo = 2019, SourceUrl = "https://fixture.example.org/fit" },
            new FitmentRecord { Make = "Fixturemoto", Model = "FX 400", SourceUrl = "https://fixture.example.org/fit" }
        ]), 1, SourcePermissionStatus.PublicFactualReferenceOnly, Now);

        Assert.That(result.Record.Fitment, Has.Count.EqualTo(1));
        Assert.That(result.Issues.Count(x => x.Field == "fitment"), Is.EqualTo(3));
    }

    [Test]
    public void Images_are_dropped_unless_source_is_authorized()
    {
        var images = new List<string> { "https://fixture.example.org/img.jpg" };

        var publicResult = CatalogValidator.ValidatePart(Part(x => x.Images = images), 1, SourcePermissionStatus.PublicFactualReferenceOnly, Now);
        Assert.That(publicResult.Record.Images, Is.Empty);
        Assert.That(publicResult.Issues.Any(x => x.Field == "images"));

        var authorizedResult = CatalogValidator.ValidatePart(Part(x => x.Images = images), 1, SourcePermissionStatus.Authorized, Now);
        Assert.That(authorizedResult.Record.Images, Is.EqualTo(images));
    }

    [Test]
    public void Price_in_research_record_is_ignored()
    {
        var json = """[{ "brand": "Fixturebrand", "mpn": "FX-100", "productName": "FX-100 Oil Filter", "productType": "oil-filter", "price": 999.0, "stock": 12, "sources": [{ "url": "https://fixture.example.org/spec" }] }]""";
        var row = FeedReaders.ReadJson<PartRecord>(json).Single();

        var result = CatalogValidator.ValidatePart(row.Record, row.Row, SourcePermissionStatus.PublicFactualReferenceOnly, Now);

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Issues.Any(x => x.Field == "commercial"));
    }

    #endregion

    #region Readers

    [Test]
    public void Csv_parser_handles_quotes_and_line_breaks()
    {
        var lines = FeedReaders.ParseCsv("a,b\r\n\"x, y\",\"say \"\"hi\"\"\nnext\"\r\n", ',');
        Assert.That(lines, Has.Count.EqualTo(2));
        Assert.That(lines[1][0], Is.EqualTo("x, y"));
        Assert.That(lines[1][1], Is.EqualTo("say \"hi\"\nnext"));
    }

    [Test]
    public void Unclosed_quote_fails_the_file()
        => Assert.Throws<FeedFormatException>(() => FeedReaders.ParseCsv("a,b\n\"x,y", ','));

    [Test]
    public void Malformed_json_fails_without_echoing_content()
    {
        var ex = Assert.Throws<FeedFormatException>(() => FeedReaders.ReadJson<PartRecord>("[{ \"brand\": \"secret-value\" "));
        Assert.That(ex.Message, Does.Not.Contain("secret-value"));
    }

    [Test]
    public void Offer_csv_reads_semicolon_and_decimal_comma()
    {
        var rows = FeedReaders.ReadOffersCsv("brand;mpn;supplier_sku;currency;purchase_cost;stock_quantity;lead_time_days;verified_on\nFixturebrand;FX-100;S-1;TRY;12,50;7;;2026-10-18\n");
        var r = rows.Single().Record;
        Assert.That(r.PurchaseCost, Is.EqualTo(12.50m));
        Assert.That(r.StockQuantity, Is.EqualTo(7));
        Assert.That(r.LeadTimeDays, Is.Null);
        Assert.That(r.VerifiedOn, Is.EqualTo(new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    public void Parts_csv_reads_fitment_and_specs()
    {
        var csv = "brand,product_name,product_type,mpn,specs,fitment,source_url,verification_status,confidence\n" +
                  "Fixturebrand,FX-100 Oil Filter,oil-filter,FX-100,Thread=M20x1.5|Height=65 mm,Fixturemoto;FX 250;2019;2022|Fixturemoto;FX 300;2020;,https://fixture.example.org/fit,VERIFIED,HIGH\n";
        var r = FeedReaders.ReadPartsCsv(csv).Single().Record;

        Assert.That(r.Specs["Thread"], Is.EqualTo("M20x1.5"));
        Assert.That(r.Fitment, Has.Count.EqualTo(2));
        Assert.That(r.Fitment[1].YearTo, Is.Null);
        Assert.That(r.Fitment[1].SourceUrl, Is.EqualTo("https://fixture.example.org/fit"));
    }

    #endregion

    #region Matching rules

    [TestCase(2019, 2022, 2019, 2022, FitmentMatchKind.Exact)]
    [TestCase(2014, 2024, 2018, 2020, FitmentMatchKind.Exact)]
    [TestCase(2019, 2022, 2018, 2020, FitmentMatchKind.Partial)]
    [TestCase(2019, 2022, 2021, 2025, FitmentMatchKind.Partial)]
    [TestCase(2019, 2022, 2023, 2025, null)]
    public void Closed_ranges(int fitFrom, int fitTo, int vFrom, int vTo, FitmentMatchKind? expected)
        => Assert.That(FitmentMatcher.Classify(fitFrom, fitTo, vFrom, vTo, 2026), Is.EqualTo(expected));

    [Test]
    public void Open_range_covers_only_years_up_to_source_access()
    {
        // "2021 onwards", read in 2024: a 2021-2024 motorcycle is covered, a 2021-2026 one only partly.
        Assert.That(FitmentMatcher.Classify(2021, null, 2021, 2024, 2024), Is.EqualTo(FitmentMatchKind.Exact));
        Assert.That(FitmentMatcher.Classify(2021, null, 2021, 2026, 2024), Is.EqualTo(FitmentMatchKind.Partial));
        Assert.That(FitmentMatcher.Classify(2021, null, 2018, 2020, 2024), Is.Null);
    }

    [Test]
    public void Guard_blocks_buy_button_for_every_state_but_sale()
    {
        Assert.That(ProductSaleGuardHook.IsBlocked(PartPublicationState.ResearchCandidate), Is.True);
        Assert.That(ProductSaleGuardHook.IsBlocked(PartPublicationState.VerifiedReference), Is.True);
        Assert.That(ProductSaleGuardHook.IsBlocked(PartPublicationState.Rejected), Is.True);
        Assert.That(ProductSaleGuardHook.IsBlocked(PartPublicationState.ReadyForSale), Is.False);
    }

    #endregion

    [Test]
    public void Vehicle_natural_key_keeps_variants_and_generations_apart()
    {
        var baseKey = CatalogImportService.VehicleNaturalKey(Vehicle());
        Assert.That(CatalogImportService.VehicleNaturalKey(Vehicle(x => x.Model = "fx-250")), Is.EqualTo(baseKey));
        Assert.That(CatalogImportService.VehicleNaturalKey(Vehicle(x => x.Variant = "ABS")), Is.Not.EqualTo(baseKey));
        Assert.That(CatalogImportService.VehicleNaturalKey(Vehicle(x => x.YearFrom = 2023)), Is.Not.EqualTo(baseKey));
    }

    [Test]
    public void Selector_years_never_extend_beyond_evidence()
    {
        var reference = new CatalogVehicleReference { YearFrom = 2019, YearTo = null, VerifiedYears = "2019,2021" };
        Assert.That(VehicleReferenceService.SelectorYears(reference), Is.EqualTo((2019, 2021)));

        reference = new CatalogVehicleReference { YearFrom = null, YearTo = null, VerifiedYears = null };
        Assert.That(VehicleReferenceService.SelectorYears(reference), Is.Null);
    }

    [TestCase("TR", true)]
    [TestCase("EU", true)]
    [TestCase("global", true)]
    [TestCase("IN", false)]
    public void Only_selector_markets_are_publishable(string market, bool publishable)
    {
        var reference = new CatalogVehicleReference { Status = VehicleReferenceStatus.Verified, EngineCc = 199.5m, YearFrom = 2015, VerifiedYears = "2015,2025", Market = market };
        Assert.That(VehicleReferenceService.IsPublishable(reference), Is.EqualTo(publishable));
    }
}
