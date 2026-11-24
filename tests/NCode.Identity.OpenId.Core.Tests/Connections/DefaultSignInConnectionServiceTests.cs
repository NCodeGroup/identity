// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Moq;
using NCode.Identity.Json;
using NCode.Identity.OpenId.Authentication.Clients;
using NCode.Identity.OpenId.Authentication.Connections;
using NCode.Identity.OpenId.Authentication.Consent;
using NCode.Identity.OpenId.Authentication.Endpoints.Authorization.Messages;
using NCode.Identity.OpenId.Authentication.Persistence;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Settings;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Connections;

public class DefaultSignInConnectionServiceTests : BaseTests
{
    private const string ReturnUrl = "/continue?state=abc";
    private const string TenantId = "tenant-1";

    private sealed class TestStrategy(string name, string signInMethod) : IConnectionStrategy
    {
        public string Name { get; } = name;

        public string SignInMethod { get; } = signInMethod;

        public string? ValidateOptions(JsonElement options) => null;

        public string? ValidateOptionsChange(JsonElement current, JsonElement proposed) => null;
    }

    private Mock<PendingAuthorizationRequestReader> MockReader { get; }
    private Mock<IOpenIdClientResolver> MockClientResolver { get; }
    private Mock<IConnectionResolver> MockConnectionResolver { get; }
    private Mock<OpenIdContext> MockContext { get; }
    private Mock<IReadOnlySettingCollection> MockTenantSettings { get; }
    private DefaultSignInConnectionService Service { get; }

    public DefaultSignInConnectionServiceTests()
    {
        MockReader = CreateStrictMock<PendingAuthorizationRequestReader>(
            Mock.Of<IPersistedGrantService>()
        );
        MockClientResolver = CreateStrictMock<IOpenIdClientResolver>();
        MockConnectionResolver = CreateStrictMock<IConnectionResolver>();
        MockContext = CreateStrictMock<OpenIdContext>();
        MockTenantSettings = CreateStrictMock<IReadOnlySettingCollection>();

        var mockTenant = CreateStrictMock<OpenIdTenant>();
        var mockProvider = CreateStrictMock<IReadOnlySettingCollectionProvider>();
        mockProvider.SetupGet(x => x.Collection).Returns(MockTenantSettings.Object);
        mockTenant.SetupGet(x => x.SettingsProvider).Returns(mockProvider.Object);
        MockContext.SetupGet(x => x.Tenant).Returns(mockTenant.Object);

        Service = new DefaultSignInConnectionService(
            MockReader.Object,
            MockClientResolver.Object,
            MockConnectionResolver.Object
        );
    }

    private static ResolvedConnection Resolved(
        string name,
        string signInMethod = ConnectionSignInMethods.Password
    ) =>
        new()
        {
            Connection = new PersistedConnection
            {
                TenantId = TenantId,
                ConnectionId = $"id-{name}",
                Name = name,
                Strategy = "local",
                DisplayName = name,
                IsSystem = false,
                IsEnabled = true,
                Options = JsonElements.EmptyObject,
                Settings = new PersistedConnectionSettings
                {
                    TenantId = TenantId,
                    ConnectionId = $"id-{name}",
                    Value = JsonElements.EmptyObject,
                },
            },
            Strategy = new TestStrategy("local", signInMethod),
            Settings = Mock.Of<IReadOnlySettingCollection>(),
        };

