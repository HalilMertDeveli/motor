using System;
using System.Linq;
using MotorShop.AdminPanel.Models;
using MotorShop.AdminPanel.Services;
using NUnit.Framework;
using Smartstore.Core.Catalog.Discounts;
using Smartstore.Core.Checkout.Payment;

namespace MotorShop.AdminPanel.Tests;

[TestFixture]
public class ReportTests
{
    [Test]
    public void Realized_revenue_counts_only_collected_money()
    {
        Assert.That(PanelReportService.RealizedPaymentStates, Is.EquivalentTo(new[]
        {
            (int)PaymentStatus.Paid, (int)PaymentStatus.PartiallyRefunded, (int)PaymentStatus.Refunded
        }));
        Assert.That(PanelReportService.RealizedPaymentStates, Does.Not.Contain((int)PaymentStatus.Pending));
        Assert.That(PanelReportService.RealizedPaymentStates, Does.Not.Contain((int)PaymentStatus.Authorized));
        Assert.That(PanelReportService.RealizedPaymentStates, Does.Not.Contain((int)PaymentStatus.Voided));
    }

    [Test]
    public void Daily_buckets_include_empty_days_and_sum_per_day()
    {
        var from = new DateTime(2026, 10, 1);
        var to = new DateTime(2026, 10, 3);
        var buckets = PanelReportService.BuildBuckets(
            [(new DateTime(2026, 10, 1, 9, 0, 0), 100m), (new DateTime(2026, 10, 1, 23, 59, 0), 50m), (new DateTime(2026, 10, 3, 0, 1, 0), 10m), (new DateTime(2026, 10, 4), 999m)],
            from, to, monthly: false);

        Assert.That(buckets.Select(x => x.Start.Day), Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(buckets.Select(x => x.Amount), Is.EqualTo(new[] { 150m, 0m, 10m }));
        Assert.That(buckets.Select(x => x.Orders), Is.EqualTo(new[] { 2, 0, 1 }));
    }

    [Test]
    public void Monthly_buckets_group_by_calendar_month()
    {
        var buckets = PanelReportService.BuildBuckets(
            [(new DateTime(2026, 1, 31), 10m), (new DateTime(2026, 2, 1), 20m), (new DateTime(2026, 2, 28), 5m)],
            new DateTime(2026, 1, 15), new DateTime(2026, 3, 2), monthly: true);

        Assert.That(buckets.Select(x => x.Start.Month), Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(buckets.Select(x => x.Amount), Is.EqualTo(new[] { 10m, 25m, 0m }));
    }

    [TestCase(7, 7)]
    [TestCase(30, 30)]
    [TestCase(365, 365)]
    [TestCase(13, 30)]
    public void Preset_periods_end_today(int days, int expectedDays)
    {
        var today = new DateTime(2026, 10, 21);
        var filter = new ReportFilter { Days = days };
        filter.Normalize(today);

        Assert.That(filter.To, Is.EqualTo(today));
        Assert.That((filter.To.Value - filter.From.Value).TotalDays + 1, Is.EqualTo(expectedDays));
    }

    [Test]
    public void Custom_period_is_ordered_and_capped_to_two_years()
    {
        var filter = new ReportFilter { Days = 0, From = new DateTime(2026, 10, 1), To = new DateTime(2020, 1, 1) };
        filter.Normalize(new DateTime(2026, 10, 21));

        Assert.That(filter.From, Is.LessThan(filter.To));
        Assert.That((filter.To.Value - filter.From.Value).TotalDays, Is.LessThanOrEqualTo(731));
    }
}

[TestFixture]
public class CouponStateTests
{
    static readonly DateTime Now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public void States()
    {
        Assert.That(CouponPanelService.GetState(new Discount(), 0, Now), Is.EqualTo(CouponState.Active));
        Assert.That(CouponPanelService.GetState(new Discount { EndDateUtc = Now.AddDays(-1) }, 0, Now), Is.EqualTo(CouponState.Expired));
        Assert.That(CouponPanelService.GetState(new Discount { StartDateUtc = Now.AddDays(1) }, 0, Now), Is.EqualTo(CouponState.Scheduled));
        Assert.That(CouponPanelService.GetState(new Discount { DiscountLimitation = DiscountLimitationType.NTimesOnly, LimitationTimes = 3 }, 3, Now), Is.EqualTo(CouponState.UsedUp));
        Assert.That(CouponPanelService.GetState(new Discount { DiscountLimitation = DiscountLimitationType.NTimesPerCustomer, LimitationTimes = 1 }, 50, Now), Is.EqualTo(CouponState.Active));
    }

    [Test]
    public void Manufacturer_discounts_stay_in_the_classic_admin()
    {
        Assert.That(CouponPanelService.SupportedTypes, Does.Not.Contain(DiscountType.AssignedToManufacturers));
        Assert.That(CouponPanelService.SupportedTypes, Does.Contain(DiscountType.AssignedToOrderTotal));
    }
}
