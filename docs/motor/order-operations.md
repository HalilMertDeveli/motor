# Order operations audit

Smartstore 6.4 as used by MOTOR. This audit covers what happens to an order from placement to completion,
cancellation or refund, and what a payment module (iyzico) must do. It does not change behaviour.

Verified on 2026-10-19: code reading plus a local runtime run (SQLite, sample data, port 5104).
"Runtime" in the tables means it was executed and checked in the database.

## 1. Lifecycle

| Step | Where (Smartstore) | What happens | Verified |
|---|---|---|---|
| Order created | `OrderProcessingService.PlaceOrderAsync` (`OrderProcessingService.PlaceOrder.cs`) | One DB transaction: payment processed, order saved (`OrderStatus = Pending`), items added, cart cleared, order note. Commit. | Runtime: orders #1, #2 |
| Stock decremented | `AddOrderItems` → `ProductService.AdjustInventoryAsync(cartItem, true)` | **At placement**, not at payment, inside the transaction. Only for `ManageInventoryMethod = ManageStock / ManageStockByAttributes`. | Runtime: stock 10 → 8 for qty 2 |
| Customer notified | `SendOrderMessages` after commit | `OrderPlaced.StoreOwnerNotification` + `OrderPlaced.CustomerNotification` queued once per placement, ids written to order notes. | Runtime: QueuedEmail #5, #6 |
| Events | after commit | `OrderPlacedEvent`; `OrderPaidEvent` only if already paid at placement. | Code |
| Admin sees order | Sales > Orders | Order, notes, payment/shipping status. | Runtime: admin order edit |
| Payment verified | `MarkOrderAsPaidAsync` / `CaptureAsync` (`OrderProcessingService.Payment.cs`) | `PaymentStatus = Paid`, `PaidDateUtc`, note, `CheckOrderStatusAsync` (Pending → Processing), `OrderPaidEvent`. **No stock change, no e-mail** (Smartstore has no "order paid" template). | Runtime: order #2 |
| Shipment + tracking | `AddShipmentAsync`, `ShipAsync`, `DeliverAsync` | Shipped / delivered dates, `Order.ShippingStatus`, `ShipmentSent` / `ShipmentDelivered` customer e-mails. Tracking URL: `MotorShop.Tracking` (see `shipping-tracking.md`). | Runtime: order #1, QueuedEmail #3, #4 |
| Completed | `CheckOrderStatusAsync` | Paid **and** (delivered or shipping not required) → Complete + `OrderCompleted.CustomerNotification`. | Code; order #1 stays Processing because unpaid |
| Cancelled | `CancelOrderAsync(order, notifyCustomer)` | `OrderStatus = Cancelled`, `OrderCancelled.CustomerNotification`, **restock** of every item. **Payment status unchanged**, no refund. | Runtime: stock 8 → 10, QueuedEmail #7 |
| Refund | `RefundAsync` / `RefundOfflineAsync` / partial | `PaymentStatus = Refunded / PartiallyRefunded`, `RefundedAmount`, note. **No stock change, no e-mail, no status change.** | Runtime: offline refund, Refunded, 59.90 |
| Void | `VoidAsync` / `VoidOfflineAsync` | Only from `Authorized`. No stock change, no e-mail. | Code |

## 2. Repeated payment confirmations

**Question:** can a second confirmation (double callback, browser refresh, admin double click) decrement stock twice or send e-mails twice?

| Scenario | Result | Evidence |
|---|---|---|
| `MarkOrderAsPaidAsync` called twice | The second call throws `PaymentException("Order.CannotMarkPaid")` because `CanMarkOrderAsPaid()` is false for Paid / Refunded / Cancelled. No second note, `PaidDateUtc` unchanged. | Runtime: admin "mark as paid" posted twice on order #2 → one "marked as paid" note, stock 8, e-mail count unchanged |
| Stock on payment | Payment never touches stock; stock is adjusted once at placement. | Code + runtime |
| E-mails on payment | No e-mail is sent on payment. Completion e-mail is guarded by `SetOrderStatusAsync` (returns when the status is unchanged). | Code |
| Cancel twice | `CanCancelOrder()` false on the second call → no second restock, no second e-mail. | Runtime: stock 10 (not 12), one cancel e-mail |
| Refund twice | `CanRefundOffline()` false after the first → no-op. | Runtime |
| Order placement twice for one payment (redirect flow, `PaymentCompleted` hit twice) | The cart is cleared by the first placement, so `ValidateOrderPlacementAsync` rejects the second (`CheckoutWorkflow.CompletePaymentAsync` logs "payment succeeded but the order placement failed" only if the cart still has items). | Code |

**Gap:** `Order` has no concurrency token and `OrderGuid` has a non-unique index. Two callbacks processed **at the same
moment** can both pass `CanMarkOrderAsPaid()` (check-then-act). The effect is limited to a duplicate note and a second
`OrderPaidEvent` (stock and e-mails are not affected), but the payment module must serialise its callback handling (§3).

