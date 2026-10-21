using System;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MotorShop.Iyzico.Client;
using NUnit.Framework;

namespace MotorShop.Iyzico.Tests;

[TestFixture]
public class IyzicoApiClientTests
{
    private static readonly IyzicoCredentials Credentials = new("api-key", "secret-key", "https://sandbox-api.iyzipay.com");

    private class StubHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage Request { get; private set; }
        public string Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request, Body);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK)
        => new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Test]
    public async Task Retrieve_signs_exact_body_and_parses_numeric_ids()
    {
        var handler = new StubHandler((_, _) => Json("""
            {"status":"success","paymentStatus":"SUCCESS","paymentId":22416035,"price":10.50,"paidPrice":11.0,
             "currency":"TRY","basketId":"B1","conversationId":"c1","token":"t1","fraudStatus":1,"installment":3,
             "binNumber":"55287900","lastFourDigits":"0008",
             "itemTransactions":[{"itemId":"OI1","paymentTransactionId":23592427,"paidPrice":11.0}],"signature":"abc"}
            """));

        var client = new IyzicoApiClient(new HttpClient(handler));
        var result = await client.RetrieveCheckoutFormAsync(Credentials, new RetrieveCheckoutFormRequest { Locale = "en", ConversationId = "c1", Token = "t1" });

        Assert.That(handler.Request.RequestUri.ToString(), Is.EqualTo("https://sandbox-api.iyzipay.com/payment/iyzipos/checkoutform/auth/ecom/detail"));
        Assert.That(handler.Body, Is.EqualTo("{\"locale\":\"en\",\"conversationId\":\"c1\",\"token\":\"t1\"}"));

        var rnd = string.Join(",", handler.Request.Headers.GetValues("x-iyzi-rnd"));
        var auth = string.Join(",", handler.Request.Headers.GetValues("Authorization"));
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(auth["IYZWSv2 ".Length..]));
        var expectedSignature = Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes("secret-key"),
            Encoding.UTF8.GetBytes(rnd + IyzicoApiClient.RetrievePath + handler.Body))).ToLowerInvariant();

        Assert.That(decoded, Is.EqualTo($"apiKey:api-key&randomKey:{rnd}&signature:{expectedSignature}"));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.PaymentId, Is.EqualTo("22416035"));
        Assert.That(result.Price, Is.EqualTo(10.50m));
        Assert.That(result.ItemTransactions[0].PaymentTransactionId, Is.EqualTo("23592427"));
        Assert.That(IyzicoSignature.FormatPriceForSignature(result.Price), Is.EqualTo("10.5"));
    }

    [Test]
    public async Task Error_body_with_http_400_is_returned_as_failure()
    {
        var handler = new StubHandler((_, _) => Json("""{"status":"failure","errorCode":"1001","errorMessage":"api bilgileri bulunamadı"}""", HttpStatusCode.BadRequest));
        var client = new IyzicoApiClient(new HttpClient(handler));

        var result = await client.CancelAsync(Credentials, new CancelRequest { PaymentId = "1" });

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo("1001"));
    }

    [Test]
    public void Non_iyzico_response_throws_retryable_exception()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>bad gateway</html>") });
        var client = new IyzicoApiClient(new HttpClient(handler));

        Assert.ThrowsAsync<IyzicoApiException>(() => client.RefundAsync(Credentials, new RefundRequest { PaymentTransactionId = "1", Price = "1.0" }));
    }

    [Test]
    public void Network_error_throws_retryable_exception()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("connection refused"));
        var client = new IyzicoApiClient(new HttpClient(handler));

        Assert.ThrowsAsync<IyzicoApiException>(() => client.RetrieveCheckoutFormAsync(Credentials, new RetrieveCheckoutFormRequest { Token = "t" }));
    }

    [Test]
    public void Missing_credentials_never_send_a_request()
    {
        var handler = new StubHandler((_, _) => Json("{\"status\":\"success\"}"));
        var client = new IyzicoApiClient(new HttpClient(handler));

        Assert.ThrowsAsync<InvalidOperationException>(() => client.RetrieveCheckoutFormAsync(new IyzicoCredentials(null, null, "https://x"), new RetrieveCheckoutFormRequest()));
        Assert.That(handler.Request, Is.Null);
    }
}
