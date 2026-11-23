// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Moq;
using NCode.Identity.Json;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Core.Tests;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.PrincipalResolution;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Settings;
using Xunit;

namespace NCode.Identity.OpenId.Core.PrincipalResolution;

public sealed class DefaultFederatedIdentityLinkingPolicyTests : BaseTests
{
    private const string JoinKey = "user@example.com";
    private const string TenantId = "tenant-1";
    private const string ConnectionId = "connection-1";
    private const string MatchedPrincipalId = "principal-9";

    // The policy reads its settings from the connection in order and stops at the first one that settles the decision:
    // a subject without a join claim reads no verification setting, and an unverified one never reads whether linking
    // is explicit-only.
    private ResolvedConnection CreateConnection(
        bool explicitOnly = false,
        bool requireVerified = true,
        bool readsVerification = true,
        bool readsExplicitOnly = true
    )
    {
        var settings = CreateStrictMock<IReadOnlySettingCollection>();
        settings
            .Setup(x => x.GetValue(OpenIdSettingKeys.FederatedIdentityJoinClaim))
            .Returns("email")
            .Verifiable();
        if (readsVerification)
        {
            settings
                .Setup(x => x.GetValue(OpenIdSettingKeys.FederatedIdentityVerifiedClaim))
                .Returns("email_verified")
                .Verifiable();
            settings
                .Setup(x => x.GetValue(OpenIdSettingKeys.FederatedIdentityRequireVerified))
                .Returns(requireVerified)
                .Verifiable();
        }

        if (readsVerification && readsExplicitOnly)
        {
            settings
                .Setup(x => x.GetValue(OpenIdSettingKeys.FederatedIdentityExplicitOnly))
                .Returns(explicitOnly)
                .Verifiable();
        }

        return new ResolvedConnection
        {
            Connection = new PersistedConnection
            {
                TenantId = TenantId,
                ConnectionId = ConnectionId,
                Name = "upstream",
                Strategy = ConnectionStrategyNames.Local,
                DisplayName = "Upstream",
                IsEnabled = true,
                Options = JsonElements.EmptyObject,
                Settings = new PersistedConnectionSettings
                {
                    TenantId = TenantId,
                    ConnectionId = ConnectionId,
                    Value = JsonElements.EmptyObject,
                },
            },
            Strategy = CreateLooseMock<IConnectionStrategy>().Object,
            Settings = settings.Object,
        };
    }

    private OpenIdContext CreateOpenIdContext() => CreateStrictMock<OpenIdContext>().Object;

    private static ClaimsPrincipal CreateUser(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "test"));

    private static ClaimsPrincipal VerifiedUser(string email = JoinKey) =>
        CreateUser(new Claim("email", email), new Claim("email_verified", "true"));

    private static PersistedFederatedIdentity Identity(string principalId) =>
        new()
        {
            TenantId = TenantId,
            FederatedIdentityId = "identity-1",
            PrincipalId = principalId,
            ConnectionId = ConnectionId,
            Issuer = "https://issuer.example",
            Subject = "subject-1",
            JoinKey = JoinKey,
            ConcurrencyToken = string.Empty,
        };

    [Fact]
    public async Task ResolveLinkAsync_WhenNoJoinClaim_ReturnsEmptyDecision()
    {
        var manager = CreateStrictMock<IStoreManager>();

        var openIdContext = CreateOpenIdContext();
        var connection = CreateConnection(readsVerification: false);
        var policy = new DefaultFederatedIdentityLinkingPolicy();

        var decision = await policy.ResolveLinkAsync(
            openIdContext,
            connection,
            CreateUser(),
            manager.Object,
            CancellationToken.None
        );

        Assert.Null(decision.LinkedPrincipalId);
        Assert.Null(decision.JoinKey);
    }

    [Fact]
    public async Task ResolveLinkAsync_WhenUnverifiedAndVerificationRequired_ReturnsEmptyDecision()
    {
        var manager = CreateStrictMock<IStoreManager>();

        var openIdContext = CreateOpenIdContext();
        var connection = CreateConnection(readsExplicitOnly: false);
        var policy = new DefaultFederatedIdentityLinkingPolicy();

        var decision = await policy.ResolveLinkAsync(
            openIdContext,
            connection,
            CreateUser(new Claim("email", JoinKey), new Claim("email_verified", "false")),
            manager.Object,
            CancellationToken.None
        );

        // An unverified join key is neither recorded nor matchable.
        Assert.Null(decision.LinkedPrincipalId);
        Assert.Null(decision.JoinKey);
    }

    [Fact]
    public async Task ResolveLinkAsync_WhenExplicitOnly_RecordsJoinKeyButDoesNotLink()
    {
        var manager = CreateStrictMock<IStoreManager>();

        var openIdContext = CreateOpenIdContext();
        var connection = CreateConnection(explicitOnly: true);
        var policy = new DefaultFederatedIdentityLinkingPolicy();

        var decision = await policy.ResolveLinkAsync(
            openIdContext,
            connection,
            VerifiedUser(),
            manager.Object,
            CancellationToken.None
        );

        Assert.Null(decision.LinkedPrincipalId);
        Assert.Equal(JoinKey, decision.JoinKey);
    }

    [Fact]
    public async Task ResolveLinkAsync_WhenVerifiedMatch_AttachesToExistingPrincipal()
    {
        var manager = CreateStrictMock<IStoreManager>();
        var identityStore = CreateStrictMock<IFederatedIdentityStore>();

        manager
            .Setup(x => x.GetStore<IFederatedIdentityStore>())
            .Returns(identityStore.Object)
            .Verifiable();
        identityStore
            .Setup(x => x.GetByJoinKeyAsync(JoinKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Identity(MatchedPrincipalId)])
            .Verifiable();

        var openIdContext = CreateOpenIdContext();
        var connection = CreateConnection();
        var policy = new DefaultFederatedIdentityLinkingPolicy();

        var decision = await policy.ResolveLinkAsync(
            openIdContext,
            connection,
            VerifiedUser(),
            manager.Object,
            CancellationToken.None
        );

        Assert.Equal(MatchedPrincipalId, decision.LinkedPrincipalId);
        Assert.Equal(JoinKey, decision.JoinKey);
    }

    [Fact]
    public async Task ResolveLinkAsync_WhenVerifiedNoMatch_RecordsJoinKeyWithoutLink()
    {
        var manager = CreateStrictMock<IStoreManager>();
        var identityStore = CreateStrictMock<IFederatedIdentityStore>();

        manager
            .Setup(x => x.GetStore<IFederatedIdentityStore>())
            .Returns(identityStore.Object)
            .Verifiable();
        identityStore
            .Setup(x => x.GetByJoinKeyAsync(JoinKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .Verifiable();

        var openIdContext = CreateOpenIdContext();
        var connection = CreateConnection();
        var policy = new DefaultFederatedIdentityLinkingPolicy();

        var decision = await policy.ResolveLinkAsync(
            openIdContext,
            connection,
            VerifiedUser(),
            manager.Object,
            CancellationToken.None
        );

        Assert.Null(decision.LinkedPrincipalId);
        Assert.Equal(JoinKey, decision.JoinKey);
    }
}
