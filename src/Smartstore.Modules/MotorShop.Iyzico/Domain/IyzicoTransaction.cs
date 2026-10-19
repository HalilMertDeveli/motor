using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Smartstore.Domain;

namespace MotorShop.Iyzico.Domain;

public enum IyzicoTransactionStatus
{
    /// <summary>
    /// Checkout form initialized, customer redirected to iyzico.
    /// </summary>
    Initialized = 0,

    /// <summary>
    /// iyzico reports a payment still in progress, e.g. 3-D Secure not completed.
    /// </summary>
    Pending = 10,

    /// <summary>
    /// Payment verified and captured.
    /// </summary>
    Paid = 20,

    /// <summary>
    /// Payment captured but held by the iyzico fraud check (fraudStatus 0). Do not ship.
    /// </summary>
    FraudReview = 30,

    /// <summary>
    /// Payment failed, was rejected by the fraud check or was not completed.
    /// </summary>
    Failed = 40,

    /// <summary>
    /// iyzico reports a payment that does not match the stored transaction (signature, amount, currency or correlation).
    /// Needs manual review. The order is never marked as paid.
    /// </summary>
    VerificationFailed = 50,

    /// <summary>
    /// A second successful payment for an order that was already paid through another token. Needs a manual refund.
    /// </summary>
    Duplicate = 60,

    /// <summary>
    /// Payment cancelled through the iyzico cancel service.
    /// </summary>
    Cancelled = 70,

    /// <summary>
    /// Customer never completed the form and the token is no longer polled.
    /// </summary>
    Expired = 80
}

/// <summary>
/// Server-side state of one iyzico checkout form attempt. Holds no card data.
/// </summary>
[Table("MotorShopIyzicoTransaction")]
[Index(nameof(Token), IsUnique = true, Name = "IX_MotorShopIyzicoTransaction_Token")]
[Index(nameof(ConversationId), IsUnique = true, Name = "IX_MotorShopIyzicoTransaction_ConversationId")]
[Index(nameof(OrderId), Name = "IX_MotorShopIyzicoTransaction_OrderId")]
[Index(nameof(PaymentId), Name = "IX_MotorShopIyzicoTransaction_PaymentId")]
[Index(nameof(Status), nameof(CreatedOnUtc), Name = "IX_MotorShopIyzicoTransaction_Status")]
public class IyzicoTransaction : BaseEntity
{
    public int OrderId { get; set; }

    public int StoreId { get; set; }

    /// <summary>
    /// Checkout form token returned by initialize. Unique.
    /// </summary>
    [Required, StringLength(200)]
    public string Token { get; set; }

    /// <summary>
    /// Our conversationId for this attempt. Unique.
    /// </summary>
    [Required, StringLength(64)]
    public string ConversationId { get; set; }

    /// <summary>
    /// basketId sent to iyzico (the order GUID).
    /// </summary>
    [Required, StringLength(64)]
    public string BasketId { get; set; }

    [StringLength(64)]
    public string PaymentId { get; set; }

    public IyzicoTransactionStatus Status { get; set; }

    /// <summary>
    /// Expected basket total ("price") in <see cref="Currency"/>.
    /// </summary>
    [Precision(18, 4)]
    public decimal Amount { get; set; }

    [Required, StringLength(3)]
    public string Currency { get; set; }

    /// <summary>
    /// Amount charged by iyzico, including installment commission passed on to the customer.
    /// </summary>
    [Precision(18, 4)]
    public decimal? PaidPrice { get; set; }

    public int? Installment { get; set; }

    public int? FraudStatus { get; set; }

    [Precision(18, 4)]
    public decimal RefundedAmount { get; set; }

    /// <summary>
    /// JSON list of item transactions (paymentTransactionId, paid and refunded amount) needed for refunds.
    /// </summary>
    [MaxLength]
    public string ItemTransactionsJson { get; set; }

    [StringLength(50)]
    public string LastErrorCode { get; set; }

    [StringLength(500)]
    public string LastErrorMessage { get; set; }

    /// <summary>
    /// Optimistic concurrency token. Incremented on every update.
    /// </summary>
    [ConcurrencyCheck]
    public int Version { get; set; }

    public DateTime CreatedOnUtc { get; set; }

    public DateTime UpdatedOnUtc { get; set; }

    public DateTime? PaidOnUtc { get; set; }
}

/// <summary>
/// Element of <see cref="IyzicoTransaction.ItemTransactionsJson"/>.
/// </summary>
public class IyzicoItemTransaction
{
    public string PaymentTransactionId { get; set; }
    public string ItemId { get; set; }
    public decimal PaidPrice { get; set; }
    public decimal RefundedAmount { get; set; }
}
