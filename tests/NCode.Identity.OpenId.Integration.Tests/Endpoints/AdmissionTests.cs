// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using NCode.Identity.OpenId.Integration.Tests.Infrastructure;
using NCode.Identity.OpenId.Settings;
using Xunit;

namespace NCode.Identity.OpenId.Integration.Tests.Endpoints;

/// <summary>
/// Drives the real host with a concurrency limit and confirms that the admission gate admits each request that arrives
/// after the one before it completed.
/// </summary>
// See ADR-1523.
public class AdmissionTests
{
    private const string TokenPath = "/oauth2/token";

    private static HttpClient CreateClient(HostApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static FormUrlEncodedContent AnonymousBody() =>
        new(
            new Dictionary<string, string>
            {
                ["grant_type"] = OpenIdProtocol.GrantTypes.ClientCredentials,
            }
        );

    private static async ValueTask AssertEveryRequestIsAdmittedAsync(
        HttpClient client,
        CancellationToken cancellationToken
    )
    {
        for (var i = 0; i < 3; ++i)
        {
            using var content = AnonymousBody();
            using var response = await client.PostAsync(TokenPath, content, cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            Assert.True(response.StatusCode != HttpStatusCode.ServiceUnavailable, body);
        }
    }

    #region Admission Tests

    [Fact]
    public async Task PostToken_WhenEachRequestCompletesBeforeTheNext_AdmitsEveryRequest()
    {
        // A limit of one at half the capacity allows one request in flight, so each request is admitted only if the
        // one before it released its place.
        using var factory = new ConfiguredHostApplicationFactory(
            new Dictionary<string, string?>
            {
                ["OpenId:Admission:ConcurrencyLimit"] = "1",
                [$"OpenId:Platform:Settings:{OpenIdSettingNames.AdmissionCapacityPercent}"] = "50",
            }
        );
        var client = CreateClient(factory);

        await AssertEveryRequestIsAdmittedAsync(client, CancellationToken.None);
    }

    [Fact]
    public async Task PostToken_WithTheDefaultOptions_AdmitsEveryRequest()
    {
        using var factory = new HostApplicationFactory();
        var client = CreateClient(factory);

        await AssertEveryRequestIsAdmittedAsync(client, CancellationToken.None);
    }

    #endregion
}
