# MotorShop.Iyzico

Card payments through the iyzico hosted Checkout Form (CF) for Smartstore 6.4.
Payment method system name: `Payments.Iyzico`. Payment method type: redirection.

## Flow

1. The customer selects "Credit / debit card (iyzico)" and confirms. The order is placed with payment status **Pending**.
   Nothing is charged at this point.
2. `PostProcessPaymentAsync` redirects to `/iyzico/pay/{orderGuid}`. That page checks that the current customer owns
   the order, settles any earlier open attempt (prevents double charges), initializes a checkout form server-side,
   verifies the initialize signature and stores a `MotorShopIyzicoTransaction` row (unique token and conversationId).
   Then it redirects to iyzico's `paymentPageUrl`.
3. The customer pays on iyzico (3-D Secure and installments are handled there).
4. iyzico posts `token` to `/iyzico/callback`. This cross-site POST usually arrives **without the session cookie**,
   so the module correlates only through the stored transaction. It retrieves the result server-side
   (`/payment/iyzipos/checkoutform/auth/ecom/detail`) and verifies:
   response signature, token, conversationId, basketId (order GUID), currency, `price` = stored amount,
   `paidPrice` = amount for a single payment (installment 1 or missing, 2 decimals) or, with installments, at most
   `MaxInstallmentSurchargePercent` (default 30) above it, paymentId present, `paymentStatus`, `fraudStatus`.
   Anything else is `VerificationFailed` (codes `PAIDPRICE`, `SURCHARGE`) and needs a manual review.
5. Only then is the order marked **Paid** (fraudStatus 1) or **Authorized** (fraudStatus 0, held for review:
   do not ship). A 303 redirect takes the browser to `/iyzico/return/{orderGuid}`.

The order is never marked paid from the browser redirect or the webhook body alone.

The same verification runs for:

- **Webhook** `/iyzico/webhook`: authenticated with `X-IYZ-SIGNATURE-V3`. The body is only a hint that triggers the retrieve.
  Duplicate, delayed and out-of-order events are harmless because settled transactions are never downgraded.
- **Reconciliation task** (every 10 minutes): re-checks open transactions whose callback and webhook never arrived,
  and expires abandoned attempts after 3 hours.
- **Return page reload** by the order owner while the payment is still open. Throttled: at most one retrieve per order
  every 5 seconds and 20 per hour (`IyzicoReturnThrottle`), and none while the attempt was updated in the last 5 seconds.

All processing of a token runs under a distributed token lock and a per-order lock (Order has no concurrency token,
so two tabs/tokens of one order must not pass CanMarkOrderAsPaid in parallel). The row also has an optimistic
concurrency token, and order transitions are idempotent, so racing callbacks and webhooks mark the order paid once.
Amount and currency are checked against the stored attempt and against the current order (OrderTotal x CurrencyRate,
CustomerCurrencyCode); an order edited after the payment started is flagged ORDER_CHANGED and not marked paid.

The module follows Smartstore order processing (docs/motor/order-operations.md): stock is reserved once by
PlaceOrderAsync, MarkOrderAsPaidAsync / MarkAsAuthorizedAsync send the standard notifications, no own e-mails.
A failed attempt does not cancel the order (the customer can retry). The reconciliation task cancels orders that are
still unpaid after CancelUnpaidOrdersAfterHours (default 24, 0 = off) and have only failed/expired attempts, through
CancelOrderAsync(order, true), which restocks the items.

## After-sales

| Smartstore action | iyzico call |
|---|---|
| Refund (full) | Same day (Turkey time): cancel (`/payment/cancel`); if rejected or later: refund per item transaction |
| Partial refund | Refund v1 (`/payment/refund`) per `paymentTransactionId`, spread over the items |
| Void | Cancel, for payments held by the fraud check (order payment status Authorized) |
| Capture | Not supported (payments are captured immediately) |

The refunded amounts per item are stored, so a refund can never exceed what was paid. A full refund returns
`paidPrice`, which includes installment commission paid by the customer. Refund answers are verified: signature
(`paymentId:price:currency:conversationId`), conversationId, paymentId, paymentTransactionId, amount and currency.

### Unconfirmed refunds

Every refund call uses a new conversationId, so a retry after a timeout would be a second refund. When iyzico gives no
answer (timeout, unreadable body) or a success answer that fails verification, the refund (or same-day cancel) is stored
on the item as **unconfirmed** (`REFUND_UNKNOWN`, `REFUND_UNVERIFIED`, `CANCEL_UNKNOWN`) and every further refund of the
order is refused until it is settled. A cancel without an answer never falls back to item refunds.

- Automatic: before the next refund and in every reconciliation run, the transaction report
  (`GET /v2/reporting/payment/details?paymentId=`) is searched for a refund/cancel with that conversationId, amount and
  currency and `refundStatus` 1. Only "processed" is confirmed automatically. iyzico does not document the `refundStatus`
  values; 1 = success is an assumption (unverified without sandbox credentials), anything else waits for an admin.
