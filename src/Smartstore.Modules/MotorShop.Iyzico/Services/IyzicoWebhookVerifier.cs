using MotorShop.Iyzico.Client;

namespace MotorShop.Iyzico.Services;

public enum IyzicoWebhookAuth
{
    /// <summary>
    /// X-IYZ-SIGNATURE-V3 present and valid.
    /// </summary>
    Valid,

    /// <summary>
    /// No signature, but signatures are not required. The event is only used as a hint to retrieve the result.
    /// </summary>
    UnsignedAccepted,

    /// <summary>
    /// No signature and signatures are required.
    /// </summary>
    Missing,

    /// <summary>
    /// Signature present but wrong, or no secret key to check it.
    /// </summary>
    Invalid
}

public static class IyzicoWebhookVerifier
{
    public static IyzicoWebhookAuth Verify(IyzicoWebhookPayload payload, string signatureHeader, string secretKey, bool requireSignature)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (signatureHeader.IsEmpty())
        {
            return requireSignature ? IyzicoWebhookAuth.Missing : IyzicoWebhookAuth.UnsignedAccepted;
        }

        if (secretKey.IsEmpty())
        {
            return IyzicoWebhookAuth.Invalid;
        }

        var expected = IyzicoSignature.ComputeWebhookSignature(secretKey, payload);
        return IyzicoSignature.SignatureEquals(expected, signatureHeader) ? IyzicoWebhookAuth.Valid : IyzicoWebhookAuth.Invalid;
    }
}
