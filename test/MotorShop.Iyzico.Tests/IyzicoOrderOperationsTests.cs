using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MotorShop.Iyzico.Domain;
using MotorShop.Iyzico.Services;
using NUnit.Framework;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Checkout.Payment;

namespace MotorShop.Iyzico.Tests;

/// <summary>
/// Contract with Smartstore order processing (docs/motor/order-operations.md §3): one paid transition per order,
/// amount/currency checked against the current order, unpaid orders cancelled with restock.
/// </summary>
[TestFixture]
[NonParallelizable]
public class IyzicoOrderOperationsTests : IyzicoTestBase
{
    [Test]
    public async Task Two_tokens_of_same_order_paid_in_parallel_mark_order_paid_once()
    {
        var order = await CreateOrderAsync();
        var tab1 = await CreateTransactionAsync(order);
        var tab2 = await CreateTransactionAsync(order);
        Api.OnRetrieve = r => r.Token == tab1.Token ? Api.Result(tab1, paymentId: "111") : Api.Result(tab2, paymentId: "222");
        Api.RetrieveDelay = TimeSpan.FromMilliseconds(150);

        var s1 = NewService();
        var s2 = NewService();
        var results = await Task.WhenAll(
            Task.Run(() => s1.ProcessTokenAsync(tab1.Token, IyzicoEventSource.Callback)),
            Task.Run(() => s2.ProcessTokenAsync(tab2.Token, IyzicoEventSource.Webhook)));

        await using var db = NewDbContext();
        var statuses = await db.Set<IyzicoTransaction>().AsNoTracking().Where(x => x.OrderId == order.Id).Select(x => x.Status).ToListAsync();
        var o = await db.Orders.AsNoTracking().FirstAsync(x => x.Id == order.Id);

        Assert.That(MarkPaidCalls, Is.EqualTo(1));
        Assert.That(results.Select(x => x.Outcome), Is.EquivalentTo(new[] { IyzicoOutcome.Paid, IyzicoOutcome.Duplicate }));
        Assert.That(statuses, Is.EquivalentTo(new[] { IyzicoTransactionStatus.Paid, IyzicoTransactionStatus.Duplicate }));
        Assert.That(o.PaymentStatus, Is.EqualTo(PaymentStatus.Paid));
    }

    [Test]
    public async Task Order_total_changed_after_start_is_not_marked_paid()
    {
        var order = await CreateOrderAsync(150m);
        var tx = await CreateTransactionAsync(order);
        Api.OnRetrieve = _ => Api.Result(tx);

        await using (var db = NewDbContext())
        {
            var o = await db.Orders.FirstAsync(x => x.Id == order.Id);
            o.OrderTotal = 120m; // edited by an admin while the customer was on the payment page
            await db.SaveChangesAsync();
        }

        var result = await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Callback);

