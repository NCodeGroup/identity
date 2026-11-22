// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Integration.Tests.Infrastructure;
using Xunit;

namespace NCode.Identity.OpenId.Integration.Tests.Endpoints;

public class ControlPlaneTenantDirectoryTests
{
    #region GetJwks Tests

    [Fact]
    public async Task GetJwks_WhenTheDirectoryCannotBeReached_ServesTheControlPlaneTenantsKeys()
    {
        // The directory accepts only tokens the control-plane cell's control-plane tenant issues, so that tenant is
        // created, judged and keyed without the directory (issue #918).
        using var factory = new HostApplicationFactory();
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(serviceCollection =>
            {
                serviceCollection.RemoveAll<ITenantDirectory>();
                serviceCollection.AddSingleton<ITenantDirectory, UnreachableTenantDirectory>();
            })
        );
        var client = host.CreateClient();

        using var response = await client.GetAsync("/oauth2/jwks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        var jsonElement = JsonElement.Parse(json);

        Assert.True(jsonElement.TryGetProperty("keys", out var keys));
        Assert.Equal(JsonValueKind.Array, keys.ValueKind);
        Assert.True(keys.GetArrayLength() >= 1);
    }

    #endregion

    /// <summary>
    /// An <see cref="ITenantDirectory"/> whose every call fails as an unreachable directory does, as the control-plane
    /// cell's remote directory does before the cell serves the token the directory requires.
    /// </summary>
    private sealed class UnreachableTenantDirectory : ITenantDirectory
    {
        /// <inheritdoc />
        public ValueTask<ReservationResult> ReserveAsync(
            TenantReservation reservation,
            CancellationToken cancellationToken
        ) => throw Unavailable();

        /// <inheritdoc />
        public ValueTask<ReservationResult> PlaceAsync(
            TenantPlacementRequest placement,
            CancellationToken cancellationToken
        ) => throw Unavailable();

        /// <inheritdoc />
        public ValueTask ReleaseAsync(string tenantId, CancellationToken cancellationToken) =>
            throw Unavailable();

        /// <inheritdoc />
        public ValueTask RetireAsync(
            TenantRetirement retirement,
            CancellationToken cancellationToken
        ) => throw Unavailable();

        /// <inheritdoc />
        public ValueTask<ReservationResult> ChangeIssuerAsync(
            TenantIssuerChange change,
            CancellationToken cancellationToken
        ) => throw Unavailable();

        /// <inheritdoc />
        public ValueTask<MoveOfferResult> OfferMoveAsync(
            string tenantId,
            string fromCell,
            string toCell,
            CancellationToken cancellationToken
        ) => throw Unavailable();

        /// <inheritdoc />
        public ValueTask<MoveResult> MoveAsync(
            string tenantId,
            string fromCell,
            string toCell,
            CancellationToken cancellationToken
        ) => throw Unavailable();

        /// <inheritdoc />
        public ValueTask<TenantPlacement?> GetPlacementOrDefaultAsync(
            string tenantId,
            CancellationToken cancellationToken
        ) => throw Unavailable();

        /// <inheritdoc />
        public ValueTask<bool> IsTenantIdRetiredAsync(
            string tenantId,
            CancellationToken cancellationToken
        ) => throw Unavailable();

        /// <inheritdoc />
        public ValueTask<string?> GetRetiredIssuerHolderOrDefaultAsync(
            string issuer,
            CancellationToken cancellationToken
        ) => throw Unavailable();

        private static DirectoryUnavailableException Unavailable() =>
            new("The directory cannot be reached.");
    }
}
