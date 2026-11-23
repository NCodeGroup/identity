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
using NCode.Identity.OpenId.Core.Tests.Accounts;
using NCode.Identity.OpenId.Principals;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Settings;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Endpoints.Token.Password;

public class DefaultCreatePasswordGrantSubjectHandlerTests : BaseTests
{
    private const string TenantId = "tenant-1";

    private Mock<IAuthenticationContextReferenceResolver> MockResolver { get; }
    private string? ResolvedAcr { get; set; }
    private DefaultCreatePasswordGrantSubjectHandler Handler { get; }

    public DefaultCreatePasswordGrantSubjectHandlerTests()
    {
        MockResolver = CreateStrictMock<IAuthenticationContextReferenceResolver>();
        MockResolver
            .Setup(x =>
                x.ResolveAsync(
                    It.IsAny<AuthenticationContextReferenceRequest>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(() => ValueTask.FromResult(ResolvedAcr))
            .Verifiable();

        var factory = new LocalAccountSubjectFactory(MockResolver.Object, TimeProvider.System);
        Handler = new DefaultCreatePasswordGrantSubjectHandler(factory);
    }

    #region Scaffolding

    private CreatePasswordGrantSubjectCommand CreateCommand(PersistedLocalAccount account)
    {
        var mockSettings = CreateStrictMock<IReadOnlySettingCollection>();
        mockSettings
            .Setup(x => x.GetValue(OpenIdSettingKeys.SubjectClaimTypes))
            .Returns(new[] { "sub" })
            .Verifiable();

        var mockTenant = CreateStrictMock<OpenIdTenant>();
        mockTenant.Setup(x => x.TenantId).Returns(TenantId).Verifiable();

        var mockContext = CreateStrictMock<OpenIdContext>();
        mockContext.Setup(x => x.Tenant).Returns(mockTenant.Object).Verifiable();

        var connection = LocalAccountStoreSetup.CreateConnection(TenantId, mockSettings.Object);
        return new CreatePasswordGrantSubjectCommand(mockContext.Object, connection, account);
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

    #region HandleAsync Tests

    [Fact]
    public async Task HandleAsync_StampsSubjectConnectionAndAccountClaims()
    {
        var command = CreateCommand(CreateAccount());

        var principal = await Handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal("account-1", principal.FindFirstValue("sub"));
        Assert.Equal(
            LocalAccountStoreSetup.ConnectionId,
            principal.FindFirstValue(ConnectionClaimTypes.Connection)
        );
        Assert.Equal(
            LocalAccountStoreSetup.ConnectionName,
            principal.FindFirstValue(JoseClaimNames.Payload.Idp)
        );
        Assert.Equal("admin", principal.FindFirstValue("role"));
    }

    [Fact]
    public async Task HandleAsync_StampsTheDisplayNameAsName()
    {
        var command = CreateCommand(CreateAccount());

        var principal = await Handler.HandleAsync(command, CancellationToken.None);

        var name = principal.FindFirstValue(JoseClaimNames.Payload.Name);

        Assert.Equal("Display Name", name);
    }

    [Fact]
    public async Task HandleAsync_DoesNotStampMetadataClaims()
    {
        var command = CreateCommand(CreateAccount());

        var principal = await Handler.HandleAsync(command, CancellationToken.None);

        // Metadata is a principal-level concept resolved at issuance, never stamped on the ROPC subject (ADR-1055).
        Assert.Null(principal.FindFirst(SubjectMetadataClaimTypes.ProfileMetadata));
        Assert.Null(principal.FindFirst(SubjectMetadataClaimTypes.SystemMetadata));
    }

    [Fact]
    public async Task HandleAsync_RecordsPasswordAuthenticationMethod()
    {
        var command = CreateCommand(CreateAccount());

        var principal = await Handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(
            OpenIdProtocol.AuthenticationMethods.Password,
            principal.FindFirstValue(JoseClaimNames.Payload.Amr)
        );
    }

    [Fact]
    public async Task HandleAsync_WhenResolverReturnsAcr_StampsAcr()
    {
        ResolvedAcr = OpenIdProtocol.AuthenticationContextClassReferences.MultiFactor;
        var command = CreateCommand(CreateAccount());

        var principal = await Handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(
            OpenIdProtocol.AuthenticationContextClassReferences.MultiFactor,
            principal.FindFirstValue(JoseClaimNames.Payload.Acr)
        );
    }

    [Fact]
    public async Task HandleAsync_WhenResolverReturnsNull_OmitsAcr()
    {
        ResolvedAcr = null;
        var command = CreateCommand(CreateAccount());

        var principal = await Handler.HandleAsync(command, CancellationToken.None);

        Assert.Null(principal.FindFirst(JoseClaimNames.Payload.Acr));
    }

    #endregion
}
