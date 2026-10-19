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
   `paidPrice` >= amount, paymentId present, `paymentStatus`, `fraudStatus`.
5. Only then is the order marked **Paid** (fraudStatus 1) or **Authorized** (fraudStatus 0, held for review:
   do not ship). A 303 redirect takes the browser to `/iyzico/return/{orderGuid}`.

The order is never marked paid from the browser redirect or the webhook body alone.

The same verification runs for:

- **Webhook** `/iyzico/webhook`: authenticated with `X-IYZ-SIGNATURE-V3`. The body is only a hint that triggers the retrieve.
  Duplicate, delayed and out-of-order events are harmless because settled transactions are never downgraded.
- **Reconciliation task** (every 10 minutes): re-checks open transactions whose callback and webhook never arrived,
  and expires abandoned attempts after 3 hours.
- **Return page reload** by the order owner while the payment is still open.

All processing of a token runs under a distributed lock. The row also has an optimistic concurrency token, and order
transitions are idempotent, so a racing callback and webhook mark the order paid once.

## After-sales

| Smartstore action | iyzico call |
|---|---|
| Refund (full) | Same day (Turkey time): cancel (`/payment/cancel`); if rejected or later: refund per item transaction |
| Partial refund | Refund v1 (`/payment/refund`) per `paymentTransactionId`, spread over the items |
| Void | Cancel, for payments held by the fraud check (order payment status Authorized) |
| Capture | Not supported (payments are captured immediately) |

The refunded amounts per item are stored, so a refund can never exceed what was paid. A full refund returns
`paidPrice`, which includes installment commission paid by the customer.

## Configuration

Admin: Plugins > Payment > iyzico > Configure.

| Setting | Default | Notes |
|---|---|---|
| Sandbox mode | on | `https://sandbox-api.iyzipay.com` vs `https://api.iyzipay.com` |
| API key / secret key | - | Stored encrypted with ASP.NET Core Data Protection, write-only in the UI, all-stores scope only |
| Require webhook signature | on | Ask iyzico (entegrasyon@iyzico.com) to enable `X-IYZ-SIGNATURE-V3` |
| Verify response signatures | on | Keep on |
| Installments | off, `1,2,3,6,9` | Single payment is always offered |
| Buyer identity number | `11111111111` | See owner decisions |
| Send order lines | on | Falls back to one basket item when lines do not add up (discounts, gift cards) |
| Cancel same-day full refunds | on | |

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
3. **Pending orders**: the order is created before payment. Unpaid orders stay Pending (stock reserved) until
   cancelled. Decide whether to auto-cancel them after some time.

## Sources (checked 2026-10-19)

- Checkout Form initialize / retrieve: https://docs.iyzico.com/en/payment-methods/checkoutform/cf-implementation
- IYZWSv2 auth: https://docs.iyzico.com/en/getting-started/preliminaries/authentication/hmacsha256-auth
- Response signatures: https://docs.iyzico.com/en/advanced/response-signature-validation
- Webhooks and X-IYZ-SIGNATURE-V3: https://docs.iyzico.com/en/advanced/webhook
- Refund and cancel: https://docs.iyzico.com/en/advanced/refund-and-cancel
- Official samples used as test vectors: iyzico/iyzipay-php `samples/webhook_Signature_Validation.php`,
  `samples/callbackUrl_Signature_Validation.php`, `samples/signature_verification.php`
