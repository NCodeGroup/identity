// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Holds the resilience pipelines that bound a request to the directory service and selects the one that fits a
/// request. A request that is safe to resend — a GET or HEAD without a body, while retries are turned on — runs through
/// the total timeout, a bounded and jittered retry, and a per-attempt timeout; any other request runs through the total
/// timeout only, so it is never sent twice.
/// </summary>
/// <param name="options">The <see cref="DirectoryClientOptions"/> that hold the limits.</param>
/// <param name="timeProvider">The <see cref="TimeProvider"/> that runs the timeouts and the retry delays.</param>
/// <param name="metrics">The <see cref="DirectoryMetrics"/> that count the retries and the timeouts.</param>
// See ADR-1385 and ADR-1336.
internal sealed class DirectoryResiliencePipelines(
    IOptions<DirectoryClientOptions> options,
    TimeProvider timeProvider,
    DirectoryMetrics metrics
)
{
    private ResiliencePipeline<HttpResponseMessage> Retrying { get; } =
        Build(options.Value, options.Value.MaxRetryAttempts, timeProvider, metrics);

    private ResiliencePipeline<HttpResponseMessage> Bounded { get; } =
        Build(options.Value, maxRetryAttempts: 0, timeProvider, metrics);

    /// <summary>
    /// Selects the pipeline that bounds <paramref name="request"/>.
    /// </summary>
    /// <param name="request">The request that is about to be sent.</param>
    /// <returns>The pipeline that runs the request.</returns>
    public ResiliencePipeline<HttpResponseMessage> Select(HttpRequestMessage request) =>
        IsRetryable(request) ? Retrying : Bounded;

    /// <summary>
    /// Determines whether <paramref name="request"/> may be sent again after a failed attempt. A request with a body is
    /// never resent, because a body's content may be a stream that one attempt consumes. That keeps the token request,
    /// whose client assertion carries a single-use <c>jti</c>, and every write to the directory, which a caller's own
    /// revision check already makes safe to repeat, to one send each.
    /// </summary>
    /// <param name="request">The request that is about to be sent.</param>
    /// <returns><c>true</c> when the request is a GET or HEAD without a body; otherwise, <c>false</c>.</returns>
    internal static bool IsRetryable(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var isSafeMethod = request.Method == HttpMethod.Get || request.Method == HttpMethod.Head;
        return isSafeMethod && request.Content is null;
    }

    /// <summary>
    /// Determines whether the outcome of an attempt is transient and may be retried: a failed connection, a request
    /// that the per-attempt timeout cancelled, or a 408, 429 or 5xx response.
    /// </summary>
    /// <param name="outcome">The outcome of the attempt.</param>
    /// <returns><c>true</c> when the outcome is transient; otherwise, <c>false</c>.</returns>
    internal static bool IsTransient(Outcome<HttpResponseMessage> outcome)
    {
        if (outcome.Exception is HttpRequestException or TimeoutRejectedException)
            return true;

        if (outcome.Result is not { } response)
            return false;

        var statusCode = (int)response.StatusCode;
        return statusCode is 408 or 429 or >= 500;
    }

    /// <summary>
    /// Gets the bounded <c>error.type</c> of a failed attempt: the response's status code, <c>timeout</c> for an
    /// attempt that the per-attempt timeout cancelled, the <see cref="HttpRequestError"/> in snake case for a failed
    /// request, or <c>_OTHER</c>.
    /// </summary>
    /// <param name="outcome">The outcome of the attempt.</param>
    /// <returns>The error type.</returns>
    internal static string GetErrorType(Outcome<HttpResponseMessage> outcome)
    {
        if (outcome is { Result: { } response })
            return ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);

        if (outcome is { Exception: TimeoutRejectedException })
            return TimeoutErrorType;

        if (outcome is { Exception: HttpRequestException exception })
        {
            var requestError = exception.HttpRequestError.ToString();
            return ToSnakeCase(requestError);
        }

        return OtherErrorType;
    }

    private const string TimeoutErrorType = "timeout";

    // The bounded catch-all value the family's metrics use (OpenIdMetricAttributes.OtherValue).
    private const string OtherErrorType = "_OTHER";

    private static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (char.IsUpper(character))
            {
                if (index > 0)
                    builder.Append('_');
                var lowerCharacter = char.ToLowerInvariant(character);
                builder.Append(lowerCharacter);
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static TimeoutStrategyOptions CreateTimeout(
        TimeSpan timeout,
        string scope,
        DirectoryMetrics metrics
    ) =>
        new()
        {
            Timeout = timeout,
            OnTimeout = args =>
            {
                metrics.TimedOut(scope);
                return ValueTask.CompletedTask;
            },
        };

    private static ResiliencePipeline<HttpResponseMessage> Build(
        DirectoryClientOptions options,
        int maxRetryAttempts,
        TimeProvider timeProvider,
        DirectoryMetrics metrics
    )
    {
        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>
        {
            TimeProvider = timeProvider,
        };
        var totalTimeout = CreateTimeout(
            options.TotalTimeout,
            DirectoryMetrics.TotalTimeoutScope,
            metrics
        );
        builder.AddTimeout(totalTimeout);

        if (maxRetryAttempts > 0)
        {
            var retryOptions = new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = maxRetryAttempts,
                Delay = options.RetryDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = static args =>
                {
                    var isTransient = IsTransient(args.Outcome);
                    return ValueTask.FromResult(isTransient);
                },
                OnRetry = args =>
                {
                    var errorType = GetErrorType(args.Outcome);
                    metrics.Retried(errorType);
                    args.Outcome.Result?.Dispose();
                    return ValueTask.CompletedTask;
                },
            };
            builder.AddRetry(retryOptions);
            var attemptTimeout = CreateTimeout(
                options.AttemptTimeout,
                DirectoryMetrics.AttemptTimeoutScope,
                metrics
            );
            builder.AddTimeout(attemptTimeout);
        }

        return builder.Build();
    }
}
