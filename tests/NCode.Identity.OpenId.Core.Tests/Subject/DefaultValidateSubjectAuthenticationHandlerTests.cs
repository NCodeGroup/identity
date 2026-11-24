// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Moq;
using NCode.Identity.Jose;
using NCode.Identity.Json;
using NCode.Identity.OpenId.Authentication.Clients;
using NCode.Identity.OpenId.Authentication.Endpoints.Authorization.Messages;
using NCode.Identity.OpenId.Authentication.Messages.Parameters;
using NCode.Identity.OpenId.Authentication.Messages.Parsers;
using NCode.Identity.OpenId.Authentication.Subject;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Errors;
using NCode.Identity.OpenId.Messages;
using NCode.Identity.OpenId.Messages.Parameters;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Registration.AspNetCore;
using NCode.Identity.Settings;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Subject;

public class DefaultValidateSubjectAuthenticationHandlerTests : BaseTests
{
    private const string TenantId = "tenant-1";

    private DefaultValidateSubjectAuthenticationHandler Handler { get; }
    private Mock<IConnectionResolver> MockConnectionResolver { get; }

    public DefaultValidateSubjectAuthenticationHandlerTests()
    {
        var mockLogger = CreateLooseMock<ILogger<DefaultValidateSubjectAuthenticationHandler>>();
        MockConnectionResolver = CreateStrictMock<IConnectionResolver>();
        Handler = new DefaultValidateSubjectAuthenticationHandler(
            TimeProvider.System,
            MockConnectionResolver.Object,
            mockLogger.Object
        );
    }

    #region Scaffolding

