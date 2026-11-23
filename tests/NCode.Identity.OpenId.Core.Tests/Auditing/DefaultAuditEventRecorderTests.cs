// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Moq;
using NCode.Identity.Events;
using NCode.Identity.Events.Audit;
using NCode.Identity.OpenId.Authentication.Auditing;
using NCode.Identity.OpenId.Authentication.Clients;
using NCode.Identity.OpenId.Authentication.Logging;
using NCode.Identity.OpenId.Authentication.Sessions;
using NCode.Identity.OpenId.Authentication.Tokens.Models;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Messages;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Settings;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Auditing;

public sealed class DefaultAuditEventRecorderTests : IDisposable
{
    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);

    public void Dispose() => MockRepository.Verify();

    private static DefaultAuditEventRecorder CreateRecorder(
        IEventPublisher eventPublisher,
        ILogger<DefaultAuditEventRecorder>? logger = null
    ) =>
        new(
            eventPublisher,
            TimeProvider.System,
            logger ?? NullLogger<DefaultAuditEventRecorder>.Instance
        );

    private static DefaultHttpContext CreateHttpContext(string endpointName)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.SetEndpoint(
            new Endpoint(
                null,
                new EndpointMetadataCollection(new EndpointNameMetadata(endpointName)),
                endpointName
            )
        );
        return httpContext;
    }

    private void SetupTenantEnvironmentType(Mock<OpenIdTenant> mockTenant, string environmentType)
    {
        var mockSettingsProvider = MockRepository.Create<IReadOnlySettingCollectionProvider>();
        var mockSettings = MockRepository.Create<IReadOnlySettingCollection>();

        mockTenant.Setup(x => x.SettingsProvider).Returns(mockSettingsProvider.Object).Verifiable();
        mockSettingsProvider.Setup(x => x.Collection).Returns(mockSettings.Object).Verifiable();
        mockSettings
            .Setup(x => x.GetValue(OpenIdSettingKeys.EnvironmentType))
            .Returns(environmentType)
            .Verifiable();
    }

    #region RecordTokenIssuedAsync Tests

    [Fact]
    public async Task RecordTokenIssuedAsync_PublishesTokenIssuedAuditEventWithMetadata()
    {
        var mockContext = MockRepository.Create<OpenIdContext>();
        var mockTenant = MockRepository.Create<OpenIdTenant>();
        var mockClient = MockRepository.Create<OpenIdClient>();
        var mockPublisher = MockRepository.Create<IEventPublisher>();

        mockContext.Setup(x => x.Tenant).Returns(mockTenant.Object).Verifiable();
        mockTenant.Setup(x => x.TenantId).Returns("tenant-1").Verifiable();
        SetupTenantEnvironmentType(mockTenant, "staging");
        mockContext.Setup(x => x.Http).Returns(CreateHttpContext("api/token")).Verifiable();
        mockClient.Setup(x => x.ClientId).Returns("client-1").Verifiable();

        TokenIssuedAuditEvent? captured = null;
        mockPublisher
            .Setup(x =>
                x.PublishAsync(It.IsAny<TokenIssuedAuditEvent>(), It.IsAny<CancellationToken>())
            )
            .Callback(
                (TokenIssuedAuditEvent auditEvent, CancellationToken _) => captured = auditEvent
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var recorder = CreateRecorder(mockPublisher.Object);

        var securityToken = new SecurityToken
        {
            TokenType = "access_token",
            TokenValue = "the-secret-token-value",
            TokenLifetime = default,
        };

        await recorder.RecordTokenIssuedAsync(
            mockContext.Object,
            mockClient.Object,
            "subject-1",
            securityToken,
            CancellationToken.None
        );

        Assert.NotNull(captured);
        Assert.Equal("token.issued", captured.EventName);
        Assert.Equal(AuditEventSources.OpenId, captured.EventSource);
        Assert.Equal(AuditOutcome.Success, captured.Outcome);
        Assert.Equal("access_token", captured.TokenType);
        Assert.Equal("client-1", captured.ClientId);
        Assert.Equal("tenant-1", captured.TenantId);
        Assert.Equal("staging", captured.TenantEnvironmentType);
        Assert.Equal("subject-1", captured.SubjectId);
        Assert.Equal(AuditActor.Client("client-1"), captured.Actor);
        Assert.Equal("api/token", captured.Request?.Endpoint);
        Assert.Equal(7, captured.EventId.Version);
    }

    #endregion

    #region RecordTokenRevokedAsync Tests

    [Fact]
    public async Task RecordTokenRevokedAsync_PublishesTokenRevokedAuditEventWithMetadata()
    {
        var mockContext = MockRepository.Create<OpenIdContext>();
        var mockTenant = MockRepository.Create<OpenIdTenant>();
        var mockClient = MockRepository.Create<OpenIdClient>();
        var mockPublisher = MockRepository.Create<IEventPublisher>();

        mockContext.Setup(x => x.Tenant).Returns(mockTenant.Object).Verifiable();
        mockTenant.Setup(x => x.TenantId).Returns("tenant-1").Verifiable();
        SetupTenantEnvironmentType(mockTenant, "production");
        mockContext.Setup(x => x.Http).Returns(CreateHttpContext("api/token")).Verifiable();
        mockClient.Setup(x => x.ClientId).Returns("client-1").Verifiable();

        TokenRevokedAuditEvent? captured = null;
        mockPublisher
            .Setup(x =>
                x.PublishAsync(It.IsAny<TokenRevokedAuditEvent>(), It.IsAny<CancellationToken>())
            )
            .Callback(
                (TokenRevokedAuditEvent auditEvent, CancellationToken _) => captured = auditEvent
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var recorder = CreateRecorder(mockPublisher.Object);

        await recorder.RecordTokenRevokedAsync(
            mockContext.Object,
            mockClient.Object,
            "subject-1",
            CancellationToken.None
        );

        Assert.NotNull(captured);
        Assert.Equal("token.revoked", captured.EventName);
        Assert.Equal(AuditEventSources.OpenId, captured.EventSource);
        Assert.Equal(AuditOutcome.Success, captured.Outcome);
        Assert.Equal("client-1", captured.ClientId);
        Assert.Equal("tenant-1", captured.TenantId);
        Assert.Equal("production", captured.TenantEnvironmentType);
        Assert.Equal("subject-1", captured.SubjectId);
        Assert.Equal(AuditActor.Client("client-1"), captured.Actor);
        Assert.Equal("api/token", captured.Request?.Endpoint);
        Assert.Equal(7, captured.EventId.Version);
    }

    #endregion

    #region RecordSubjectAuthenticatedAsync Tests

    [Fact]
    public async Task RecordSubjectAuthenticatedAsync_PublishesSuccessAuditEventWithSubject()
    {
        var mockContext = MockRepository.Create<OpenIdContext>();
        var mockTenant = MockRepository.Create<OpenIdTenant>();
        var mockPublisher = MockRepository.Create<IEventPublisher>();

        mockContext.Setup(x => x.Tenant).Returns(mockTenant.Object).Verifiable();
        mockTenant.Setup(x => x.TenantId).Returns("tenant-1").Verifiable();
        SetupTenantEnvironmentType(mockTenant, "production");
        mockContext.Setup(x => x.Http).Returns(CreateHttpContext("api/authorize")).Verifiable();

        SubjectAuthenticationAuditEvent? captured = null;
        mockPublisher
            .Setup(x =>
                x.PublishAsync(
                    It.IsAny<SubjectAuthenticationAuditEvent>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(
                (SubjectAuthenticationAuditEvent auditEvent, CancellationToken _) =>
                    captured = auditEvent
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var recorder = CreateRecorder(mockPublisher.Object);

        await recorder.RecordSubjectAuthenticatedAsync(
            mockContext.Object,
            "subject-1",
            CancellationToken.None
        );

        Assert.NotNull(captured);
        Assert.Equal("subject.authentication", captured.EventName);
        Assert.Equal(AuditEventSources.OpenId, captured.EventSource);
        Assert.Equal(AuditOutcome.Success, captured.Outcome);
        Assert.Equal("tenant-1", captured.TenantId);
        Assert.Equal("production", captured.TenantEnvironmentType);
        Assert.Equal("subject-1", captured.SubjectId);
        Assert.Equal(AuditActor.EndUser("subject-1"), captured.Actor);
        Assert.Equal("api/authorize", captured.Request?.Endpoint);
        Assert.Null(captured.Error);
        Assert.Equal(7, captured.EventId.Version);
    }

    #endregion

    #region RecordSubjectAuthenticationFailedAsync Tests

    [Fact]
    public async Task RecordSubjectAuthenticationFailedAsync_PublishesFailureAuditEventWithError()
    {
        var mockContext = MockRepository.Create<OpenIdContext>();
        var mockTenant = MockRepository.Create<OpenIdTenant>();
        var mockPublisher = MockRepository.Create<IEventPublisher>();

        mockContext.Setup(x => x.Tenant).Returns(mockTenant.Object).Verifiable();
        mockTenant.Setup(x => x.TenantId).Returns("tenant-1").Verifiable();
        SetupTenantEnvironmentType(mockTenant, "production");
        mockContext.Setup(x => x.Http).Returns(CreateHttpContext("api/authorize")).Verifiable();

        SubjectAuthenticationAuditEvent? captured = null;
        mockPublisher
            .Setup(x =>
                x.PublishAsync(
                    It.IsAny<SubjectAuthenticationAuditEvent>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(
                (SubjectAuthenticationAuditEvent auditEvent, CancellationToken _) =>
                    captured = auditEvent
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var recorder = CreateRecorder(mockPublisher.Object);

        await recorder.RecordSubjectAuthenticationFailedAsync(
            mockContext.Object,
            "subject_id_missing",
            "Unable to determine the end-user's subject id.",
            attemptedUsername: "attempted-user",
            CancellationToken.None
        );

        Assert.NotNull(captured);
        Assert.Equal("subject.authentication", captured.EventName);
        Assert.Equal(AuditEventSources.OpenId, captured.EventSource);
        Assert.Equal(AuditOutcome.Failure, captured.Outcome);
        Assert.Equal("tenant-1", captured.TenantId);
        Assert.Equal("production", captured.TenantEnvironmentType);
        Assert.Null(captured.SubjectId);
        Assert.Equal("attempted-user", captured.AttemptedUsername);
        Assert.Equal(AuditActor.Anonymous, captured.Actor);
        Assert.Equal("api/authorize", captured.Request?.Endpoint);
        Assert.Equal("subject_id_missing", captured.Error?.Code);
        Assert.Equal("Unable to determine the end-user's subject id.", captured.Error?.Message);
        Assert.Equal(7, captured.EventId.Version);
    }

    #endregion

    #region Authorization Decision Tests

    [Fact]
    public async Task RecordAuthorizationGrantedAsync_PublishesGrantedAuditEvent()
    {
        var mockContext = MockRepository.Create<OpenIdContext>();
        var mockTenant = MockRepository.Create<OpenIdTenant>();
        var mockClient = MockRepository.Create<OpenIdClient>();
        var mockPublisher = MockRepository.Create<IEventPublisher>();

        mockContext.Setup(x => x.Tenant).Returns(mockTenant.Object).Verifiable();
        mockTenant.Setup(x => x.TenantId).Returns("tenant-1").Verifiable();
        SetupTenantEnvironmentType(mockTenant, "production");
        mockContext.Setup(x => x.Http).Returns(CreateHttpContext("api/authorize")).Verifiable();
        mockClient.Setup(x => x.ClientId).Returns("client-1").Verifiable();

        AuthorizationGrantedAuditEvent? captured = null;
        mockPublisher
            .Setup(x =>
                x.PublishAsync(
                    It.IsAny<AuthorizationGrantedAuditEvent>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(
                (AuthorizationGrantedAuditEvent auditEvent, CancellationToken _) =>
                    captured = auditEvent
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var recorder = CreateRecorder(mockPublisher.Object);

        await recorder.RecordAuthorizationGrantedAsync(
            mockContext.Object,
            mockClient.Object,
            "subject-1",
            CancellationToken.None
        );

        Assert.NotNull(captured);
        Assert.Equal("authorization.granted", captured.EventName);
        Assert.Equal(AuditEventSources.OpenId, captured.EventSource);
        Assert.Equal(AuditOutcome.Success, captured.Outcome);
        Assert.Equal("subject-1", captured.SubjectId);
        Assert.Equal(AuditActor.EndUser("subject-1"), captured.Actor);
        Assert.Equal("api/authorize", captured.Request?.Endpoint);
        Assert.Null(captured.Error);
        Assert.Equal(7, captured.EventId.Version);
    }

    [Fact]
    public async Task RecordAuthorizationDeniedAsync_PublishesDeniedAuditEventWithError()
    {
        var mockContext = MockRepository.Create<OpenIdContext>();
        var mockTenant = MockRepository.Create<OpenIdTenant>();
        var mockClient = MockRepository.Create<OpenIdClient>();
        var mockPublisher = MockRepository.Create<IEventPublisher>();

        mockContext.Setup(x => x.Tenant).Returns(mockTenant.Object).Verifiable();
        mockTenant.Setup(x => x.TenantId).Returns("tenant-1").Verifiable();
        SetupTenantEnvironmentType(mockTenant, "production");
        mockContext.Setup(x => x.Http).Returns(CreateHttpContext("api/authorize")).Verifiable();
        mockClient.Setup(x => x.ClientId).Returns("client-1").Verifiable();

        AuthorizationDeniedAuditEvent? captured = null;
        mockPublisher
            .Setup(x =>
                x.PublishAsync(
                    It.IsAny<AuthorizationDeniedAuditEvent>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(
                (AuthorizationDeniedAuditEvent auditEvent, CancellationToken _) =>
                    captured = auditEvent
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var recorder = CreateRecorder(mockPublisher.Object);

        await recorder.RecordAuthorizationDeniedAsync(
            mockContext.Object,
            mockClient.Object,
            "subject-1",
            "login_required",
            CancellationToken.None
        );

        Assert.NotNull(captured);
        Assert.Equal("authorization.denied", captured.EventName);
        Assert.Equal(AuditEventSources.OpenId, captured.EventSource);
        Assert.Equal(AuditOutcome.Denied, captured.Outcome);
        Assert.Equal(AuditActor.EndUser("subject-1"), captured.Actor);
        Assert.Equal("api/authorize", captured.Request?.Endpoint);
        Assert.Equal("login_required", captured.Error?.Code);
        Assert.Equal(7, captured.EventId.Version);
    }

    #endregion

    #region Client Authentication Tests

    [Fact]
    public async Task RecordClientAuthenticationFailedAsync_PublishesFailedAuditEventWithError()
    {
        var mockContext = MockRepository.Create<OpenIdContext>();
        var mockTenant = MockRepository.Create<OpenIdTenant>();
        var mockPublisher = MockRepository.Create<IEventPublisher>();

        mockContext.Setup(x => x.Tenant).Returns(mockTenant.Object).Verifiable();
        mockContext.Setup(x => x.Http).Returns(CreateHttpContext("api/token")).Verifiable();
        mockTenant.Setup(x => x.TenantId).Returns("tenant-1").Verifiable();
        SetupTenantEnvironmentType(mockTenant, "production");

        ClientAuthenticationFailedAuditEvent? captured = null;
        mockPublisher
            .Setup(x =>
                x.PublishAsync(
                    It.IsAny<ClientAuthenticationFailedAuditEvent>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(
                (ClientAuthenticationFailedAuditEvent auditEvent, CancellationToken _) =>
                    captured = auditEvent
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var recorder = CreateRecorder(mockPublisher.Object);

        await recorder.RecordClientAuthenticationFailedAsync(
            mockContext.Object,
            "client-1",
            OpenIdProtocol.ClientAuthenticationMethods.PrivateKeyJwt,
            "client_not_found",
            CancellationToken.None
        );

        Assert.NotNull(captured);
        Assert.Equal("client.authentication", captured.EventName);
        Assert.Equal(AuditEventSources.OpenId, captured.EventSource);
        Assert.Equal(AuditOutcome.Failure, captured.Outcome);
        Assert.Equal("client-1", captured.ClientId);
        Assert.Equal(AuditActor.Client("client-1"), captured.Actor);
        Assert.Equal("api/token", captured.Request?.Endpoint);
        Assert.Equal(
            OpenIdProtocol.ClientAuthenticationMethods.PrivateKeyJwt,
            captured.AuthenticationMethod
        );
        Assert.Equal("client_not_found", captured.Error?.Code);
        Assert.Null(captured.SubjectId);
        Assert.Equal(7, captured.EventId.Version);
    }

    #endregion

    #region RecordOpenIdErrorAsync Tests

    [Fact]
    public async Task RecordOpenIdErrorAsync_WhenGenericError_PublishesFailureOutcomeWithMetadata()
    {
        var mockContext = MockRepository.Create<OpenIdContext>();
        var mockTenant = MockRepository.Create<OpenIdTenant>();
        var mockError = MockRepository.Create<IOpenIdError>();
        var mockPublisher = MockRepository.Create<IEventPublisher>();

        mockContext.Setup(x => x.Tenant).Returns(mockTenant.Object).Verifiable();
        mockContext.Setup(x => x.Http).Returns(CreateHttpContext("api/token")).Verifiable();
        mockTenant.Setup(x => x.TenantId).Returns("tenant-1").Verifiable();
        SetupTenantEnvironmentType(mockTenant, "production");
        mockError.Setup(x => x.Code).Returns("invalid_request").Verifiable();
        mockError
            .Setup(x => x.Description)
            .Returns("The request is missing a parameter.")
            .Verifiable();
        mockError.Setup(x => x.StatusCode).Returns(400).Verifiable();

        OpenIdErrorEvent? captured = null;
        mockPublisher
            .Setup(x => x.PublishAsync(It.IsAny<OpenIdErrorEvent>(), It.IsAny<CancellationToken>()))
            .Callback((OpenIdErrorEvent auditEvent, CancellationToken _) => captured = auditEvent)
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var recorder = CreateRecorder(mockPublisher.Object);

        await recorder.RecordOpenIdErrorAsync(
            mockContext.Object,
            mockError.Object,
            CancellationToken.None
        );

        Assert.NotNull(captured);
        Assert.Equal("openid.error", captured.EventName);
        Assert.Equal(AuditEventSources.OpenId, captured.EventSource);
        Assert.Equal(AuditOutcome.Failure, captured.Outcome);
        Assert.Equal("tenant-1", captured.TenantId);
        Assert.Equal("production", captured.TenantEnvironmentType);
        Assert.Equal(AuditActor.Anonymous, captured.Actor);
        Assert.Equal("api/token", captured.Request?.Endpoint);
        Assert.Equal(
            new AuditError("invalid_request", "The request is missing a parameter.", 400),
            captured.Error
        );
        Assert.Empty(captured.Resources);
        Assert.Equal(7, captured.EventId.Version);
    }

    [Fact]
    public async Task RecordOpenIdErrorAsync_WhenAccessDenied_PublishesDeniedOutcome()
    {
        var mockContext = MockRepository.Create<OpenIdContext>();
        var mockTenant = MockRepository.Create<OpenIdTenant>();
        var mockError = MockRepository.Create<IOpenIdError>();
        var mockPublisher = MockRepository.Create<IEventPublisher>();

        mockContext.Setup(x => x.Tenant).Returns(mockTenant.Object).Verifiable();
        mockContext.Setup(x => x.Http).Returns(CreateHttpContext("api/authorize")).Verifiable();
        mockTenant.Setup(x => x.TenantId).Returns("tenant-1").Verifiable();
        SetupTenantEnvironmentType(mockTenant, "production");
        mockError.Setup(x => x.Code).Returns(OpenIdProtocol.ErrorCodes.AccessDenied).Verifiable();
        mockError.Setup(x => x.Description).Returns((string?)null).Verifiable();
        mockError.Setup(x => x.StatusCode).Returns((int?)null).Verifiable();

        OpenIdErrorEvent? captured = null;
        mockPublisher
            .Setup(x => x.PublishAsync(It.IsAny<OpenIdErrorEvent>(), It.IsAny<CancellationToken>()))
            .Callback((OpenIdErrorEvent auditEvent, CancellationToken _) => captured = auditEvent)
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var recorder = CreateRecorder(mockPublisher.Object);

        await recorder.RecordOpenIdErrorAsync(
            mockContext.Object,
            mockError.Object,
            CancellationToken.None
        );

        Assert.NotNull(captured);
        Assert.Equal(AuditOutcome.Denied, captured.Outcome);
        Assert.Equal(AuditActor.Anonymous, captured.Actor);
        Assert.Equal("api/authorize", captured.Request?.Endpoint);
        Assert.Equal(new AuditError(OpenIdProtocol.ErrorCodes.AccessDenied), captured.Error);
    }

    #endregion

    #region RecordRefreshTokenReplayAsync Tests

    [Fact]
    public async Task RecordRefreshTokenReplayAsync_PublishesReplayAuditEventWithDeniedOutcome()
    {
        var mockContext = MockRepository.Create<OpenIdContext>();
        var mockTenant = MockRepository.Create<OpenIdTenant>();
        var mockClient = MockRepository.Create<OpenIdClient>();
        var mockPublisher = MockRepository.Create<IEventPublisher>();

        mockContext.Setup(x => x.Tenant).Returns(mockTenant.Object).Verifiable();
        mockContext.Setup(x => x.Http).Returns(CreateHttpContext("api/token")).Verifiable();
        mockTenant.Setup(x => x.TenantId).Returns("tenant-1").Verifiable();
        SetupTenantEnvironmentType(mockTenant, "production");
        mockClient.Setup(x => x.ClientId).Returns("client-1").Verifiable();

        RefreshTokenReplayAuditEvent? captured = null;
        mockPublisher
            .Setup(x =>
                x.PublishAsync(
                    It.IsAny<RefreshTokenReplayAuditEvent>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(
                (RefreshTokenReplayAuditEvent auditEvent, CancellationToken _) =>
                    captured = auditEvent
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var recorder = CreateRecorder(mockPublisher.Object);

        await recorder.RecordRefreshTokenReplayAsync(
            mockContext.Object,
            mockClient.Object,
            "subject-1",
            CancellationToken.None
        );

        Assert.NotNull(captured);
        Assert.Equal("token.refresh.replay", captured.EventName);
        Assert.Equal(AuditEventSources.OpenId, captured.EventSource);
        Assert.Equal(AuditOutcome.Denied, captured.Outcome);
        Assert.Equal("tenant-1", captured.TenantId);
        Assert.Equal("production", captured.TenantEnvironmentType);
        Assert.Equal("client-1", captured.ClientId);
        Assert.Equal("subject-1", captured.SubjectId);
        Assert.Equal(AuditActor.Client("client-1"), captured.Actor);
        Assert.Equal("api/token", captured.Request?.Endpoint);
        Assert.Equal(7, captured.EventId.Version);
    }

    #endregion

    #region RecordRefreshTokenRetryAsync Tests

    [Fact]
    public async Task RecordRefreshTokenRetryAsync_PublishesRetryAuditEventWithSuccessOutcome()
    {
        var mockContext = MockRepository.Create<OpenIdContext>();
        var mockTenant = MockRepository.Create<OpenIdTenant>();
        var mockClient = MockRepository.Create<OpenIdClient>();
        var mockPublisher = MockRepository.Create<IEventPublisher>();

        mockContext.Setup(x => x.Tenant).Returns(mockTenant.Object).Verifiable();
        mockContext.Setup(x => x.Http).Returns(CreateHttpContext("api/token")).Verifiable();
        mockTenant.Setup(x => x.TenantId).Returns("tenant-1").Verifiable();
        SetupTenantEnvironmentType(mockTenant, "production");
        mockClient.Setup(x => x.ClientId).Returns("client-1").Verifiable();

        RefreshTokenRetryAuditEvent? captured = null;
        mockPublisher
            .Setup(x =>
                x.PublishAsync(
                    It.IsAny<RefreshTokenRetryAuditEvent>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(
                (RefreshTokenRetryAuditEvent auditEvent, CancellationToken _) =>
                    captured = auditEvent
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var recorder = CreateRecorder(mockPublisher.Object);

        await recorder.RecordRefreshTokenRetryAsync(
            mockContext.Object,
            mockClient.Object,
            "subject-1",
            CancellationToken.None
        );

        Assert.NotNull(captured);
        Assert.Equal("token.refresh.retry", captured.EventName);
        Assert.Equal(AuditEventSources.OpenId, captured.EventSource);
        Assert.Equal(AuditOutcome.Success, captured.Outcome);
        Assert.Equal("tenant-1", captured.TenantId);
        Assert.Equal("production", captured.TenantEnvironmentType);
        Assert.Equal("client-1", captured.ClientId);
        Assert.Equal("subject-1", captured.SubjectId);
        Assert.Equal(AuditActor.Client("client-1"), captured.Actor);
        Assert.Equal("api/token", captured.Request?.Endpoint);
        Assert.Equal(7, captured.EventId.Version);
    }

    #endregion

    #region Session Lifecycle Tests

    [Fact]
    public async Task RecordSessionCreatedAsync_PublishesSessionCreatedAuditEvent()
    {
        var mockContext = MockRepository.Create<OpenIdContext>();
        var mockTenant = MockRepository.Create<OpenIdTenant>();
        var mockPublisher = MockRepository.Create<IEventPublisher>();

        mockContext.Setup(x => x.Tenant).Returns(mockTenant.Object).Verifiable();
        mockTenant.Setup(x => x.TenantId).Returns("tenant-1").Verifiable();
        SetupTenantEnvironmentType(mockTenant, "production");
        mockContext.Setup(x => x.Http).Returns(CreateHttpContext("login")).Verifiable();

        SessionCreatedAuditEvent? captured = null;
        mockPublisher
            .Setup(x =>
                x.PublishAsync(It.IsAny<SessionCreatedAuditEvent>(), It.IsAny<CancellationToken>())
            )
            .Callback(
                (SessionCreatedAuditEvent auditEvent, CancellationToken _) => captured = auditEvent
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var recorder = CreateRecorder(mockPublisher.Object);

        await recorder.RecordSessionCreatedAsync(
            mockContext.Object,
            "subject-1",
            "sid-1",
            CancellationToken.None
        );

        Assert.NotNull(captured);
        Assert.Equal("session.created", captured.EventName);
        Assert.Equal(AuditEventSources.OpenId, captured.EventSource);
        Assert.Equal(AuditOutcome.Success, captured.Outcome);
        Assert.Equal("tenant-1", captured.TenantId);
        Assert.Equal("production", captured.TenantEnvironmentType);
        Assert.Equal("subject-1", captured.SubjectId);
        Assert.Equal(AuditActor.EndUser("subject-1"), captured.Actor);
        Assert.Equal("login", captured.Request?.Endpoint);
        Assert.Equal("sid-1", captured.SessionId);
        var resource = Assert.Single(captured.Resources);
        Assert.Equal(new AuditResource(AuditResourceTypes.Session, "sid-1"), resource);
        Assert.Equal(7, captured.EventId.Version);
    }

    [Theory]
    [InlineData(SessionTerminationReasons.GlobalSignOut)]
    [InlineData("password_changed")]
    public async Task RecordSessionTerminatedAsync_PublishesSessionTerminatedAuditEventWithReason(
        string reason
    )
    {
        var mockContext = MockRepository.Create<OpenIdContext>();
        var mockTenant = MockRepository.Create<OpenIdTenant>();
        var mockPublisher = MockRepository.Create<IEventPublisher>();

        mockContext.Setup(x => x.Tenant).Returns(mockTenant.Object).Verifiable();
        mockTenant.Setup(x => x.TenantId).Returns("tenant-1").Verifiable();
        SetupTenantEnvironmentType(mockTenant, "production");
        mockContext.Setup(x => x.Http).Returns(CreateHttpContext("api/sessions")).Verifiable();

        SessionTerminatedAuditEvent? captured = null;
        mockPublisher
            .Setup(x =>
                x.PublishAsync(
                    It.IsAny<SessionTerminatedAuditEvent>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(
                (SessionTerminatedAuditEvent auditEvent, CancellationToken _) =>
                    captured = auditEvent
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var recorder = CreateRecorder(mockPublisher.Object);

        await recorder.RecordSessionTerminatedAsync(
            mockContext.Object,
            reason,
            "subject-1",
            sessionId: null,
            CancellationToken.None
        );

        Assert.NotNull(captured);
        Assert.Equal("session.terminated", captured.EventName);
        Assert.Equal(AuditEventSources.OpenId, captured.EventSource);
        Assert.Equal(AuditOutcome.Success, captured.Outcome);
        Assert.Equal("tenant-1", captured.TenantId);
        Assert.Equal("production", captured.TenantEnvironmentType);
        Assert.Equal("subject-1", captured.SubjectId);
        Assert.Equal(AuditActor.System, captured.Actor);
        Assert.Equal("api/sessions", captured.Request?.Endpoint);
        Assert.Null(captured.SessionId);
        Assert.Empty(captured.Resources);
        Assert.Equal(reason, captured.TerminationReason);
        Assert.Equal(7, captured.EventId.Version);
    }

    #endregion

    #region RecordUserInfoAccessedAsync Tests

    [Fact]
    public async Task RecordUserInfoAccessedAsync_PublishesTheSortedDistinctClaimNames()
    {
        var mockContext = MockRepository.Create<OpenIdContext>();
        var mockTenant = MockRepository.Create<OpenIdTenant>();
        var mockClient = MockRepository.Create<OpenIdClient>();
        var mockPublisher = MockRepository.Create<IEventPublisher>();

        mockContext.Setup(x => x.Tenant).Returns(mockTenant.Object).Verifiable();
        mockTenant.Setup(x => x.TenantId).Returns("tenant-1").Verifiable();
        SetupTenantEnvironmentType(mockTenant, "production");
        mockContext.Setup(x => x.Http).Returns(CreateHttpContext("userinfo_endpoint")).Verifiable();
        mockClient.Setup(x => x.ClientId).Returns("client-1").Verifiable();

        UserInfoAccessedAuditEvent? captured = null;
        mockPublisher
            .Setup(x =>
                x.PublishAsync(
                    It.IsAny<UserInfoAccessedAuditEvent>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(
                (UserInfoAccessedAuditEvent auditEvent, CancellationToken _) =>
                    captured = auditEvent
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var recorder = CreateRecorder(mockPublisher.Object);

        await recorder.RecordUserInfoAccessedAsync(
            mockContext.Object,
            mockClient.Object,
            "subject-1",
            ["sub", "email", "name", "email"],
            CancellationToken.None
        );

        Assert.NotNull(captured);
        Assert.Equal("userinfo.accessed", captured.EventName);
        Assert.Equal(AuditEventCategories.DataAccess, captured.EventCategory);
        Assert.Equal(AuditOutcome.Success, captured.Outcome);
        Assert.Equal(AuditSeverities.Info, captured.Severity);
        Assert.Equal("tenant-1", captured.TenantId);
        Assert.Equal("subject-1", captured.SubjectId);
        Assert.Equal("client-1", captured.ClientId);
        Assert.Equal(AuditActor.Client("client-1"), captured.Actor);
        Assert.Equal("userinfo_endpoint", captured.Request?.Endpoint);
        Assert.Equal(["email", "name", "sub"], captured.ClaimNames);
        Assert.Null(captured.Error);
    }

    #endregion

    #region RecordUserInfoAccessDeniedAsync Tests

    [Fact]
    public async Task RecordUserInfoAccessDeniedAsync_PublishesADeniedEventWithTheErrorCode()
    {
        var mockContext = MockRepository.Create<OpenIdContext>();
        var mockTenant = MockRepository.Create<OpenIdTenant>();
        var mockPublisher = MockRepository.Create<IEventPublisher>();

        mockContext.Setup(x => x.Tenant).Returns(mockTenant.Object).Verifiable();
        mockTenant.Setup(x => x.TenantId).Returns("tenant-1").Verifiable();
        SetupTenantEnvironmentType(mockTenant, "production");
        mockContext.Setup(x => x.Http).Returns(CreateHttpContext("userinfo_endpoint")).Verifiable();

        UserInfoAccessedAuditEvent? captured = null;
        mockPublisher
            .Setup(x =>
                x.PublishAsync(
                    It.IsAny<UserInfoAccessedAuditEvent>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(
                (UserInfoAccessedAuditEvent auditEvent, CancellationToken _) =>
                    captured = auditEvent
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var recorder = CreateRecorder(mockPublisher.Object);

        await recorder.RecordUserInfoAccessDeniedAsync(
            mockContext.Object,
            "subject-1",
            "client-1",
            OpenIdProtocol.ErrorCodes.InvalidToken,
            CancellationToken.None
        );

        Assert.NotNull(captured);
        Assert.Equal(AuditOutcome.Denied, captured.Outcome);
        Assert.Equal("subject-1", captured.SubjectId);
        Assert.Equal("client-1", captured.ClientId);
        Assert.Equal(AuditActor.Client("client-1"), captured.Actor);
        Assert.Equal(OpenIdProtocol.ErrorCodes.InvalidToken, captured.Error?.Code);
        Assert.Empty(captured.ClaimNames);
        Assert.False(captured.HasAuditDetails);
    }

    [Fact]
    public async Task RecordUserInfoAccessDeniedAsync_WithoutAClient_PublishesADeniedEventWithAnAnonymousActor()
    {
        // A token that names no client identifies no caller, so the denied event's actor is anonymous.
        var mockContext = MockRepository.Create<OpenIdContext>();
        var mockTenant = MockRepository.Create<OpenIdTenant>();
        var mockPublisher = MockRepository.Create<IEventPublisher>();

        mockContext.Setup(x => x.Tenant).Returns(mockTenant.Object).Verifiable();
        mockTenant.Setup(x => x.TenantId).Returns("tenant-1").Verifiable();
        SetupTenantEnvironmentType(mockTenant, "production");
        mockContext.Setup(x => x.Http).Returns(CreateHttpContext("userinfo_endpoint")).Verifiable();

        UserInfoAccessedAuditEvent? captured = null;
        mockPublisher
            .Setup(x =>
                x.PublishAsync(
                    It.IsAny<UserInfoAccessedAuditEvent>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(
                (UserInfoAccessedAuditEvent auditEvent, CancellationToken _) =>
                    captured = auditEvent
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var recorder = CreateRecorder(mockPublisher.Object);

        await recorder.RecordUserInfoAccessDeniedAsync(
            mockContext.Object,
            "subject-1",
            clientId: null,
            OpenIdProtocol.ErrorCodes.InvalidToken,
            CancellationToken.None
        );

        Assert.NotNull(captured);
        Assert.Equal("userinfo.accessed", captured.EventName);
        Assert.Equal(AuditEventCategories.DataAccess, captured.EventCategory);
        Assert.Equal(AuditOutcome.Denied, captured.Outcome);
        Assert.Equal("tenant-1", captured.TenantId);
        Assert.Equal("subject-1", captured.SubjectId);
        Assert.Null(captured.ClientId);
        Assert.Equal(AuditActor.Anonymous, captured.Actor);
        Assert.Equal("userinfo_endpoint", captured.Request?.Endpoint);
        Assert.Equal(OpenIdProtocol.ErrorCodes.InvalidToken, captured.Error?.Code);
        Assert.Empty(captured.ClaimNames);
        Assert.False(captured.HasAuditDetails);
    }

    #endregion

    #region Self-Service Credential Tests

    [Fact]
    public async Task RecordPasswordChangedAsync_Always_PublishesSuccessAuditEventWithTheAccount()
    {
        var captured = await RecordAsync<PasswordChangeAuditEvent>(
            (recorder, openIdContext) =>
                recorder.RecordPasswordChangedAsync(openIdContext, "subject-1", "account-1")
        );

        Assert.Equal("password.change", captured.EventName);
        Assert.Equal(AuditEventCategories.Authentication, captured.EventCategory);
        Assert.Equal(AuditEventSources.OpenId, captured.EventSource);
        Assert.Equal(AuditOutcome.Success, captured.Outcome);
        Assert.Equal("tenant-1", captured.TenantId);
        Assert.Equal("production", captured.TenantEnvironmentType);
        Assert.Equal("subject-1", captured.SubjectId);
        Assert.Equal("account-1", captured.LocalAccountId);
        Assert.Equal(AuditActor.EndUser("subject-1"), captured.Actor);
        Assert.Equal("api/authorize", captured.Request?.Endpoint);
        Assert.Null(captured.Error);
        Assert.Equal(7, captured.EventId.Version);
    }

    [Fact]
    public async Task RecordPasswordChangeFailedAsync_Always_PublishesFailureAuditEventWithError()
    {
        var captured = await RecordAsync<PasswordChangeAuditEvent>(
            (recorder, openIdContext) =>
                recorder.RecordPasswordChangeFailedAsync(
                    openIdContext,
                    "subject-1",
                    "account-1",
                    "invalid_current_password",
                    CancellationToken.None
                )
        );

        Assert.Equal("password.change", captured.EventName);
        Assert.Equal(AuditOutcome.Failure, captured.Outcome);
        Assert.Equal(AuditSeverities.Warning, captured.Severity);
        Assert.Equal("subject-1", captured.SubjectId);
        Assert.Equal("account-1", captured.LocalAccountId);
        Assert.Equal(AuditActor.EndUser("subject-1"), captured.Actor);
        Assert.Equal("invalid_current_password", captured.Error?.Code);
    }

    [Fact]
    public async Task RecordPasswordResetRequestedAsync_Always_PublishesSuccessAuditEventWithTheAccount()
    {
        var captured = await RecordAsync<PasswordResetRequestAuditEvent>(
            (recorder, openIdContext) =>
                recorder.RecordPasswordResetRequestedAsync(
                    openIdContext,
                    "subject-1",
                    "account-1",
                    CancellationToken.None
                )
        );

        Assert.Equal("password.reset_request", captured.EventName);
        Assert.Equal(AuditOutcome.Success, captured.Outcome);
        Assert.Equal("subject-1", captured.SubjectId);
        Assert.Equal("account-1", captured.LocalAccountId);
        Assert.Equal(AuditActor.EndUser("subject-1"), captured.Actor);
        Assert.Null(captured.AttemptedUsername);
        Assert.Null(captured.Error);
    }

    [Fact]
    public async Task RecordPasswordResetRequestedAsync_WithoutASubject_PublishesAnAnonymousActor()
    {
        var captured = await RecordAsync<PasswordResetRequestAuditEvent>(
            (recorder, openIdContext) =>
                recorder.RecordPasswordResetRequestedAsync(
                    openIdContext,
                    subjectId: null,
                    "account-1",
                    CancellationToken.None
                )
        );

        Assert.Null(captured.SubjectId);
        Assert.Equal("account-1", captured.LocalAccountId);
        Assert.Equal(AuditActor.Anonymous, captured.Actor);
    }

    [Fact]
    public async Task RecordPasswordResetRequestFailedAsync_Always_PublishesFailureAuditEventWithTheUserName()
    {
        var captured = await RecordAsync<PasswordResetRequestAuditEvent>(
            (recorder, openIdContext) =>
                recorder.RecordPasswordResetRequestFailedAsync(
                    openIdContext,
                    "account_not_eligible",
                    "attempted-user",
                    CancellationToken.None
                )
        );

        Assert.Equal("password.reset_request", captured.EventName);
        Assert.Equal(AuditOutcome.Failure, captured.Outcome);
        Assert.Null(captured.SubjectId);
        Assert.Null(captured.LocalAccountId);
        Assert.Equal("attempted-user", captured.AttemptedUsername);
        Assert.Equal(AuditActor.Anonymous, captured.Actor);
        Assert.Equal("account_not_eligible", captured.Error?.Code);
    }

    [Fact]
    public async Task RecordPasswordResetAsync_Always_PublishesSuccessAuditEventWithTheAccount()
    {
        var captured = await RecordAsync<PasswordResetAuditEvent>(
            (recorder, openIdContext) =>
                recorder.RecordPasswordResetAsync(openIdContext, "subject-1", "account-1")
        );

        Assert.Equal("password.reset", captured.EventName);
        Assert.Equal(AuditOutcome.Success, captured.Outcome);
        Assert.Equal("subject-1", captured.SubjectId);
        Assert.Equal("account-1", captured.LocalAccountId);
        Assert.Equal(AuditActor.EndUser("subject-1"), captured.Actor);
        Assert.Null(captured.Error);
    }

    [Fact]
    public async Task RecordPasswordResetFailedAsync_Always_PublishesFailureAuditEventWithError()
    {
        var captured = await RecordAsync<PasswordResetAuditEvent>(
            (recorder, openIdContext) =>
                recorder.RecordPasswordResetFailedAsync(
                    openIdContext,
                    subjectId: null,
                    localAccountId: null,
                    "invalid_code",
                    CancellationToken.None
                )
        );

        Assert.Equal("password.reset", captured.EventName);
        Assert.Equal(AuditOutcome.Failure, captured.Outcome);
        Assert.Null(captured.SubjectId);
        Assert.Null(captured.LocalAccountId);
        Assert.Equal(AuditActor.Anonymous, captured.Actor);
        Assert.Equal("invalid_code", captured.Error?.Code);
    }

    [Fact]
    public async Task RecordEmailVerifiedAsync_Always_PublishesSuccessAuditEventWithTheAccount()
    {
        var captured = await RecordAsync<EmailVerificationAuditEvent>(
            (recorder, openIdContext) =>
                recorder.RecordEmailVerifiedAsync(openIdContext, "subject-1", "account-1")
        );

        Assert.Equal("email.verification", captured.EventName);
        Assert.Equal(AuditOutcome.Success, captured.Outcome);
        Assert.Equal("subject-1", captured.SubjectId);
        Assert.Equal("account-1", captured.LocalAccountId);
        Assert.Equal(AuditActor.EndUser("subject-1"), captured.Actor);
        Assert.Null(captured.Error);
    }

    [Fact]
    public async Task RecordEmailVerificationFailedAsync_Always_PublishesFailureAuditEventWithError()
    {
        var captured = await RecordAsync<EmailVerificationAuditEvent>(
            (recorder, openIdContext) =>
                recorder.RecordEmailVerificationFailedAsync(
                    openIdContext,
                    "subject-1",
                    "account-1",
                    "invalid_code",
                    CancellationToken.None
                )
        );

        Assert.Equal("email.verification", captured.EventName);
        Assert.Equal(AuditOutcome.Failure, captured.Outcome);
        Assert.Equal("subject-1", captured.SubjectId);
        Assert.Equal("account-1", captured.LocalAccountId);
        Assert.Equal(AuditActor.EndUser("subject-1"), captured.Actor);
        Assert.Equal("invalid_code", captured.Error?.Code);
    }

    [Fact]
    public async Task RecordSecondFactorVerifiedAsync_Always_PublishesSuccessAuditEventWithTheFactor()
    {
        var captured = await RecordAsync<SecondFactorAuditEvent>(
            (recorder, openIdContext) =>
                recorder.RecordSecondFactorVerifiedAsync(
                    openIdContext,
                    "subject-1",
                    "account-1",
                    AuditCredentialFactors.EmailCode
                )
        );

        Assert.Equal("subject.second_factor", captured.EventName);
        Assert.Equal(AuditOutcome.Success, captured.Outcome);
        Assert.Equal("subject-1", captured.SubjectId);
        Assert.Equal("account-1", captured.LocalAccountId);
        Assert.Equal("email_code", captured.Factor);
        Assert.Equal(AuditActor.EndUser("subject-1"), captured.Actor);
        Assert.Null(captured.Error);
    }

    [Fact]
    public async Task RecordSecondFactorFailedAsync_Always_PublishesFailureAuditEventWithTheFactor()
    {
        var captured = await RecordAsync<SecondFactorAuditEvent>(
            (recorder, openIdContext) =>
                recorder.RecordSecondFactorFailedAsync(
                    openIdContext,
                    "subject-1",
                    localAccountId: null,
                    AuditCredentialFactors.EmailCode,
                    "no_email",
                    CancellationToken.None
                )
        );

        Assert.Equal("subject.second_factor", captured.EventName);
        Assert.Equal(AuditOutcome.Failure, captured.Outcome);
        Assert.Equal("subject-1", captured.SubjectId);
        Assert.Null(captured.LocalAccountId);
        Assert.Equal("email_code", captured.Factor);
        Assert.Equal(AuditActor.EndUser("subject-1"), captured.Actor);
        Assert.Equal("no_email", captured.Error?.Code);
    }

    [Fact]
    public async Task RecordPasswordChangedAsync_Always_PublishesOnATokenOfItsOwn()
    {
        var openIdContext = CreateAuthorizeContext();
        var mockPublisher = MockRepository.Create<IEventPublisher>();
        mockPublisher
            .Setup(x =>
                x.PublishAsync(
                    It.IsAny<PasswordChangeAuditEvent>(),
                    It.Is<CancellationToken>(token =>
                        token.CanBeCanceled && !token.IsCancellationRequested
                    )
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        var recorder = CreateRecorder(mockPublisher.Object);

        await recorder.RecordPasswordChangedAsync(openIdContext, "subject-1", "account-1");
    }

    [Fact]
    public async Task RecordPasswordResetAsync_WhenThePublishFails_LogsAndDoesNotThrow()
    {
        var openIdContext = CreateAuthorizeContext();
        var exception = new InvalidOperationException("The publish failed.");
        var mockPublisher = MockRepository.Create<IEventPublisher>();
        mockPublisher
            .Setup(x =>
                x.PublishAsync(It.IsAny<PasswordResetAuditEvent>(), It.IsAny<CancellationToken>())
            )
            .Throws(exception)
            .Verifiable();
        var logger = new FakeLogger<DefaultAuditEventRecorder>();
        var recorder = CreateRecorder(mockPublisher.Object, logger);

        await recorder.RecordPasswordResetAsync(openIdContext, "subject-1", "account-1");

        var record = Assert.Single(logger.Collector.GetSnapshot());
        Assert.Equal(EventIds.AuditRecordPublishFailed, record.Id.Id);
        Assert.Equal(LogLevel.Error, record.Level);
        Assert.Same(exception, record.Exception);
    }

    private OpenIdContext CreateAuthorizeContext()
    {
        var mockContext = MockRepository.Create<OpenIdContext>();
        var mockTenant = MockRepository.Create<OpenIdTenant>();

        mockContext.Setup(x => x.Tenant).Returns(mockTenant.Object).Verifiable();
        mockTenant.Setup(x => x.TenantId).Returns("tenant-1").Verifiable();
        SetupTenantEnvironmentType(mockTenant, "production");
        mockContext.Setup(x => x.Http).Returns(CreateHttpContext("api/authorize")).Verifiable();

        return mockContext.Object;
    }

    private async ValueTask<TEvent> RecordAsync<TEvent>(
        Func<DefaultAuditEventRecorder, OpenIdContext, ValueTask> record,
        CancellationToken cancellationToken = default
    )
        where TEvent : class, IAuditEvent
    {
        cancellationToken.ThrowIfCancellationRequested();

        var mockContext = MockRepository.Create<OpenIdContext>();
        var mockTenant = MockRepository.Create<OpenIdTenant>();
        var mockPublisher = MockRepository.Create<IEventPublisher>();

        mockContext.Setup(x => x.Tenant).Returns(mockTenant.Object).Verifiable();
        mockTenant.Setup(x => x.TenantId).Returns("tenant-1").Verifiable();
        SetupTenantEnvironmentType(mockTenant, "production");
        mockContext.Setup(x => x.Http).Returns(CreateHttpContext("api/authorize")).Verifiable();

        TEvent? captured = null;
        mockPublisher
            .Setup(x => x.PublishAsync(It.IsAny<TEvent>(), It.IsAny<CancellationToken>()))
            .Callback((TEvent auditEvent, CancellationToken _) => captured = auditEvent)
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var recorder = CreateRecorder(mockPublisher.Object);

        await record(recorder, mockContext.Object);

        Assert.NotNull(captured);
        return captured;
    }

    #endregion
}
