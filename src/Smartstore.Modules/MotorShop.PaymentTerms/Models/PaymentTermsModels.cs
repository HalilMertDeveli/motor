namespace MotorShop.PaymentTerms.Models;

public class DealerTermOptionModel
{
    public string Value { get; set; }
    public string Label { get; set; }
    public bool Selected { get; set; }
}

public class DealerTermPaymentInfoModel : ModelBase
{
    public string FormKey { get; set; }
    public List<DealerTermOptionModel> Terms { get; set; } = [];
    public decimal LateFeeMonthlyPercent { get; set; }
}

public class PaymentTermsInfoModel : ModelBase
{
    public bool ShowInstallments { get; set; }
    public bool IsDealer { get; set; }
    public int CashTermDays { get; set; }
    public string DeferredTerms { get; set; }
    public decimal LateFeeMonthlyPercent { get; set; }
    public string MinCardTotal { get; set; }
    public string MinInstallmentTotal { get; set; }
    public string PaymentInfoUrl { get; set; }
}