## 3. What the iyzico module must do

The payment module owns the integration (`payment-iyzico` session). The contract with Smartstore order processing:

1. **Placement.** Implement `IPaymentMethod` (blueprint `Smartstore.PayPal`). For 3-D Secure / hosted checkout use the
   confirm flow: `RequiresConfirmation = true`, `GetConfirmationUrlAsync` (redirect), `CompletePaymentAsync` (return).
   The order is created by `PlaceOrderAsync` after the return; stock is decremented there, once.
   Put the iyzico conversation id / token into `ProcessPaymentRequest` so it ends up on the order
   (`AuthorizationTransactionId` or `CaptureTransactionId`) and set `OrderGuid` as the iyzico `basketId` / conversation reference.
2. **Callback / webhook** (`[WebhookEndpoint]`, like PayPal and Stripe):
   - Verify the request with iyzico (signature, or retrieve the payment by token from the iyzico API). Never trust form values alone.
   - Find the order by `OrderGuid` or the stored transaction id **and** `PaymentMethodSystemName`.
   - Serialise per order: `await using var handle = await lockProvider.GetLock($"iyzico:{order.OrderGuid}").AcquireAsync(TimeSpan.FromSeconds(30));`
     (`IDistributedLockProvider`; the default provider is in-process, which matches a single VPS container), then reload the order.
   - Compare the paid amount and currency with `order.OrderTotal` / `CustomerCurrencyCode`.
   - Paid: `if (order.CanMarkOrderAsPaid()) { order.CaptureTransactionId = paymentId; await orderProcessingService.MarkOrderAsPaidAsync(order); }`
     otherwise do nothing and answer 200 (idempotent).
   - Failed / cancelled payment before capture: `CancelOrderAsync(order, notifyCustomer: true)` if `CanCancelOrder()`, which restocks.
   - Always answer the webhook with 2xx once handled; return 5xx only if iyzico should retry (order not found yet).
   - Write one order note per callback with the iyzico payment id, never card data.
3. **Refund.** Implement `RefundAsync` (and partial) in the provider so admin "Refund" calls iyzico. Remember: refund does not
   restock; if goods are returned, the merchant restocks via a return request or a manual stock change.
4. **Do not** send own e-mails for order placed / paid, and do not adjust stock in the module.

## 4. Notification failure and retry (QueuedEmail)

- Every e-mail is queued (`QueuedEmail`), never sent inline. Task "Send emails" (`QueuedMessagesSendTask`) runs every minute and
  picks `SentTries < 3 && !SendManually && SentOnUtc == null`, ordered by priority.
- **Per message errors** (rejected recipient, malformed message): `ProcessMailBatchAsync` increments `SentTries` in `finally`,
  so a message is tried at most **3 times**, one minute apart. After that it is never picked up again.
- **SMTP connection errors** (server down, wrong host): `SendMailsAsync` opens the connection **before** the per-message loop, so the
  exception happens before `SentTries` is incremented. Messages stay at `SentTries = 0` and are retried every minute until the
  server is reachable. Runtime: local store without SMTP, task log "Send emails: The operation has timed out", 7 queued e-mails all `SentTries = 0`.
- **Purge:** `QueuedMessagesClearTask` (daily) deletes sent messages and messages with `SentTries >= 3` older than
  `CommonSettings.MaxQueuedMessagesAgeInDays` (default 14). A message that failed 3 times disappears after 14 days without alert.
- **Admin:** System > Message queue: filter by "max. sent tries", "Requeue" (creates a copy with tries reset, **attachments are not copied**),
  "Send now".

Operational recommendation: monitor `QueuedEmail` rows with `SentTries >= 3` or `SentOnUtc IS NULL AND CreatedOnUtc < now - 15 min`
(a health check or a daily admin look), and configure a real SMTP account before go-live.

## 5. Risks and decisions

| # | Topic | Risk | Owner decision / follow-up |
|---|---|---|---|
| 1 | Stock at placement | Unpaid orders (bank transfer, abandoned 3-D Secure after placement) hold stock until cancelled | Define a rule: cancel unpaid orders after N hours (manual or a scheduled task, not built) |
| 2 | Cancel ≠ refund | Cancelling a paid order does not refund | Process: cancel **and** refund via iyzico |
| 3 | Refund ≠ restock | Refund leaves stock unchanged | Use return requests or adjust stock manually |
| 4 | No "payment received" e-mail | Customer only gets "order placed" | Decide whether a paid e-mail is needed (template + event consumer) |
| 5 | Concurrent callbacks | Duplicate note / event | iyzico module serialises per order (§3) |
| 6 | E-mail failures | Silent drop after 3 tries + 14 days | Monitoring, SMTP before go-live |
| 7 | Invoice | See `invoicing.md` | e-Arşiv provider choice |
