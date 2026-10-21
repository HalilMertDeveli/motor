using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace MotorShop.WordPress.Client;

/// <summary>
/// Retry, circuit breaker and timeouts for WordPress requests. One instance per application,
/// so that the circuit state is shared by all requests.
/// </summary>
/// <remarks>
/// Only GET requests are sent, so a retry cannot duplicate anything. When WordPress keeps failing the circuit opens
/// and blog pages fall back to cached content at once, instead of each page view waiting for the timeout.
/// Changing <see cref="WordPressOptions.TimeoutSeconds"/> needs an application restart.
/// </remarks>
public sealed class WordPressResilience
{
    public WordPressResilience(IOptions<WordPressOptions> options)
    {
        var attemptTimeout = TimeSpan.FromSeconds(Math.Clamp(options.Value.TimeoutSeconds, 1, 30));

        var shouldHandle = new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .Handle<TimeoutRejectedException>()
            .HandleResult(r => IsTransient(r.StatusCode));

        Pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            // Upper bound for one call including retries, so a page never waits much longer than one timeout.
            .AddTimeout(attemptTimeout * 2 + TimeSpan.FromSeconds(1))
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = shouldHandle,
                MaxRetryAttempts = 1,
                Delay = TimeSpan.FromMilliseconds(250),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = shouldHandle,
                FailureRatio = 0.5,
                MinimumThroughput = 4,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(30)
            })
            .AddTimeout(attemptTimeout)
            .Build();
    }

    public ResiliencePipeline<HttpResponseMessage> Pipeline { get; }

    internal static bool IsTransient(HttpStatusCode status)
        => status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)status >= 500;
}