        var (after, t, notes) = await LoadAsync(order.Id, tx.Token);
        Assert.That(result.Outcome, Is.EqualTo(IyzicoOutcome.VerificationFailed));
        Assert.That(t.LastErrorCode, Is.EqualTo("ORDER_CHANGED"));
        Assert.That(after.PaymentStatus, Is.EqualTo(PaymentStatus.Pending));
        Assert.That(MarkPaidCalls, Is.Zero);
        Assert.That(notes.Single().Note, Does.Contain("NOT marked as paid"));
    }

    [Test]
    public async Task Order_currency_changed_after_start_is_not_marked_paid()
    {
        var order = await CreateOrderAsync(150m, "TRY");
        var tx = await CreateTransactionAsync(order);
        Api.OnRetrieve = _ => Api.Result(tx);

        await using (var db = NewDbContext())
        {
            var o = await db.Orders.FirstAsync(x => x.Id == order.Id);
            o.CustomerCurrencyCode = "EUR";
            await db.SaveChangesAsync();
        }

        var result = await NewService().ProcessTokenAsync(tx.Token, IyzicoEventSource.Callback);

        Assert.That(result.Outcome, Is.EqualTo(IyzicoOutcome.VerificationFailed));
        Assert.That(MarkPaidCalls, Is.Zero);
    }

    [Test]
    public async Task Stale_unpaid_order_with_failed_attempt_is_cancelled_and_restocked()
    {
        var order = await CreateOrderAsync();
        await CreateTransactionAsync(order, status: IyzicoTransactionStatus.Failed);
        var neverStarted = await CreateOrderAsync();
        Now = Now.AddHours(25);

        var cancelled = await NewService().CancelStaleUnpaidOrdersAsync();

        await using var db = NewDbContext();
        var o = await db.Orders.AsNoTracking().FirstAsync(x => x.Id == order.Id);
        var notes = await db.OrderNotes.AsNoTracking().Where(x => x.OrderId == order.Id).ToListAsync();
        var o2 = await db.Orders.AsNoTracking().FirstAsync(x => x.Id == neverStarted.Id);

        Assert.That(cancelled, Is.EqualTo(2));
        Assert.That(CancelOrderCalls, Is.EqualTo(2));
        Assert.That(o.OrderStatus, Is.EqualTo(OrderStatus.Cancelled));
        Assert.That(o2.OrderStatus, Is.EqualTo(OrderStatus.Cancelled), "Customer never reached the payment page.");
        Assert.That(notes.Single().Note, Does.Contain("cancelled automatically"));
    }

    [Test]
    public async Task Stale_cancellation_settles_open_attempt_first_and_keeps_paid_order()
    {
        var order = await CreateOrderAsync();
        var tx = await CreateTransactionAsync(order);
        Api.OnRetrieve = _ => Api.Result(tx); // paid, but callback and webhook were lost
        Now = Now.AddHours(25);

        var cancelled = await NewService().CancelStaleUnpaidOrdersAsync();

        var (o, t, _) = await LoadAsync(order.Id, tx.Token);
        Assert.That(cancelled, Is.Zero);
        Assert.That(CancelOrderCalls, Is.Zero);
        Assert.That(t.Status, Is.EqualTo(IyzicoTransactionStatus.Paid));
        Assert.That(o.PaymentStatus, Is.EqualTo(PaymentStatus.Paid));
    }

    [TestCase(IyzicoTransactionStatus.FraudReview)]
    [TestCase(IyzicoTransactionStatus.VerificationFailed)]
    [TestCase(IyzicoTransactionStatus.Duplicate)]
    [TestCase(IyzicoTransactionStatus.Cancelled)]
    public async Task Stale_cancellation_skips_orders_that_need_a_human(IyzicoTransactionStatus status)
    {
        var order = await CreateOrderAsync();
        await CreateTransactionAsync(order, status: status);
        Now = Now.AddHours(48);

        var cancelled = await NewService().CancelStaleUnpaidOrdersAsync();

        Assert.That(cancelled, Is.Zero);
        Assert.That(CancelOrderCalls, Is.Zero);
    }

    [Test]
    public async Task Stale_cancellation_respects_age_and_can_be_disabled()
    {
        var order = await CreateOrderAsync();
        await CreateTransactionAsync(order, status: IyzicoTransactionStatus.Failed);

        Now = Now.AddHours(23);
        Assert.That(await NewService().CancelStaleUnpaidOrdersAsync(), Is.Zero, "Younger than 24 h: the customer may still retry.");

        Now = Now.AddHours(10);
        Settings.CancelUnpaidOrdersAfterHours = 0;
        Assert.That(await NewService().CancelStaleUnpaidOrdersAsync(), Is.Zero, "0 disables it.");

        Settings.CancelUnpaidOrdersAfterHours = 24;
        Assert.That(await NewService().CancelStaleUnpaidOrdersAsync(), Is.EqualTo(1));
        Assert.That(CancelOrderCalls, Is.EqualTo(1));
    }
}
