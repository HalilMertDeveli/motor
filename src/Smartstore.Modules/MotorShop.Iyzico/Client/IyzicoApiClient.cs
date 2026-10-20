using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace MotorShop.Iyzico.Client;

public interface IIyzicoApiClient
{
    Task<CheckoutFormInitializeResponse> InitializeCheckoutFormAsync(IyzicoCredentials credentials, CheckoutFormInitializeRequest request, CancellationToken cancelToken = default);

    Task<CheckoutFormResult> RetrieveCheckoutFormAsync(IyzicoCredentials credentials, RetrieveCheckoutFormRequest request, CancellationToken cancelToken = default);

    Task<RefundResponse> RefundAsync(IyzicoCredentials credentials, RefundRequest request, CancellationToken cancelToken = default);

    Task<CancelResponse> CancelAsync(IyzicoCredentials credentials, CancelRequest request, CancellationToken cancelToken = default);
}

/// <summary>
/// Thrown when iyzico could not be reached or answered with something that is not an iyzico response.
/// The operation may be retried.
/// </summary>
public sealed class IyzicoApiException : Exception
{
    public IyzicoApiException(string message, Exception innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Small typed client for the iyzico REST API with IYZWSv2 authentication.
/// Request and response bodies are never logged: they contain buyer data.
/// </summary>
public class IyzicoApiClient : IIyzicoApiClient
{
    public const string InitializePath = "/payment/iyzipos/checkoutform/initialize/auth/ecom";
    public const string RetrievePath = "/payment/iyzipos/checkoutform/auth/ecom/detail";
    public const string RefundPath = "/payment/refund";
    public const string CancelPath = "/payment/cancel";

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient _httpClient;

    public IyzicoApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public Task<CheckoutFormInitializeResponse> InitializeCheckoutFormAsync(IyzicoCredentials credentials, CheckoutFormInitializeRequest request, CancellationToken cancelToken = default)
        => PostAsync<CheckoutFormInitializeRequest, CheckoutFormInitializeResponse>(credentials, InitializePath, request, cancelToken);

    public Task<CheckoutFormResult> RetrieveCheckoutFormAsync(IyzicoCredentials credentials, RetrieveCheckoutFormRequest request, CancellationToken cancelToken = default)
        => PostAsync<RetrieveCheckoutFormRequest, CheckoutFormResult>(credentials, RetrievePath, request, cancelToken);

    public Task<RefundResponse> RefundAsync(IyzicoCredentials credentials, RefundRequest request, CancellationToken cancelToken = default)
        => PostAsync<RefundRequest, RefundResponse>(credentials, RefundPath, request, cancelToken);

    public Task<CancelResponse> CancelAsync(IyzicoCredentials credentials, CancelRequest request, CancellationToken cancelToken = default)
        => PostAsync<CancelRequest, CancelResponse>(credentials, CancelPath, request, cancelToken);

    protected virtual async Task<TResponse> PostAsync<TRequest, TResponse>(
        IyzicoCredentials credentials,
        string path,
        TRequest request,
        CancellationToken cancelToken)
        where TResponse : IyzicoResponse
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(request);

        if (!credentials.IsComplete)
        {
            throw new InvalidOperationException("iyzico API credentials are not configured.");
        }

        // The body is serialized once: exactly these bytes are signed and sent.
        var body = JsonSerializer.Serialize(request, JsonOptions);
        var randomKey = IyzicoSignature.CreateRandomKey();

        using var message = new HttpRequestMessage(HttpMethod.Post, credentials.BaseUrl + path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        message.Headers.TryAddWithoutValidation("Authorization", IyzicoSignature.CreateAuthorization(credentials.ApiKey, credentials.SecretKey, randomKey, path, body));
        message.Headers.TryAddWithoutValidation("x-iyzi-rnd", randomKey);
        message.Headers.TryAddWithoutValidation("x-iyzi-client-version", "motorshop-iyzico-1.0.0");
        message.Headers.TryAddWithoutValidation("Accept", "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(message, cancelToken);
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
        {
            throw new IyzicoApiException($"iyzico request to {path} failed: {ex.GetType().Name}.", ex);
        }

        using (response)
        {
            var content = await response.Content.ReadAsStringAsync(cancelToken);

            TResponse result = null;
            if (content.HasValue())
            {
                try
                {
                    result = JsonSerializer.Deserialize<TResponse>(content, JsonOptions);
                }
                catch (JsonException)
                {
                    // Handled below.
                }
            }

            if (result == null || result.Status.IsEmpty())
            {
                throw new IyzicoApiException($"iyzico returned HTTP {(int)response.StatusCode} without a valid response body for {path}.");
            }

            return result;
        }
    }
}