    private static SubjectAuthentication CreateSubjectAuthentication(
        string? tenantId,
        ClaimsPrincipal subject
    )
    {
        var properties = new AuthenticationProperties(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [OpenIdConstants.AuthenticationPropertyItems.TenantId] = tenantId,
            }
        );
        return new SubjectAuthentication("scheme", properties, subject, "subject-id");
    }

    private static ClaimsPrincipal CreateAuthenticatedSubject(string? acr)
    {
        var identity = new ClaimsIdentity(authenticationType: "pwd");
        if (acr is not null)
        {
            identity.AddClaim(new Claim(JoseClaimNames.Payload.Acr, acr));
        }

        return new ClaimsPrincipal(identity);
    }

    private static IParameterCollection CreateClaimsParameters(RequestClaims requestClaims)
    {
        var parameter = new Parameter<IRequestClaims>(
            new ParameterDescriptor(OpenIdAuthenticationParameters.Claims),
            AuthenticationParameterParsers.RequestClaims,
            requestClaims
        );
        return new ParameterCollection([parameter], cloneParameters: false);
    }

    private static IParameterCollection CreateMaxAgeParameters(TimeSpan maxAge)
    {
        var parameter = new Parameter<TimeSpan?>(
            new ParameterDescriptor(OpenIdCommonParameters.MaxAge),
            OpenIdCommonParameters.MaxAge.Parser,
            maxAge
        );
        return new ParameterCollection([parameter], cloneParameters: false);
    }

    private static ClaimsPrincipal CreateSubjectWithAuthTime(string authTime)
    {
        var identity = new ClaimsIdentity(authenticationType: "pwd");
        identity.AddClaim(new Claim(JoseClaimNames.Payload.AuthTime, authTime));
        return new ClaimsPrincipal(identity);
    }

    private static RequestClaims CreateEssentialAcrRequest(
        bool? essential,
        string? value = null,
        IEnumerable<string>? values = null
    ) =>
        new()
        {
            IdToken = new RequestClaimDictionary
            {
                [JoseClaimNames.Payload.Acr] = new RequestClaim
                {
                    Essential = essential,
                    Value = value,
                    Values = values,
                },
            },
        };

    private (
        ValidateSubjectAuthenticationCommand command,
        OperationDisposition<IOpenIdError> disposition,
        Mock<OpenIdContext> context,
        Mock<OpenIdTenant> tenant
    ) CreateCommand(
        SubjectAuthentication subjectAuthentication,
        IOpenIdError? initialError,
        IParameterCollection? requestParameters = null,
        IReadOnlyCollection<string>? allowedConnections = null
    )
    {
        var mockContext = CreateStrictMock<OpenIdContext>();
        var mockClient = CreateStrictMock<OpenIdClient>();
        var mockTenant = CreateStrictMock<OpenIdTenant>();
        var mockRequest = CreateStrictMock<IOpenIdRequest>();
        var mockSettings = CreateLooseMock<IReadOnlySettingCollection>();
        var mockErrorFactory = CreateLooseMock<IOpenIdErrorFactory>();
        var mockError = CreateLooseMock<IOpenIdError>();

        // ErrorFactory and Settings are read before the has-error short-circuit on every path;
        // Tenant/TenantId are read only past it, so the consuming tests set those up themselves.
        mockContext.SetupGet(x => x.ErrorFactory).Returns(mockErrorFactory.Object).Verifiable();
        mockClient.SetupGet(x => x.Settings).Returns(mockSettings.Object).Verifiable();
        mockErrorFactory.Setup(x => x.Create(It.IsAny<string>())).Returns(mockError.Object);

        if (allowedConnections is not null)
        {
            mockSettings
                .Setup(x =>
                    x.TryGetValue(OpenIdSettingKeys.AllowedConnections, out allowedConnections)
                )
                .Returns(true);
        }

        // Parameters are read only once a request reaches the max_age/acr checks past the early
        // short-circuits, so only those tests supply a collection.
        if (requestParameters is not null)
        {
            mockRequest.SetupGet(x => x.Parameters).Returns(requestParameters).Verifiable();
        }

        var disposition = new OperationDisposition<IOpenIdError> { Error = initialError };
        var command = new ValidateSubjectAuthenticationCommand(
            mockContext.Object,
            mockClient.Object,
            mockRequest.Object,
            subjectAuthentication,
            disposition,
            EnforceAuthenticationAge: true
        );

        return (command, disposition, mockContext, mockTenant);
    }

    private static void SetupTenant(Mock<OpenIdContext> mockContext, Mock<OpenIdTenant> mockTenant)
    {
        mockContext.SetupGet(x => x.Tenant).Returns(mockTenant.Object).Verifiable();
        mockTenant.SetupGet(x => x.TenantId).Returns(TenantId).Verifiable();
    }

    private static ClaimsPrincipal CreateSubjectWithConnection(string? connectionId)
    {
        var identity = new ClaimsIdentity(authenticationType: "pwd");
        if (connectionId is not null)
        {
            identity.AddClaim(new Claim(ConnectionClaimTypes.Connection, connectionId));
        }

        return new ClaimsPrincipal(identity);
    }

    private static IParameterCollection CreateAcrValuesParameters(params string[] acrValues)
    {
        var parameter = new Parameter<List<string>>(
            new ParameterDescriptor(OpenIdCommonParameters.AcrValues),
            OpenIdCommonParameters.AcrValues.Parser,
            [.. acrValues]
        );
        return new ParameterCollection([parameter], cloneParameters: false);
    }

    private static IParameterCollection CreateConnectionParameters(string connection)
    {
        var parameter = new Parameter<string>(
            new ParameterDescriptor(OpenIdCommonParameters.Connection),
            OpenIdCommonParameters.Connection.Parser,
            connection
        );
        return new ParameterCollection([parameter], cloneParameters: false);
    }

    private ResolvedConnection SetupConnection(string connectionId, string name, bool isEnabled)
    {
        var resolved = new ResolvedConnection
        {
            Connection = new PersistedConnection
            {
                TenantId = TenantId,
                ConnectionId = connectionId,
                Name = name,
                Strategy = ConnectionStrategyNames.Local,
                DisplayName = name,
                IsSystem = false,
                IsEnabled = isEnabled,
                Options = JsonElements.EmptyObject,
                Settings = new PersistedConnectionSettings
                {
                    TenantId = TenantId,
                    ConnectionId = connectionId,
                    Value = JsonElements.EmptyObject,
                },
            },
            Strategy = CreateLooseMock<IConnectionStrategy>().Object,
            Settings = CreateLooseMock<IReadOnlySettingCollection>().Object,
        };

        MockConnectionResolver
            .Setup(x =>
                x.GetByIdOrDefaultAsync(
                    It.IsAny<OpenIdContext>(),
                    connectionId,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(resolved);

        return resolved;
    }

    #endregion

    #region Connection Tests

    [Fact]
    public async Task HandleAsync_WhenNoConnectionClaimAndNoAllowList_Succeeds()
    {
        var subject = CreateSubjectAuthentication(TenantId, CreateSubjectWithConnection(null));
        var (command, disposition, mockContext, mockTenant) = CreateCommand(
            subject,
            initialError: null
        );
        SetupTenant(mockContext, mockTenant);

        await Handler.HandleAsync(command with { OpenIdRequest = null }, CancellationToken.None);

        Assert.False(disposition.HasError);
    }

    [Fact]
    public async Task HandleAsync_WhenConnectionIsListed_Succeeds()
    {
        SetupConnection("c1", "local", isEnabled: true);
        var subject = CreateSubjectAuthentication(TenantId, CreateSubjectWithConnection("c1"));
        var (command, disposition, mockContext, mockTenant) = CreateCommand(
            subject,
            initialError: null,
            allowedConnections: ["local"]
        );
        SetupTenant(mockContext, mockTenant);

        await Handler.HandleAsync(command with { OpenIdRequest = null }, CancellationToken.None);

        Assert.False(disposition.HasError);
    }

    [Fact]
    public async Task HandleAsync_WhenConnectionIsUnknown_SetsAccessDeniedError()
    {
        MockConnectionResolver
            .Setup(x =>
                x.GetByIdOrDefaultAsync(
                    It.IsAny<OpenIdContext>(),
                    "missing",
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync((ResolvedConnection?)null);
        var subject = CreateSubjectAuthentication(TenantId, CreateSubjectWithConnection("missing"));
        var (command, disposition, mockContext, mockTenant) = CreateCommand(
            subject,
            initialError: null
        );
        SetupTenant(mockContext, mockTenant);

        await Handler.HandleAsync(command with { OpenIdRequest = null }, CancellationToken.None);

        Assert.True(disposition.HasError);
    }

    [Fact]
    public async Task HandleAsync_WhenConnectionIsDisabled_SetsAccessDeniedError()
    {
        SetupConnection("c1", "local", isEnabled: false);
        var subject = CreateSubjectAuthentication(TenantId, CreateSubjectWithConnection("c1"));
        var (command, disposition, mockContext, mockTenant) = CreateCommand(
            subject,
            initialError: null
        );
        SetupTenant(mockContext, mockTenant);

        await Handler.HandleAsync(command with { OpenIdRequest = null }, CancellationToken.None);

        Assert.True(disposition.HasError);
    }

    [Fact]
    public async Task HandleAsync_WhenAllowListIsSetAndSubjectHasNoConnection_SetsAccessDeniedError()
    {
        var subject = CreateSubjectAuthentication(TenantId, CreateSubjectWithConnection(null));
        var (command, disposition, mockContext, mockTenant) = CreateCommand(
            subject,
            initialError: null,
            allowedConnections: ["local"]
        );
        SetupTenant(mockContext, mockTenant);

        await Handler.HandleAsync(command with { OpenIdRequest = null }, CancellationToken.None);

        Assert.True(disposition.HasError);
    }

    [Fact]
    public async Task HandleAsync_WhenConnectionIsNotListed_SetsAccessDeniedError()
    {
        SetupConnection("c1", "other", isEnabled: true);
        var subject = CreateSubjectAuthentication(TenantId, CreateSubjectWithConnection("c1"));
        var (command, disposition, mockContext, mockTenant) = CreateCommand(
            subject,
            initialError: null,
            allowedConnections: ["local"]
        );
        SetupTenant(mockContext, mockTenant);

        await Handler.HandleAsync(command with { OpenIdRequest = null }, CancellationToken.None);

        Assert.True(disposition.HasError);
    }

    [Fact]
    public async Task HandleAsync_WhenRequestedConnectionDiffers_SetsAccessDeniedError()
    {
        SetupConnection("c1", "local", isEnabled: true);
        var subject = CreateSubjectAuthentication(TenantId, CreateSubjectWithConnection("c1"));
        var parameters = CreateAcrValuesParameters("idp:other");
        var (command, disposition, mockContext, mockTenant) = CreateCommand(
            subject,
            initialError: null,
            parameters
        );
        SetupTenant(mockContext, mockTenant);

        await Handler.HandleAsync(command, CancellationToken.None);

        Assert.True(disposition.HasError);
    }

    [Fact]
    public async Task HandleAsync_WhenRequestedConnectionMatches_Succeeds()
    {
        SetupConnection("c1", "local", isEnabled: true);
        var subject = CreateSubjectAuthentication(TenantId, CreateSubjectWithConnection("c1"));
        var parameters = CreateAcrValuesParameters("idp:local");
        var (command, disposition, mockContext, mockTenant) = CreateCommand(
            subject,
            initialError: null,
            parameters
        );
        SetupTenant(mockContext, mockTenant);

        await Handler.HandleAsync(command, CancellationToken.None);

        Assert.False(disposition.HasError);
    }

    [Fact]
    public async Task HandleAsync_WhenRequestedConnectionParameterDiffersInCase_Succeeds()
    {
        SetupConnection("c1", "local", isEnabled: true);
        var subject = CreateSubjectAuthentication(TenantId, CreateSubjectWithConnection("c1"));
        var parameters = CreateConnectionParameters("LOCAL");
        var (command, disposition, mockContext, mockTenant) = CreateCommand(
            subject,
            initialError: null,
            parameters
        );
        SetupTenant(mockContext, mockTenant);

        await Handler.HandleAsync(command, CancellationToken.None);

        Assert.False(disposition.HasError);
    }

    [Fact]
    public async Task HandleAsync_WhenRequestedConnectionParameterDiffersInName_SetsAccessDeniedError()
    {
        SetupConnection("c1", "local", isEnabled: true);
        var subject = CreateSubjectAuthentication(TenantId, CreateSubjectWithConnection("c1"));
        var parameters = CreateConnectionParameters("other");
        var (command, disposition, mockContext, mockTenant) = CreateCommand(
            subject,
            initialError: null,
            parameters
        );
        SetupTenant(mockContext, mockTenant);

        await Handler.HandleAsync(command, CancellationToken.None);

        Assert.True(disposition.HasError);
    }

    [Fact]
    public async Task HandleAsync_WhenAllowedConnectionDiffersInCase_Succeeds()
    {
        SetupConnection("c1", "local", isEnabled: true);
        var subject = CreateSubjectAuthentication(TenantId, CreateSubjectWithConnection("c1"));
        var (command, disposition, mockContext, mockTenant) = CreateCommand(
            subject,
            initialError: null,
            allowedConnections: ["LOCAL"]
        );
        SetupTenant(mockContext, mockTenant);

        await Handler.HandleAsync(command with { OpenIdRequest = null }, CancellationToken.None);

        Assert.False(disposition.HasError);
    }

    #endregion

    #region HandleAsync Tests

    [Fact]
    public async Task HandleAsync_WhenAlreadyHasError_ShortCircuits()
    {
        var mockInitialError = CreateLooseMock<IOpenIdError>();
        var subject = CreateSubjectAuthentication(TenantId, new ClaimsPrincipal());
        var (command, disposition, _, _) = CreateCommand(subject, mockInitialError.Object);

        await Handler.HandleAsync(command, CancellationToken.None);

        Assert.Same(mockInitialError.Object, disposition.Error);
    }

    [Fact]
    public async Task HandleAsync_WhenTenantMismatch_SetsAccessDeniedError()
    {
        var subject = CreateSubjectAuthentication("other-tenant", new ClaimsPrincipal());
        var (command, disposition, mockContext, mockTenant) = CreateCommand(
            subject,
            initialError: null
        );
        SetupTenant(mockContext, mockTenant);

        await Handler.HandleAsync(command, CancellationToken.None);

        Assert.True(disposition.HasError);
    }

    [Fact]
    public async Task HandleAsync_WhenSubjectNotAuthenticated_SetsAccessDeniedError()
    {
        var subject = CreateSubjectAuthentication(
            TenantId,
            new ClaimsPrincipal(new ClaimsIdentity())
        );
        var (command, disposition, mockContext, mockTenant) = CreateCommand(
            subject,
            initialError: null
        );
        SetupTenant(mockContext, mockTenant);

        await Handler.HandleAsync(command, CancellationToken.None);

        Assert.True(disposition.HasError);
    }

    [Fact]
    public async Task HandleAsync_WhenThereIsNoRequest_SkipsTheRequestedConstraints()
    {
        var subject = CreateSubjectAuthentication(TenantId, CreateAuthenticatedSubject("acr-1"));
        var (command, disposition, mockContext, mockTenant) = CreateCommand(
            subject,
            initialError: null
        );
        SetupTenant(mockContext, mockTenant);

        await Handler.HandleAsync(command with { OpenIdRequest = null }, CancellationToken.None);

        Assert.False(disposition.HasError);
    }

    #endregion

    #region Essential acr Enforcement Tests

    [Fact]
    public async Task HandleAsync_WhenEssentialAcrSatisfiedViaValues_Succeeds()
    {
        var subject = CreateSubjectAuthentication(TenantId, CreateAuthenticatedSubject("acr-1"));
        var parameters = CreateClaimsParameters(
            CreateEssentialAcrRequest(essential: true, values: ["acr-1", "acr-2"])
        );
        var (command, disposition, mockContext, mockTenant) = CreateCommand(
            subject,
            initialError: null,
            parameters
        );
        SetupTenant(mockContext, mockTenant);

        await Handler.HandleAsync(command, CancellationToken.None);

        Assert.False(disposition.HasError);
    }

    [Fact]
    public async Task HandleAsync_WhenEssentialAcrSatisfiedViaSingleValue_Succeeds()
    {
        var subject = CreateSubjectAuthentication(TenantId, CreateAuthenticatedSubject("acr-1"));
        var parameters = CreateClaimsParameters(
            CreateEssentialAcrRequest(essential: true, value: "acr-1")
        );
        var (command, disposition, mockContext, mockTenant) = CreateCommand(
            subject,
            initialError: null,
            parameters
        );
        SetupTenant(mockContext, mockTenant);

        await Handler.HandleAsync(command, CancellationToken.None);

        Assert.False(disposition.HasError);
    }

    [Fact]
    public async Task HandleAsync_WhenEssentialAcrNotSatisfied_SetsAccessDeniedError()
    {
        var subject = CreateSubjectAuthentication(TenantId, CreateAuthenticatedSubject("acr-3"));
        var parameters = CreateClaimsParameters(
            CreateEssentialAcrRequest(essential: true, values: ["acr-1", "acr-2"])
        );
        var (command, disposition, mockContext, mockTenant) = CreateCommand(
            subject,
            initialError: null,
            parameters
        );
        SetupTenant(mockContext, mockTenant);

        await Handler.HandleAsync(command, CancellationToken.None);

        Assert.True(disposition.HasError);
    }

    [Fact]
    public async Task HandleAsync_WhenEssentialAcrRequestedButSubjectHasNone_SetsAccessDeniedError()
    {
        var subject = CreateSubjectAuthentication(TenantId, CreateAuthenticatedSubject(acr: null));
        var parameters = CreateClaimsParameters(
            CreateEssentialAcrRequest(essential: true, values: ["acr-1"])
        );
        var (command, disposition, mockContext, mockTenant) = CreateCommand(
            subject,
            initialError: null,
            parameters
        );
        SetupTenant(mockContext, mockTenant);

        await Handler.HandleAsync(command, CancellationToken.None);

        Assert.True(disposition.HasError);
    }

    [Fact]
    public async Task HandleAsync_WhenAcrRequestedButNotEssential_Succeeds()
    {
        var subject = CreateSubjectAuthentication(TenantId, CreateAuthenticatedSubject("acr-3"));
        var parameters = CreateClaimsParameters(
            CreateEssentialAcrRequest(essential: false, values: ["acr-1", "acr-2"])
        );
        var (command, disposition, mockContext, mockTenant) = CreateCommand(
            subject,
            initialError: null,
            parameters
        );
        SetupTenant(mockContext, mockTenant);

        await Handler.HandleAsync(command, CancellationToken.None);

        Assert.False(disposition.HasError);
    }

    [Fact]
    public async Task HandleAsync_WhenEssentialAcrWithoutSpecificValues_Succeeds()
    {
        var subject = CreateSubjectAuthentication(TenantId, CreateAuthenticatedSubject(acr: null));
        var parameters = CreateClaimsParameters(CreateEssentialAcrRequest(essential: true));
        var (command, disposition, mockContext, mockTenant) = CreateCommand(
            subject,
            initialError: null,
            parameters
        );
        SetupTenant(mockContext, mockTenant);

        await Handler.HandleAsync(command, CancellationToken.None);

        Assert.False(disposition.HasError);
    }

    #endregion

    #region auth_time NumericDate Tests

    [Fact]
    public async Task HandleAsync_WhenAuthTimeOutOfRangeAndMaxAgeRequested_SetsAccessDeniedError()
    {
        var subject = CreateSubjectAuthentication(
            TenantId,
            CreateSubjectWithAuthTime("99999999999999")
        );
        var parameters = CreateMaxAgeParameters(TimeSpan.FromHours(1));
        var (command, disposition, mockContext, mockTenant) = CreateCommand(
            subject,
            initialError: null,
            parameters
        );
        SetupTenant(mockContext, mockTenant);

        await Handler.HandleAsync(command, CancellationToken.None);

        Assert.True(disposition.HasError);
    }

    [Fact]
    public async Task HandleAsync_WhenFractionalAuthTimeWithinMaxAge_Succeeds()
    {
        var authSeconds = DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeSeconds();
        var authTime = string.Create(CultureInfo.InvariantCulture, $"{authSeconds}.5");
        var subject = CreateSubjectAuthentication(TenantId, CreateSubjectWithAuthTime(authTime));
        var parameters = CreateMaxAgeParameters(TimeSpan.FromHours(1));
        var (command, disposition, mockContext, mockTenant) = CreateCommand(
            subject,
            initialError: null,
            parameters
        );
        SetupTenant(mockContext, mockTenant);

        await Handler.HandleAsync(command, CancellationToken.None);

        Assert.False(disposition.HasError);
    }

    #endregion
}
