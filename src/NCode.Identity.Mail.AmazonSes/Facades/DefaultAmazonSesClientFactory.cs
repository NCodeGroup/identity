// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using Amazon;
using Amazon.Runtime;
using Amazon.SimpleEmailV2;
using Microsoft.Extensions.Options;
using NCode.Identity.Mail.AmazonSes.Options;

namespace NCode.Identity.Mail.AmazonSes.Facades;

/// <summary>
/// Provides the default implementation of <see cref="IAmazonSesClientFactory"/>: one
/// <see cref="AmazonSimpleEmailServiceV2Client"/> for the configured region, created on first use and shared by every
/// delivery.
/// </summary>
/// <remarks>
/// The client resolves its credentials through the default chain of the AWS SDK (a web identity token, a shared
/// profile, container credentials or instance credentials) on its first request, so creating it never fails for lack of
/// credentials; the first call that uses it does. The SDK retries a failed call twice in its standard mode.
/// </remarks>
// See ADR-1528.
[ExcludeFromCodeCoverage(
    Justification = "Creates an AWS SDK client; a passthrough to AWSSDK.SimpleEmailV2."
)]
internal sealed class DefaultAmazonSesClientFactory(
    IOptions<AmazonSesMailTransportOptions> optionsAccessor
) : IAmazonSesClientFactory, IDisposable
{
    private const int MaxErrorRetry = 2;

    private Lazy<AmazonSimpleEmailServiceV2Client> LazyClient { get; } =
        new(
            () => CreateClient(optionsAccessor.Value),
            LazyThreadSafetyMode.ExecutionAndPublication
        );

    private static AmazonSimpleEmailServiceV2Client CreateClient(
        AmazonSesMailTransportOptions options
    )
    {
        var regionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        var config = new AmazonSimpleEmailServiceV2Config
        {
            RegionEndpoint = regionEndpoint,
            RetryMode = RequestRetryMode.Standard,
            MaxErrorRetry = MaxErrorRetry,
            Timeout = options.Timeout,
        };
        return new AmazonSimpleEmailServiceV2Client(config);
    }

    /// <inheritdoc />
    public IAmazonSimpleEmailServiceV2 GetClient() => LazyClient.Value;

    /// <summary>
    /// Disposes the client when it has been created.
    /// </summary>
    public void Dispose()
    {
        if (LazyClient.IsValueCreated)
            LazyClient.Value.Dispose();
    }
}
