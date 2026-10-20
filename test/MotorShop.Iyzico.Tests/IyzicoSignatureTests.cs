using System;
using System.Security.Cryptography;
using System.Text;
using MotorShop.Iyzico.Client;
using MotorShop.Iyzico.Services;
using NUnit.Framework;

namespace MotorShop.Iyzico.Tests;

[TestFixture]
public class IyzicoSignatureTests
{
    // Published sample values from iyzico/iyzipay-php samples/webhook_Signature_Validation.php (sandbox sample key, not ours).
    const string WebhookSampleSecret = "sandbox-UuLnD7wZa0a3DmytfIrwHQqAMfqCNJrs";

    private static IyzicoWebhookPayload SampleWebhook() => new()
    {
        PaymentConversationId = "conversationId",
        MerchantId = "3382172",
        Token = "e828c62d-fd12-4b1a-ad5a-380f15fe6596",
        Status = "SUCCESS",
        IyziReferenceCode = "77e1635c-6b39-475a-8b7b-ca15ad0cd4ac",
        IyziEventType = "CHECKOUT_FORM_AUTH",
        IyziEventTime = 1721825036103,
        IyziPaymentId = "22483975"
    };

    [Test]
    public void Webhook_signature_matches_official_iyzico_sample()
    {
        var signature = IyzicoSignature.ComputeWebhookSignature(WebhookSampleSecret, SampleWebhook());

        Assert.That(signature, Is.EqualTo("4031c575763486a2d2bf3f7ace0baebfcd6bed65393ab4df3fe80c00d5fd6878"));
    }

    [Test]
    public void Response_signature_matches_official_callback_sample()
    {
        // iyzipay-php samples/callbackUrl_Signature_Validation.php: "::1:22484292:success".
        var signature = IyzicoSignature.ComputeResponseSignature("jLc7GQHD2pyJoqXDeEcTnGHYtP7Ai5jl", "", "", "1", "22484292", "success");

        Assert.That(signature, Is.EqualTo("a4f73b80bb32a6ef8358090bbd8609a49a7b53f463048f8ef147496e236d04f0"));
    }

    [Test]
    public void Webhook_verifier_accepts_valid_and_rejects_tampered_signature()
    {
        var payload = SampleWebhook();
        const string valid = "4031c575763486a2d2bf3f7ace0baebfcd6bed65393ab4df3fe80c00d5fd6878";

        Assert.That(IyzicoWebhookVerifier.Verify(payload, valid, WebhookSampleSecret, true), Is.EqualTo(IyzicoWebhookAuth.Valid));
        Assert.That(IyzicoWebhookVerifier.Verify(payload, valid.ToUpperInvariant(), WebhookSampleSecret, true), Is.EqualTo(IyzicoWebhookAuth.Valid));

        payload.Status = "FAILURE";
        Assert.That(IyzicoWebhookVerifier.Verify(payload, valid, WebhookSampleSecret, true), Is.EqualTo(IyzicoWebhookAuth.Invalid));

        payload = SampleWebhook();
        Assert.That(IyzicoWebhookVerifier.Verify(payload, valid, "another-secret", true), Is.EqualTo(IyzicoWebhookAuth.Invalid));
        Assert.That(IyzicoWebhookVerifier.Verify(payload, "deadbeef", WebhookSampleSecret, false), Is.EqualTo(IyzicoWebhookAuth.Invalid));
        Assert.That(IyzicoWebhookVerifier.Verify(payload, valid, null, true), Is.EqualTo(IyzicoWebhookAuth.Invalid));
    }

    [Test]
    public void Webhook_verifier_handles_missing_signature_by_setting()
    {
        Assert.That(IyzicoWebhookVerifier.Verify(SampleWebhook(), null, WebhookSampleSecret, true), Is.EqualTo(IyzicoWebhookAuth.Missing));
        Assert.That(IyzicoWebhookVerifier.Verify(SampleWebhook(), "", WebhookSampleSecret, false), Is.EqualTo(IyzicoWebhookAuth.UnsignedAccepted));
    }

    [TestCase(10, "10")]
    [TestCase(10.0, "10")]
    [TestCase(10.5, "10.5")]
    [TestCase(10.50, "10.5")]
    [TestCase(10.510, "10.51")]
    [TestCase(10.5105, "10.5105")]
    public void Price_for_signature_has_no_trailing_zeros(decimal price, string expected)
    {
        // Examples from https://docs.iyzico.com/en/advanced/response-signature-validation
        Assert.That(IyzicoSignature.FormatPriceForSignature(price), Is.EqualTo(expected));
    }

    [Test]
    public void Price_for_signature_strips_scale_zeros()
    {
        Assert.That(IyzicoSignature.FormatPriceForSignature(10.5000m), Is.EqualTo("10.5"));
        Assert.That(IyzicoSignature.FormatPriceForSignature(100.00m), Is.EqualTo("100"));
        Assert.That(IyzicoSignature.FormatPriceForSignature(null), Is.EqualTo(string.Empty));
    }

    [Test]
    public void Price_for_request_keeps_one_decimal_like_official_sdk()
    {
        Assert.That(IyzicoSignature.FormatPriceForRequest(100m), Is.EqualTo("100.0"));
        Assert.That(IyzicoSignature.FormatPriceForRequest(1.20m), Is.EqualTo("1.2"));
        Assert.That(IyzicoSignature.FormatPriceForRequest(1234.56m), Is.EqualTo("1234.56"));
    }

    [Test]
    public void Authorization_header_follows_IYZWSv2()
    {
        const string body = "{\"locale\":\"tr\",\"conversationId\":\"123\"}";
        const string path = "/payment/iyzipos/checkoutform/auth/ecom/detail";

        var header = IyzicoSignature.CreateAuthorization("api", "secret", "rnd123", path, body);

        Assert.That(header, Does.StartWith("IYZWSv2 "));

        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header["IYZWSv2 ".Length..]));
        var expectedSignature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("secret"), Encoding.UTF8.GetBytes("rnd123" + path + body))).ToLowerInvariant();

        Assert.That(decoded, Is.EqualTo($"apiKey:api&randomKey:rnd123&signature:{expectedSignature}"));
    }

    [Test]
    public void Random_keys_are_unique()
    {
        var first = IyzicoSignature.CreateRandomKey();
        var second = IyzicoSignature.CreateRandomKey();

        Assert.That(second, Is.Not.EqualTo(first));
    }

    [Test]
    public void Signature_comparison_rejects_empty_values()
    {
        Assert.That(IyzicoSignature.SignatureEquals("abc", null), Is.False);
        Assert.That(IyzicoSignature.SignatureEquals(null, "abc"), Is.False);
        Assert.That(IyzicoSignature.SignatureEquals("abc", "abd"), Is.False);
        Assert.That(IyzicoSignature.SignatureEquals("ABC", "abc"), Is.True);
    }

    [Test]
    public void Credentials_never_print_keys()
    {
        var credentials = new IyzicoCredentials("my-api-key", "my-secret-key", "https://sandbox-api.iyzipay.com/");

        Assert.That(credentials.ToString(), Does.Not.Contain("my-api-key").And.Not.Contain("my-secret-key"));
        Assert.That(credentials.BaseUrl, Is.EqualTo("https://sandbox-api.iyzipay.com"));
    }
}