- Manual: Plugins > Payment > iyzico > Configure lists unconfirmed refunds with "Check with iyzico", "Processed at iyzico"
  and "Not processed" (permission Order.Update). Check the payment in the iyzico merchant panel first.
- A refund confirmed or marked as processed is counted in the module but Smartstore did not record it (the refund call
  failed): record it in the order as an offline refund, as the order note says.

## Configuration

Admin: Plugins > Payment > iyzico > Configure.

| Setting | Default | Notes |
|---|---|---|
| Sandbox mode | on | `https://sandbox-api.iyzipay.com` vs `https://api.iyzipay.com` |
| API key / secret key | - | Stored encrypted with ASP.NET Core Data Protection, write-only in the UI, all-stores scope only |
| Allowed currencies | `TRY` | Method is hidden and refused for other currencies. Only TRY, USD, EUR, GBP, NOK, CHF take effect |
| Maximum installment commission (%) | 30 | Highest accepted `paidPrice` above the order total with installments |
| Installments | off, `1,2,3,6,9` | Single payment is always offered |
| Buyer identity number | `11111111111` | See owner decisions |
| Send order lines | on | Falls back to one basket item when lines do not add up (discounts, gift cards) |
| Cancel same-day full refunds | on | |
| Cancel unpaid orders after (hours) | 24 | 0 turns it off; releases stock |

Signature checks are always on and not editable in the admin area. They can only be relaxed through configuration, for
local mocks; the configure page then shows a red warning:

```
# Never in production.
MotorShop__Iyzico__SkipResponseSignatureVerification=true
# Until iyzico (entegrasyon@iyzico.com) has enabled X-IYZ-SIGNATURE-V3 for the merchant.
MotorShop__Iyzico__AllowUnsignedWebhooks=true
```

The former admin settings `RequireWebhookSignature` / `VerifyResponseSignature` are ignored.

Configuration values take precedence over the admin keys (recommended for production):

```
MotorShop__Iyzico__ApiKey=...
MotorShop__Iyzico__SecretKey=...
# Only for local mocks / private test gateways. Not settable in the admin area.
MotorShop__Iyzico__BaseUrl=http://localhost:5113
```

The tenant's `DataProtection-Keys` folder must be on a persistent volume. Otherwise keys entered in the admin area
cannot be decrypted after a redeploy (the method then hides itself until keys are entered again).

Webhook URL to register in the iyzico merchant panel: `https://<store>/iyzico/webhook`.
The callback URL `https://<store>/iyzico/callback` is sent with every payment.

## Payments.Manual is disabled

Smartstore OfflinePayment "Credit card (manual)" (`Payments.Manual`) stores card numbers in the shop database and must
never be offered. Without changing the upstream module, this module

- hides it in checkout with `BlockedPaymentMethodFilter`, even if an admin activates it again,
- removes it from `PaymentSettings.ActivePaymentMethodSystemNames` (all stores) in `Module.InstallAsync` (fresh installs:
  module migrations are only stamped there) and through the data seeder of migration `DeactivateManualPayment`
  (existing databases). Both are idempotent.

## Data and logging

- No card data is read or stored: the response models do not even contain card fields.
- The transaction table stores token, conversationId, paymentId, amounts, installment count, fraud status,
  item transaction ids and the last error code.
- Logs contain order ids, transaction ids, error codes and event types. Never keys, request or response bodies.
- Buyer name, e-mail, phone, address and IP are sent to iyzico, as the API requires (KVKK: list iyzico as a processor).

## Owner decisions (open)

1. **TCKN**: iyzico marks `buyer.identityNumber` as required. The store does not collect a TCKN; the configurable
   placeholder `11111111111` is sent. Confirm with iyzico whether this is acceptable for the merchant account,
   or decide to collect a TCKN (legal/KVKK impact).
2. **Installment commission**: when installments pass commission to the customer, iyzico charges `paidPrice` above
   the order total. The order total in Smartstore is not changed. Decide how to invoice it (KDV).
3. **Unpaid orders**: the order is created before payment. Unpaid orders are cancelled (stock released, customer
   notified) after 24 hours by default. Confirm the window or set 0 to keep them.

## Sources (checked 2026-10-19)

- Checkout Form initialize / retrieve: https://docs.iyzico.com/en/payment-methods/checkoutform/cf-implementation
- IYZWSv2 auth: https://docs.iyzico.com/en/getting-started/preliminaries/authentication/hmacsha256-auth
- Response signatures: https://docs.iyzico.com/en/advanced/response-signature-validation
- Webhooks and X-IYZ-SIGNATURE-V3: https://docs.iyzico.com/en/advanced/webhook
- Refund and cancel: https://docs.iyzico.com/en/advanced/refund-and-cancel
- Transaction detail report (refunds, cancels): `GET /v2/reporting/payment/details`, https://docs.iyzico.com/llms-full.txt (checked 2026-10-20)
- Official samples used as test vectors: iyzico/iyzipay-php `samples/webhook_Signature_Validation.php`,
  `samples/callbackUrl_Signature_Validation.php`, `samples/signature_verification.php`
