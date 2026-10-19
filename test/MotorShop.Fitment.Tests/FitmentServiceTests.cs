using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using Moq;
using MotorShop.Fitment.Domain;
using MotorShop.Fitment.Models;
using MotorShop.Fitment.Services;
using NUnit.Framework;

namespace MotorShop.Fitment.Tests;

[TestFixture]
public class FitmentServiceTests : FitmentTestBase
{
    const string CookieName = ".MotorShop.Vehicle";

    [SetUp]
    public async Task ClearData()
    {
        DbContext.ChangeTracker.Clear();
        DbContext.ProductMotorVehicles().RemoveRange(await DbContext.ProductMotorVehicles().ToListAsync());
        DbContext.MotorVehicles().RemoveRange(await DbContext.MotorVehicles().ToListAsync());
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    [Test]
    public void Model_contains_module_entities()
    {
        Assert.That(DbContext.Model.FindEntityType(typeof(MotorVehicle)), Is.Not.Null);
        Assert.That(DbContext.Model.FindEntityType(typeof(ProductMotorVehicle)), Is.Not.Null);
    }

    #region GetYearsAsync

    [Test]
    public async Task GetYears_expands_ranges_distinct_and_descending()
    {
        await AddVehicleAsync("Honda", "CBR 250R", 2011, 2013);
        await AddVehicleAsync("Honda", "CBR 250R", 2013, 2014);
        await AddVehicleAsync("Honda", "CBR 500R", 2019, 2020);
        await AddVehicleAsync("Yamaha", "CBR 250R", 2000, 2001);

        var years = await CreateService().GetYearsAsync("Honda", "CBR 250R");

        Assert.That(years, Is.EqualTo(new[] { 2014, 2013, 2012, 2011 }));
    }

    [Test]
    public async Task GetYears_single_year_range()
    {
        await AddVehicleAsync("Honda", "CBR 250R", 2015, 2015);

        var years = await CreateService().GetYearsAsync("Honda", "CBR 250R");

        Assert.That(years, Is.EqualTo(new[] { 2015 }));
    }

    [Test]
    public async Task GetYears_reversed_range_is_read_as_min_to_max()
    {
        await AddVehicleAsync("Honda", "CBR 250R", 2015, 2012);

        var years = await CreateService().GetYearsAsync("Honda", "CBR 250R");

        Assert.That(years, Is.EqualTo(new[] { 2015, 2014, 2013, 2012 }));
    }

    [TestCase(null, "CBR 250R")]
    [TestCase("Honda", null)]
    [TestCase(" ", "CBR 250R")]
    [TestCase("Honda", "")]
    public async Task GetYears_requires_make_and_model(string make, string model)
    {
        await AddVehicleAsync("Honda", "CBR 250R", 2011, 2013);

        var years = await CreateService().GetYearsAsync(make, model);

        Assert.That(years, Is.Empty);
    }

    #endregion

    #region GetFittingProductIdsAsync

    [Test]
    public async Task GetFittingProductIds_filters_by_make_only()
    {
        var data = await SeedCatalogAsync();

        var ids = await CreateService().GetFittingProductIdsAsync(new VehicleSelection("Honda", null, null));

        Assert.That(ids, Is.EquivalentTo(new[] { data.CbrPart, data.SharedPart, data.Cb650Part }));
    }

    [Test]
    public async Task GetFittingProductIds_filters_by_make_and_model()
    {
        var data = await SeedCatalogAsync();

        var ids = await CreateService().GetFittingProductIdsAsync(new VehicleSelection("Honda", "CBR 250R", null));

        Assert.That(ids, Is.EquivalentTo(new[] { data.CbrPart, data.SharedPart }));
    }

    [Test]
    public async Task GetFittingProductIds_returns_each_product_once()
    {
        var data = await SeedCatalogAsync();

        // SharedPart fits both CBR 250R generations.
        var ids = await CreateService().GetFittingProductIdsAsync(new VehicleSelection("Honda", "CBR 250R", null));

        Assert.That(ids.Count(x => x == data.SharedPart), Is.EqualTo(1));
    }

    // CBR 250R generations: 2011-2013 (CbrPart, SharedPart) and 2017-2019 (SharedPart).
    [TestCase(2010, false, false)]
    [TestCase(2011, true, true)]
    [TestCase(2013, true, true)]
    [TestCase(2014, false, false)]
    [TestCase(2016, false, false)]
    [TestCase(2017, false, true)]
    [TestCase(2019, false, true)]
    [TestCase(2020, false, false)]
    public async Task GetFittingProductIds_year_boundaries_are_inclusive(int year, bool expectCbrPart, bool expectSharedPart)
    {
        var data = await SeedCatalogAsync();

        var ids = await CreateService().GetFittingProductIdsAsync(new VehicleSelection("Honda", "CBR 250R", year));

        Assert.That(ids.Contains(data.CbrPart), Is.EqualTo(expectCbrPart), "CbrPart");
        Assert.That(ids.Contains(data.SharedPart), Is.EqualTo(expectSharedPart), "SharedPart");
        Assert.That(ids, Does.Not.Contain(data.Cb650Part));
        Assert.That(ids, Does.Not.Contain(data.YamahaPart));
    }

    [Test]
    public async Task GetFittingProductIds_empty_selection_returns_nothing()
    {
        await SeedCatalogAsync();
        var service = CreateService();

        Assert.That(await service.GetFittingProductIdsAsync(null), Is.Empty);
        Assert.That(await service.GetFittingProductIdsAsync(new VehicleSelection(" ", "CBR 250R", 2012)), Is.Empty);
    }

    [Test]
    public async Task GetFittingProductIds_matches_every_year_offered_by_GetYears()
    {
        var vehicle = await AddVehicleAsync("Honda", "CBR 250R", 2015, 2010);
        await AddMappingAsync(500, vehicle);
        var service = CreateService();

        var years = await service.GetYearsAsync("Honda", "CBR 250R");
        Assert.That(years, Has.Count.EqualTo(6));

        foreach (var year in years)
        {
            var ids = await service.GetFittingProductIdsAsync(new VehicleSelection("Honda", "CBR 250R", year));
            Assert.That(ids, Does.Contain(500), $"Year {year}");
        }
    }

    [TestCase(2009, false)]
    [TestCase(2010, true)]
    [TestCase(2012, true)]
    [TestCase(2015, true)]
    [TestCase(2016, false)]
    public async Task GetFittingProductIds_reversed_range_is_read_as_min_to_max(int year, bool expectMatch)
    {
        var vehicle = await AddVehicleAsync("Honda", "CBR 250R", 2015, 2010);
        await AddMappingAsync(500, vehicle);

        var ids = await CreateService().GetFittingProductIdsAsync(new VehicleSelection("Honda", "CBR 250R", year));

        Assert.That(ids.Contains(500), Is.EqualTo(expectMatch));
    }

    [TestCase(2015, 2010, 2012, true)]
    [TestCase(2015, 2010, 2016, false)]
    [TestCase(2010, 2015, 2010, true)]
    [TestCase(2010, 2015, 2009, false)]
    public void ProductFitmentModel_Fits_uses_the_same_year_rule(int yearFrom, int yearTo, int year, bool expected)
    {
        var vehicle = new MotorVehicle { Make = "Honda", Model = "CBR 250R", YearFrom = yearFrom, YearTo = yearTo };
        var model = new ProductFitmentModel { Selection = new VehicleSelection("honda", "cbr 250r", year) };

        Assert.That(model.Fits(vehicle), Is.EqualTo(expected));
        Assert.That(vehicle.CoversYear(year), Is.EqualTo(expected));
    }

    [Test]
    public void Vehicle_DisplayName_shows_reversed_range_as_min_to_max()
    {
        var vehicle = new MotorVehicle { Make = "Honda", Model = "CBR 250R", YearFrom = 2015, YearTo = 2010 };

        Assert.That(vehicle.DisplayName, Is.EqualTo("Honda CBR 250R (2010-2015)"));
    }

    #endregion

    #region GetVehiclesForProductAsync

    [Test]
    public async Task GetVehiclesForProduct_orders_by_make_model_yearFrom()
    {
        var yamaha = await AddVehicleAsync("Yamaha", "MT-07", 2014, 2020);
        var hondaCbrNew = await AddVehicleAsync("Honda", "CBR 250R", 2017, 2019);
        var hondaCb = await AddVehicleAsync("Honda", "CB650R", 2019, 2023);
        var hondaCbrOld = await AddVehicleAsync("Honda", "CBR 250R", 2011, 2013);
        var notMapped = await AddVehicleAsync("Aprilia", "RS 660", 2020, 2024);

        await AddMappingAsync(100, yamaha, hondaCbrNew, hondaCb, hondaCbrOld);
        await AddMappingAsync(200, notMapped);

        var vehicles = await CreateService().GetVehiclesForProductAsync(100);

        Assert.That(vehicles.Select(x => x.Id), Is.EqualTo(new[] { hondaCb.Id, hondaCbrOld.Id, hondaCbrNew.Id, yamaha.Id }));
    }

    #endregion

    #region Selection cookie

    [Test]
    public void Selection_round_trips_through_cookie()
    {
        var selection = RoundTrip(new VehicleSelection("Honda", "CBR 250R", 2012));

        Assert.That(selection, Is.EqualTo(new VehicleSelection("Honda", "CBR 250R", 2012)));
    }

    [Test]
    public void Selection_round_trips_characters_that_need_escaping()
    {
        var original = new VehicleSelection("Ducati & Söhne", "Monster 821; \"Stealth\" = %20", 2018);

        Assert.That(RoundTrip(original), Is.EqualTo(original));
    }

    [Test]
    public void Selection_without_year_round_trips_with_null_year()
    {
        var selection = RoundTrip(new VehicleSelection("Honda", "CBR 250R", null));

        Assert.That(selection, Is.EqualTo(new VehicleSelection("Honda", "CBR 250R", null)));
    }

    [Test]
    public void Selection_with_make_only_keeps_make()
    {
        var selection = RoundTrip(new VehicleSelection("Honda", null, null));

        Assert.That(selection, Is.Not.Null);
        Assert.That(selection.Make, Is.EqualTo("Honda"));
        Assert.That(selection.Model, Is.Null);
        Assert.That(selection.Year, Is.Null);
    }

    [Test]
    public void Selection_with_null_model_and_year_reads_back_null_model()
    {
        Assert.That(RoundTrip(new VehicleSelection("Honda", null, 2012)), Is.EqualTo(new VehicleSelection("Honda", null, 2012)));
    }

    [TestCase("v2|Honda", "CBR 250R", 2012)]
    [TestCase("Honda", "%7C|%20", 2012)]
    [TestCase("Honda", "a||b", null)]
    public void Selection_round_trips_separator_and_prefix_lookalikes(string make, string model, int? year)
    {
        var original = new VehicleSelection(make, model, year);

        Assert.That(RoundTrip(original), Is.EqualTo(original));
    }

    // Values written by the previous format (Make|Model|Year, escaped as a whole) must stay readable.
    [TestCase("Honda", "CBR 250R", 2012)]
    [TestCase("Ducati & Söhne", "Monster 821", null)]
    [TestCase("Honda", null, null)]
    public void GetSelection_reads_legacy_cookie(string make, string model, int? year)
    {
        var legacyContext = new DefaultHttpContext();
        legacyContext.Response.Cookies.Append(CookieName, Uri.EscapeDataString($"{make}|{model}|{year}"));
        var legacyCookie = GetResponseCookie(legacyContext);

        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"{CookieName}={legacyCookie.Value}";

        Assert.That(CreateService(context).GetSelection(), Is.EqualTo(new VehicleSelection(make, model, year)));
    }

    [TestCase("v2%7CHonda")]
    [TestCase("v2%7CHonda%7CCBR%7C2012%7Cextra")]
    [TestCase("v2%7C%7CCBR%7C2012")]
    public void GetSelection_rejects_malformed_v2_cookie(string rawCookie)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"{CookieName}={rawCookie}";

        Assert.That(CreateService(context).GetSelection(), Is.Null);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Saving_empty_make_deletes_cookie(string make)
    {
        var context = new DefaultHttpContext();

        CreateService(context).SaveSelection(new VehicleSelection(make, "CBR 250R", 2012));

        var cookie = GetResponseCookie(context);
        Assert.That(cookie, Is.Not.Null);
        Assert.That(cookie.Value.Value, Is.Empty);
        Assert.That(cookie.Expires, Is.LessThan(DateTimeOffset.UtcNow));
    }

    [Test]
    public void Saving_null_selection_deletes_cookie()
    {
        var context = new DefaultHttpContext();

        CreateService(context).SaveSelection(null);

        var cookie = GetResponseCookie(context);
        Assert.That(cookie, Is.Not.Null);
        Assert.That(cookie.Expires, Is.LessThan(DateTimeOffset.UtcNow));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("%7C%7C2012")]
    public void GetSelection_without_make_returns_null(string rawCookie)
    {
        var context = new DefaultHttpContext();
        if (rawCookie != null)
        {
            context.Request.Headers.Cookie = $"{CookieName}={rawCookie}";
        }

        Assert.That(CreateService(context).GetSelection(), Is.Null);
    }

    [TestCase("Har|ley", "Sportster", 2010)]
    [TestCase("Honda", "CBR|250R", 2012)]
    public void Selection_round_trips_pipe_in_make_or_model(string make, string model, int year)
    {
        var original = new VehicleSelection(make, model, year);

        Assert.That(RoundTrip(original), Is.EqualTo(original));
    }

    #endregion

    #region Utilities

    private FitmentService CreateService(HttpContext httpContext = null)
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext ?? new DefaultHttpContext());
        return new FitmentService(DbContext, accessor.Object);
    }

    /// <summary>
    /// Saves the selection on one request and reads it back on the next one,
    /// sending the cookie value exactly as a browser would.
    /// </summary>
    private VehicleSelection RoundTrip(VehicleSelection selection)
    {
        var saveContext = new DefaultHttpContext();
        CreateService(saveContext).SaveSelection(selection);

        var cookie = GetResponseCookie(saveContext);
        Assert.That(cookie, Is.Not.Null, "Set-Cookie header");

        var readContext = new DefaultHttpContext();
        readContext.Request.Headers.Cookie = $"{cookie.Name}={cookie.Value}";

        return CreateService(readContext).GetSelection();
    }

    private static SetCookieHeaderValue GetResponseCookie(HttpContext context)
    {
        return SetCookieHeaderValue
            .ParseList(context.Response.Headers.SetCookie.ToArray())
            .SingleOrDefault(x => x.Name == CookieName);
    }

    private async Task<MotorVehicle> AddVehicleAsync(string make, string model, int yearFrom, int yearTo)
    {
        var vehicle = new MotorVehicle { Make = make, Model = model, YearFrom = yearFrom, YearTo = yearTo };
        DbContext.MotorVehicles().Add(vehicle);
        await DbContext.SaveChangesAsync();
        return vehicle;
    }

    private async Task AddMappingAsync(int productId, params MotorVehicle[] vehicles)
    {
        DbContext.ProductMotorVehicles().AddRange(vehicles.Select(v => new ProductMotorVehicle { ProductId = productId, MotorVehicleId = v.Id }));
        await DbContext.SaveChangesAsync();
    }

    private async Task<(int CbrPart, int SharedPart, int Cb650Part, int YamahaPart)> SeedCatalogAsync()
    {
        const int cbrPart = 1, sharedPart = 2, cb650Part = 3, yamahaPart = 4;

        var cbrOld = await AddVehicleAsync("Honda", "CBR 250R", 2011, 2013);
        var cbrNew = await AddVehicleAsync("Honda", "CBR 250R", 2017, 2019);
        var cb650 = await AddVehicleAsync("Honda", "CB650R", 2011, 2023);
        var yamahaCbr = await AddVehicleAsync("Yamaha", "CBR 250R", 2011, 2023);

        await AddMappingAsync(cbrPart, cbrOld);
        await AddMappingAsync(sharedPart, cbrOld, cbrNew);
        await AddMappingAsync(cb650Part, cb650);
        await AddMappingAsync(yamahaPart, yamahaCbr);

        return (cbrPart, sharedPart, cb650Part, yamahaPart);
    }

    #endregion
}
