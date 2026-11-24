// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Moq;
using NCode.Identity.Jose;
using NCode.Identity.OpenId.Accounts.Authentication;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Authentication.Subject;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Settings;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Accounts;

public class LocalAccountSubjectFactoryTests : BaseTests
{
    private const string TenantId = "tenant-1";
    private const string AuthenticationType = "test-authentication-type";

    private Mock<IAuthenticationContextReferenceResolver> MockResolver { get; }
    private LocalAccountSubjectFactory Factory { get; }

    public LocalAccountSubjectFactoryTests()
    {
        MockResolver = CreateStrictMock<IAuthenticationContextReferenceResolver>();
        MockResolver
            .Setup(x =>
                x.ResolveAsync(
                    It.IsAny<AuthenticationContextReferenceRequest>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(() => ValueTask.FromResult<string?>(null))
            .Verifiable();

        Factory = new LocalAccountSubjectFactory(MockResolver.Object, TimeProvider.System);
    }

    #region Scaffolding

    private OpenIdContext CreateContext()
    {
        var mockTenant = CreateStrictMock<OpenIdTenant>();
        mockTenant.Setup(x => x.TenantId).Returns(TenantId).Verifiable();

        var mockContext = CreateStrictMock<OpenIdContext>();
        mockContext.Setup(x => x.Tenant).Returns(mockTenant.Object).Verifiable();

        return mockContext.Object;
    }

    private ResolvedConnection CreateConnection()
    {
        var subjectClaimTypes = new[] { "sub" };
        var mockSettings = CreateStrictMock<IReadOnlySettingCollection>();
        mockSettings
            .Setup(x => x.GetValue(OpenIdSettingKeys.SubjectClaimTypes))
            .Returns(subjectClaimTypes)
            .Verifiable();

        return LocalAccountStoreSetup.CreateConnection(TenantId, mockSettings.Object);
    }

    private static PersistedLocalAccount CreateAccount() =>
        new()
        {
            TenantId = string.Empty,
            LocalAccountId = "account-1",
            ConnectionId = LocalAccountStoreSetup.ConnectionId,
            UserName = "user-1",
            DisplayName = "Display Name",
            Email = null,
            EmailVerified = false,
            IsEnabled = true,
            Claims = [new PersistedLocalAccountClaim { Type = "role", Value = "admin" }],
            ConcurrencyToken = string.Empty,
        };

    #endregion

    #region CreateAsync Tests

    [Fact]
    public async Task CreateAsync_StampsSubjectConnectionTenantAndAccountClaims()
    {
        var context = CreateContext();
        var connection = CreateConnection();
        var account = CreateAccount();

        var principal = await Factory.CreateAsync(
            context,
            connection,
            account,
            AuthenticationType,
            CancellationToken.None
        );

        var subject = principal.FindFirstValue("sub");
        var connectionId = principal.FindFirstValue(ConnectionClaimTypes.Connection);
        var idp = principal.FindFirstValue(JoseClaimNames.Payload.Idp);
        var tenantId = principal.FindFirstValue(JoseClaimNames.Payload.Tid);
        var role = principal.FindFirstValue("role");
        Assert.Equal("account-1", subject);
        Assert.Equal(LocalAccountStoreSetup.ConnectionId, connectionId);
        Assert.Equal(LocalAccountStoreSetup.ConnectionName, idp);
        Assert.Equal(TenantId, tenantId);
        Assert.Equal("admin", role);
    }

    [Fact]
    public async Task CreateAsync_WhenTheAccountCarriesConnectionOrIdpClaims_DropsThem()
    {
        var context = CreateContext();
        var connection = CreateConnection();
        var account = CreateAccount();
        account.Claims =
        [
            new PersistedLocalAccountClaim
            {
                Type = ConnectionClaimTypes.Connection,
                Value = "other-connection",
            },
            new PersistedLocalAccountClaim { Type = JoseClaimNames.Payload.Idp, Value = "other" },
            new PersistedLocalAccountClaim { Type = "role", Value = "admin" },
        ];

        var principal = await Factory.CreateAsync(
            context,
            connection,
            account,
            AuthenticationType,
            CancellationToken.None
        );

        var connectionClaims = principal.FindAll(ConnectionClaimTypes.Connection).ToList();
        var idpClaims = principal.FindAll(JoseClaimNames.Payload.Idp).ToList();
        var connectionClaim = Assert.Single(connectionClaims);
        var idpClaim = Assert.Single(idpClaims);
        Assert.Equal(LocalAccountStoreSetup.ConnectionId, connectionClaim.Value);
        Assert.Equal(LocalAccountStoreSetup.ConnectionName, idpClaim.Value);
        Assert.Equal("admin", principal.FindFirstValue("role"));
    }

    [Fact]
    public async Task CreateAsync_StampsTheDisplayNameAsName()
    {
        var context = CreateContext();
        var connection = CreateConnection();
        var account = CreateAccount();

        var principal = await Factory.CreateAsync(
            context,
            connection,
            account,
            AuthenticationType,
            CancellationToken.None
        );

        var name = principal.FindFirstValue(JoseClaimNames.Payload.Name);
        Assert.Equal("Display Name", name);
    }

    [Fact]
    public async Task CreateAsync_RecordsPasswordAuthenticationMethod()
    {
        var context = CreateContext();
        var connection = CreateConnection();
        var account = CreateAccount();

        var principal = await Factory.CreateAsync(
            context,
            connection,
            account,
            AuthenticationType,
            CancellationToken.None
        );

        var amr = principal.FindFirstValue(JoseClaimNames.Payload.Amr);
        Assert.Equal(OpenIdProtocol.AuthenticationMethods.Password, amr);
    }

    [Fact]
    public async Task CreateAsync_UsesTheGivenAuthenticationType()
    {
        var context = CreateContext();
        var connection = CreateConnection();
        var account = CreateAccount();

        var principal = await Factory.CreateAsync(
            context,
            connection,
            account,
            AuthenticationType,
            CancellationToken.None
        );

        var authenticationType = principal.Identity?.AuthenticationType;
        Assert.Equal(AuthenticationType, authenticationType);
    }

    #endregion
}
