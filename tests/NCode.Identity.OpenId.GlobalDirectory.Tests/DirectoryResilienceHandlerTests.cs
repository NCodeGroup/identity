// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.Metrics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Options;
using NCode.Buffers;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public class DirectoryResilienceHandlerTests : BaseTests
{
    private static Uri Uri { get; } = new("https://directory.example.test/directory/v1/tenants/a");

    private ServiceProvider Provider { get; } =
        new ServiceCollection().AddMetrics().BuildServiceProvider();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Provider.Dispose();

        base.Dispose(disposing);
    }

    private DirectoryResiliencePipelines CreatePipelines(DirectoryClientOptions options)
    {
        var meterFactory = Provider.GetRequiredService<IMeterFactory>();
        var metrics = new DirectoryMetrics(meterFactory);
        return new DirectoryResiliencePipelines(
            Options.Create(options),
            TimeProvider.System,
            metrics
        );
    }

    private static DirectoryClientOptions CreateOptions() =>
        new()
        {
            RetryDelay = TimeSpan.Zero,
            AttemptTimeout = TimeSpan.FromMilliseconds(200),
            TotalTimeout = TimeSpan.FromSeconds(5),
        };

    private HttpClient CreateClient(DirectoryClientOptions options, HttpMessageHandler inner)
    {
        var handler = new DirectoryResilienceHandler(CreatePipelines(options))
        {
            InnerHandler = inner,
        };
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    #region IsRetryable Tests

    [Theory]
    [InlineData("GET", false, true)]
    [InlineData("HEAD", false, true)]
    [InlineData("GET", true, false)]
    [InlineData("PUT", false, false)]
    [InlineData("POST", true, false)]
    [InlineData("DELETE", false, false)]
    public void IsRetryable_WhenCalled_RetriesOnlyAGetOrHeadWithoutABody(
        string method,
        bool hasBody,
        bool expected
    )
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), Uri);
        if (hasBody)
            request.Content = new StringContent("{}", SecureEncoding.UTF8, "application/json");

        var result = DirectoryResiliencePipelines.IsRetryable(request);

        Assert.Equal(expected, result);
    }

    #endregion

    #region SendAsync Tests

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task SendAsync_WhenAGetAnswersTransientlyThenSucceeds_RetriesAndReturnsTheSuccess(
        HttpStatusCode transient
    )
    {
        var attempts = 0;
        var inner = new StubHttpMessageHandler(
            (_, _) =>
                Task.FromResult(
                    StubHttpMessageHandler.CreateResponse(
                        Interlocked.Increment(ref attempts) == 1 ? transient : HttpStatusCode.OK,
                        "{}"
                    )
                )
        );
        using var client = CreateClient(CreateOptions(), inner);

        using var response = await client.GetAsync(Uri, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Requests.Count);
    }

    [Fact]
    public async Task SendAsync_WhenAGetFailsToConnectThenSucceeds_Retries()
    {
        var attempts = 0;
        var inner = new StubHttpMessageHandler(
            (_, _) =>
                Interlocked.Increment(ref attempts) == 1
                    ? Task.FromException<HttpResponseMessage>(
                        new HttpRequestException(HttpRequestError.ConnectionError, "refused")
                    )
                    : Task.FromResult(
                        StubHttpMessageHandler.CreateResponse(HttpStatusCode.OK, "{}")
                    )
        );
        using var client = CreateClient(CreateOptions(), inner);

        using var response = await client.GetAsync(Uri, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Requests.Count);
    }

    [Fact]
    public async Task SendAsync_WhenAGetKeepsFailing_StopsAfterTheMaximumRetriesAndReturnsTheFailure()
    {
        var inner = StubHttpMessageHandler.Respond(HttpStatusCode.ServiceUnavailable);
        var options = CreateOptions();
        options.MaxRetryAttempts = 2;
        using var client = CreateClient(options, inner);

        using var response = await client.GetAsync(Uri, CancellationToken.None);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, inner.Requests.Count);
    }

    [Fact]
    public async Task SendAsync_WhenRetriesAreTurnedOff_SendsAGetOnce()
    {
        var inner = StubHttpMessageHandler.Respond(HttpStatusCode.ServiceUnavailable);
        var options = CreateOptions();
        options.MaxRetryAttempts = 0;
        using var client = CreateClient(options, inner);

        using var response = await client.GetAsync(Uri, CancellationToken.None);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Single(inner.Requests);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task SendAsync_WhenAWriteAnswersTransiently_SendsItOnce(string method)
    {
        // A write is never resent: the token POST carries a single-use assertion, and a PUT's body is consumed.
        var inner = StubHttpMessageHandler.Respond(HttpStatusCode.ServiceUnavailable);
        using var client = CreateClient(CreateOptions(), inner);
        using var request = new HttpRequestMessage(new HttpMethod(method), Uri)
        {
            Content = new StringContent("{}", SecureEncoding.UTF8, "application/json"),
        };

        using var response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Single(inner.Requests);
    }

    [Fact]
    public async Task SendAsync_WhenAnAttemptOutlastsTheAttemptTimeoutThenSucceeds_RetriesTheAttempt()
    {
        var attempts = 0;
        var inner = new StubHttpMessageHandler(
            async (_, token) =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);

                return StubHttpMessageHandler.CreateResponse(HttpStatusCode.OK, "{}");
            }
        );
        using var client = CreateClient(CreateOptions(), inner);

        using var response = await client.GetAsync(Uri, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Requests.Count);
    }

    [Fact]
    public async Task SendAsync_WhenAWriteOutlastsTheTotalTimeout_ThrowsATimeoutCancellation()
    {
        var inner = new StubHttpMessageHandler(
            async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return StubHttpMessageHandler.CreateResponse(HttpStatusCode.OK, "{}");
            }
        );
        var options = CreateOptions();
        options.AttemptTimeout = TimeSpan.FromMilliseconds(50);
        options.TotalTimeout = TimeSpan.FromMilliseconds(100);
        using var client = CreateClient(options, inner);
        using var request = new HttpRequestMessage(HttpMethod.Put, Uri)
        {
            Content = new StringContent("{}", SecureEncoding.UTF8, "application/json"),
        };

        var exception = await Assert.ThrowsAsync<TaskCanceledException>(async () =>
            await client.SendAsync(request, CancellationToken.None)
        );

        Assert.IsType<TimeoutException>(exception.InnerException);
        Assert.Single(inner.Requests);
    }

    [Fact]
    public async Task SendAsync_WhenAGetIsRetried_CountsTheRetryByItsStatusCode()
    {
        var meterFactory = Provider.GetRequiredService<IMeterFactory>();
        using var collector = new MetricCollector<long>(
            meterFactory,
            DirectoryMetrics.MeterName,
            DirectoryMetrics.RetriesCounterName
        );
        var attempts = 0;
        var inner = new StubHttpMessageHandler(
            (_, _) =>
                Task.FromResult(
                    StubHttpMessageHandler.CreateResponse(
                        Interlocked.Increment(ref attempts) == 1
                            ? HttpStatusCode.BadGateway
                            : HttpStatusCode.OK,
                        "{}"
                    )
                )
        );
        using var client = CreateClient(CreateOptions(), inner);

        using var response = await client.GetAsync(Uri, CancellationToken.None);

        var measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.Equal("502", measurement.Tags[DirectoryMetrics.ErrorTypeAttribute]);
    }

    #endregion
}