    private void SetupAllowed(
        IReadOnlySettingCollection settings,
        params ResolvedConnection[] allowed
    ) =>
        MockConnectionResolver
            .Setup(x =>
                x.GetAllowedAsync(
                    MockContext.Object,
                    settings,
                    ConnectionSignInMethods.Password,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(allowed);

    private void SetupDefault(IReadOnlySettingCollection settings, ResolvedConnection? selected) =>
        MockConnectionResolver
            .Setup(x =>
                x.SelectAsync(MockContext.Object, settings, null, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(new ConnectionSelection { Connection = selected });

    // A parked request of a client with the settings, naming the connection and acr values.
    private IReadOnlySettingCollection SetupRequest(string? connection, string[] acrValues)
    {
        var mockRequest = CreateStrictMock<IAuthorizationRequest>();
        mockRequest.SetupGet(x => x.ClientId).Returns("client-1");
        mockRequest.SetupGet(x => x.Connection).Returns(connection);
        mockRequest.SetupGet(x => x.AcrValues).Returns(acrValues);
        MockReader
            .Setup(x => x.ReadAsync(MockContext.Object, ReturnUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockRequest.Object);

        var clientSettings = Mock.Of<IReadOnlySettingCollection>();
        var mockClient = CreateStrictMock<OpenIdClient>();
        mockClient.SetupGet(x => x.Settings).Returns(clientSettings);
        MockClientResolver
            .Setup(x =>
                x.ResolveClientOrDefaultAsync(
                    MockContext.Object,
                    "client-1",
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(mockClient.Object);
        return clientSettings;
    }

    [Fact]
    public async Task GetOfferedAsync_WhenNoReturnUrl_UsesTenantSettingsAndPreselectsTheDefault()
    {
        var local = Resolved("local");
        var other = Resolved("other");
        SetupAllowed(MockTenantSettings.Object, local, other);
        SetupDefault(MockTenantSettings.Object, local);

        var result = await Service.GetOfferedAsync(
            MockContext.Object,
            null,
            CancellationToken.None
        );

        Assert.Equal(["local", "other"], result.Offered.Select(x => x.Name));
        Assert.Same(local, result.Preselected);
    }

    [Fact]
    public async Task GetOfferedAsync_WhenNoDefaultAndSeveralOffered_PreselectsNothing()
    {
        SetupAllowed(MockTenantSettings.Object, Resolved("a"), Resolved("b"));
        SetupDefault(MockTenantSettings.Object, null);

        var result = await Service.GetOfferedAsync(
            MockContext.Object,
            null,
            CancellationToken.None
        );

        Assert.Equal(2, result.Offered.Count);
        Assert.Null(result.Preselected);
    }

    [Fact]
    public async Task GetOfferedAsync_WhenNoDefaultAndOneOffered_PreselectsIt()
    {
        var only = Resolved("a");
        SetupAllowed(MockTenantSettings.Object, only);
        SetupDefault(MockTenantSettings.Object, null);

        var result = await Service.GetOfferedAsync(
            MockContext.Object,
            null,
            CancellationToken.None
        );

        Assert.Same(only, result.Preselected);
    }

    [Fact]
    public async Task GetOfferedAsync_WhenTheRequestNamesAConnection_NarrowsToIt()
    {
        var clientSettings = SetupRequest("other", []);
        var local = Resolved("local");
        var other = Resolved("other");
        SetupAllowed(clientSettings, local, other);

        var result = await Service.GetOfferedAsync(
            MockContext.Object,
            ReturnUrl,
            CancellationToken.None
        );

        Assert.Same(other, Assert.Single(result.Offered));
        Assert.Same(other, result.Preselected);
    }

    [Fact]
    public async Task GetOfferedAsync_WhenTheRequestNamesAnIdpAcrValue_NarrowsToIt()
    {
        var clientSettings = SetupRequest(null, ["idp:other"]);
        var local = Resolved("local");
        var other = Resolved("other");
        SetupAllowed(clientSettings, local, other);

        var result = await Service.GetOfferedAsync(
            MockContext.Object,
            ReturnUrl,
            CancellationToken.None
        );

        Assert.Same(other, Assert.Single(result.Offered));
    }

    [Fact]
    public async Task GetOfferedAsync_WhenTheRequestedConnectionIsNotAllowed_OffersNothing()
    {
        var clientSettings = SetupRequest("missing", []);
        SetupAllowed(clientSettings, Resolved("local"));

        var result = await Service.GetOfferedAsync(
            MockContext.Object,
            ReturnUrl,
            CancellationToken.None
        );

        Assert.Empty(result.Offered);
        Assert.Null(result.Preselected);
    }

    [Fact]
    public async Task SelectAsync_WhenPostedNameIsOffered_SelectsIt()
    {
        var local = Resolved("local");
        var other = Resolved("other");
        SetupAllowed(MockTenantSettings.Object, local, other);
        SetupDefault(MockTenantSettings.Object, local);

        var result = await Service.SelectAsync(
            MockContext.Object,
            null,
            "other",
            CancellationToken.None
        );

        Assert.False(result.IsRefused);
        Assert.Same(other, result.Connection);
    }

    [Fact]
    public async Task SelectAsync_WhenPostedNameIsNotOffered_IsRefused()
    {
        SetupAllowed(MockTenantSettings.Object, Resolved("local"));
        SetupDefault(MockTenantSettings.Object, null);

        var result = await Service.SelectAsync(
            MockContext.Object,
            null,
            "missing",
            CancellationToken.None
        );

        Assert.True(result.IsRefused);
        Assert.Null(result.Connection);
    }

    [Fact]
    public async Task SelectAsync_WhenNoNamePosted_SelectsThePreselectedConnection()
    {
        var local = Resolved("local");
        SetupAllowed(MockTenantSettings.Object, local, Resolved("other"));
        SetupDefault(MockTenantSettings.Object, local);

        var result = await Service.SelectAsync(
            MockContext.Object,
            null,
            null,
            CancellationToken.None
        );

        Assert.Same(local, result.Connection);
    }

    [Fact]
    public async Task SelectAsync_WhenNoNamePostedAndNothingPreselected_IsRefused()
    {
        SetupAllowed(MockTenantSettings.Object, Resolved("a"), Resolved("b"));
        SetupDefault(MockTenantSettings.Object, null);

        var result = await Service.SelectAsync(
            MockContext.Object,
            null,
            null,
            CancellationToken.None
        );

        Assert.Equal("No connection is available for the sign-in.", result.ErrorDescription);
    }
}
