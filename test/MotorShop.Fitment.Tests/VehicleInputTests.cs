using MotorShop.Fitment.Services;
using NUnit.Framework;

namespace MotorShop.Fitment.Tests;

[TestFixture]
public class VehicleInputTests
{
    [Test]
    public void TryCreate_trims_and_keeps_valid_range()
    {
        Assert.That(VehicleInput.TryCreate(" Honda ", " CBR 250R ", 2011, 2013, 250, out var vehicle), Is.True);

        Assert.That(vehicle.Make, Is.EqualTo("Honda"));
        Assert.That(vehicle.Model, Is.EqualTo("CBR 250R"));
        Assert.That(vehicle.YearFrom, Is.EqualTo(2011));
        Assert.That(vehicle.YearTo, Is.EqualTo(2013));
        Assert.That(vehicle.EngineCc, Is.EqualTo(250));
    }

    [Test]
    public void TryCreate_swaps_reversed_range()
    {
        Assert.That(VehicleInput.TryCreate("Honda", "CBR 250R", 2015, 2010, null, out var vehicle), Is.True);

        Assert.That(vehicle.YearFrom, Is.EqualTo(2010));
        Assert.That(vehicle.YearTo, Is.EqualTo(2015));
    }

    [TestCase(null, "CBR 250R", 2011, 2013)]
    [TestCase(" ", "CBR 250R", 2011, 2013)]
    [TestCase("Honda", "", 2011, 2013)]
    [TestCase("Honda", "CBR 250R", 1949, 2013)]
    [TestCase("Honda", "CBR 250R", 2011, 2101)]
    [TestCase("Honda", "CBR 250R", 0, 2013)]
    [TestCase("Honda", "CBR 250R", 2011, 20130)]
    public void TryCreate_rejects_invalid_input(string make, string model, int yearFrom, int yearTo)
    {
        Assert.That(VehicleInput.TryCreate(make, model, yearFrom, yearTo, null, out var vehicle), Is.False);
        Assert.That(vehicle, Is.Null);
    }

    [Test]
    public void TryCreate_rejects_too_long_make_or_model()
    {
        Assert.That(VehicleInput.TryCreate(new string('x', 101), "CBR 250R", 2011, 2013, null, out _), Is.False);
        Assert.That(VehicleInput.TryCreate("Honda", new string('x', 201), 2011, 2013, null, out _), Is.False);
        Assert.That(VehicleInput.TryCreate(new string('x', 100), new string('x', 200), 2011, 2013, null, out _), Is.True);
    }

    [TestCase(VehicleInput.MinYear)]
    [TestCase(VehicleInput.MaxYear)]
    public void TryCreate_year_bounds_are_inclusive(int year)
    {
        Assert.That(VehicleInput.TryCreate("Honda", "CBR 250R", year, year, null, out _), Is.True);
    }

    [TestCase("Honda;CBR 250R;2011;2013;250", "Honda", "CBR 250R", 2011, 2013, 250)]
    [TestCase("Honda\tCBR 250R\t2013\t2011", "Honda", "CBR 250R", 2011, 2013, null)]
    [TestCase(" Yamaha ; MT-07 ; 2014 ; 2025 ; ", "Yamaha", "MT-07", 2014, 2025, null)]
    [TestCase("Yamaha;MT-07;2014;2025;n/a", "Yamaha", "MT-07", 2014, 2025, null)]
    public void TryParseLine_reads_valid_lines(string line, string make, string model, int yearFrom, int yearTo, int? cc)
    {
        Assert.That(VehicleInput.TryParseLine(line, out var vehicle), Is.True);

        Assert.That(vehicle.Make, Is.EqualTo(make));
        Assert.That(vehicle.Model, Is.EqualTo(model));
        Assert.That(vehicle.YearFrom, Is.EqualTo(yearFrom));
        Assert.That(vehicle.YearTo, Is.EqualTo(yearTo));
        Assert.That(vehicle.EngineCc, Is.EqualTo(cc));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("Honda;CBR 250R;2011")]
    [TestCase("Honda;CBR 250R;abc;2013")]
    [TestCase(";CBR 250R;2011;2013")]
    [TestCase("Honda;;2011;2013")]
    [TestCase("Honda;CBR 250R;2011;99999")]
    public void TryParseLine_rejects_invalid_lines(string line)
    {
        Assert.That(VehicleInput.TryParseLine(line, out var vehicle), Is.False);
        Assert.That(vehicle, Is.Null);
    }
}
