// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Authentication;
using NCode.Identity.AspNetCore;
using NCode.Identity.OpenId.Authentication.Clients;
using NCode.Identity.OpenId.Authentication.Endpoints.Authorization.Commands;
using NCode.Identity.OpenId.Authentication.Endpoints.Authorization.Messages;
using NCode.Identity.OpenId.Authentication.Endpoints.Authorization.Results;
using NCode.Identity.OpenId.Authentication.Endpoints.Continue;
using NCode.Identity.OpenId.Authentication.Subject;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Errors;
using NCode.Identity.OpenId.Exceptions;
using NCode.Identity.OpenId.Messages;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Registration.AspNetCore;

namespace NCode.Identity.OpenId.Authentication.Endpoints.Authorization;

/// <summary>
/// Provides a default implementation of the <see cref="IAuthorizationRequestProcessor"/> abstraction.
/// </summary>
internal class DefaultAuthorizationRequestProcessor(
    IContinueService continueService,
    IEnumerable<IOpenIdExceptionTranslator> exceptionTranslators
) : IAuthorizationRequestProcessor
{
    private IContinueService ContinueService { get; } = continueService;

    private IOpenIdExceptionTranslator[] ExceptionTranslators { get; } = [.. exceptionTranslators];

    /// <inheritdoc />
    public async ValueTask<ReadOnlyEndpointDisposition> ProcessRequestAsync(
        OpenIdContext openIdContext,
        OpenIdClient openIdClient,
        IAuthorizationRequest authorizationRequest,
        AuthorizationResponseDelivery delivery,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await CoreProcessRequestAsync(
                openIdContext,
                openIdClient,
                authorizationRequest,
                delivery,
                cancellationToken
            );
        }
        catch (HttpResultException exception)
        {
            return ReadOnlyEndpointDisposition.Handled(exception.HttpResult);
        }
        catch (Exception exception)
        {
            var openIdError = GetOpenIdError(openIdContext, exception, delivery.State);
            var httpResult = new AuthorizationResult(
                openIdContext,
                openIdClient,
                delivery,
                openIdError
            );
            return ReadOnlyEndpointDisposition.Handled(httpResult);
        }
    }

    private IOpenIdError GetOpenIdError(
        OpenIdContext openIdContext,
        Exception exception,
        string? state
    )
    {
        if (exception is OpenIdException openIdException)
            return openIdException.Error.WithState(state);

        if (
            ExceptionTranslators.TryTranslate(
                openIdContext.Environment,
                exception,
                out var translatedError
            )
        )
        {
            // The authorization endpoint returns an error to the client's redirect URI (RFC 6749 section 4.1.2.1), so
            // the status code the translator gives for a direct answer is cleared.
            return translatedError.WithStatusCode(null).WithState(state);
        }

        return openIdContext
            .ErrorFactory.Create(OpenIdProtocol.ErrorCodes.ServerError)
            .WithState(state)
            .WithException(exception);
    }

    private async ValueTask<ReadOnlyEndpointDisposition> CoreProcessRequestAsync(
        OpenIdContext openIdContext,
        OpenIdClient openIdClient,
        IAuthorizationRequest authorizationRequest,
        AuthorizationResponseDelivery delivery,
        CancellationToken cancellationToken
    )
    {
        var mediator = openIdContext.Mediator;

        // Errors produced by the AuthenticateCommand / AuthorizeCommand already have their state stamped by their
        // post-processors; the two errors created inline below (prompt=none login_required, no-consent-surface
        // interaction_required) bypass that pipeline, so echo the delivery's state onto them here (OIDC Core 3.1.2.6).
        var state = delivery.State;

        var validateCommand = new ValidateAuthorizationRequestCommand(
            openIdContext,
            openIdClient,
            authorizationRequest,
            delivery
        );
        await mediator.SendAsync(validateCommand, cancellationToken);

        var authenticateCommand = new AuthenticateCommand(
            openIdContext,
            openIdClient,
            authorizationRequest
        );
        var authenticateSubjectDisposition = await mediator.SendAsync<
            AuthenticateCommand,
            AuthenticateSubjectDisposition
        >(authenticateCommand, cancellationToken);

        if (authenticateSubjectDisposition.HasError)
        {
            var authenticateErrorResult = new AuthorizationResult(
                openIdContext,
                openIdClient,
                delivery,
                authenticateSubjectDisposition.Error
            );
            return ReadOnlyEndpointDisposition.Handled(authenticateErrorResult);
        }

        if (!authenticateSubjectDisposition.IsAuthenticated)
        {
            // prompt=none forbids interactive authentication, so report login_required back to the client instead
            // of redirecting the user-agent to the interactive login surface.
            if (authorizationRequest.PromptTypes.Contains(OpenIdProtocol.PromptTypes.None))
            {
                var loginRequiredError = openIdContext
                    .ErrorFactory.LoginRequired()
                    .WithState(state);
                var loginRequiredResult = new AuthorizationResult(
                    openIdContext,
                    openIdClient,
                    delivery,
                    loginRequiredError
                );
                return ReadOnlyEndpointDisposition.Handled(loginRequiredResult);
            }

            return await ChallengeAsync(
                openIdContext,
                openIdClient,
                authorizationRequest,
                cancellationToken
            );
        }

        var authenticationTicket = authenticateSubjectDisposition.Ticket.Value;

        var authorizeCommand = new AuthorizeCommand(
            openIdContext,
            openIdClient,
            authorizationRequest,
            authenticationTicket
        );
        var authorizeSubjectDisposition = await mediator.SendAsync<
            AuthorizeCommand,
            AuthorizeDisposition
        >(authorizeCommand, cancellationToken);

        if (authorizeSubjectDisposition.HasError)
        {
            var authorizeErrorResult = new AuthorizationResult(
                openIdContext,
                openIdClient,
                delivery,
                authorizeSubjectDisposition.Error
            );
            return ReadOnlyEndpointDisposition.Handled(authorizeErrorResult);
        }

        if (authorizeSubjectDisposition.ChallengeRequired)
        {
            return await ChallengeAsync(
                openIdContext,
                openIdClient,
                authorizationRequest,
                cancellationToken
            );
        }

        if (authorizeSubjectDisposition.ConsentRequired)
        {
            var consentDisposition = await RequestConsentAsync(
                openIdContext,
                openIdClient,
                authorizationRequest,
                cancellationToken
            );
            if (consentDisposition.WasHandled)
            {
                return consentDisposition;
            }

            // No interactive consent surface is registered to render the consent screen, so the request cannot
            // obtain consent and the client is told interaction is required.
            var interactionRequiredError = openIdContext
                .ErrorFactory.InteractionRequired()
                .WithState(state);
            var interactionRequiredResult = new AuthorizationResult(
                openIdContext,
                openIdClient,
                delivery,
                interactionRequiredError
            );
            return ReadOnlyEndpointDisposition.Handled(interactionRequiredResult);
        }

        var createTicketCommand = new CreateAuthorizationTicketCommand(
            openIdContext,
            openIdClient,
            authorizationRequest,
            authenticationTicket
        );
        var authorizationTicket = await mediator.SendAsync<
            CreateAuthorizationTicketCommand,
            IAuthorizationTicket
        >(createTicketCommand, cancellationToken);

        var authorizationResult = new AuthorizationResult(
            openIdContext,
            openIdClient,
            delivery,
            authorizationTicket
        );
        return ReadOnlyEndpointDisposition.Handled(authorizationResult);
    }

    private async ValueTask<ReadOnlyEndpointDisposition> ChallengeAsync(
        OpenIdContext openIdContext,
        OpenIdClient openIdClient,
        IAuthorizationRequest authorizationRequest,
        CancellationToken cancellationToken
    )
    {
        var mediator = openIdContext.Mediator;

        var continueUrl = await GetContinueUrlAsync(
            openIdContext,
            openIdClient,
            authorizationRequest,
            cancellationToken
        );

        var authenticationProperties = new AuthenticationProperties { RedirectUri = continueUrl };

        authenticationProperties.SetString(
            OpenIdConstants.AuthenticationPropertyItems.TenantId,
            openIdContext.Tenant.TenantId
        );

        // for the following parameters, see the OpenIdConnectHandler class for reference:
        // https://github.com/dotnet/aspnetcore/blob/cd5ca6985645aa4929747bd690109a99a97126e3/src/Security/Authentication/OpenIdConnect/src/OpenIdConnectHandler.cs#L398

        // OpenIdConnectHandler requires a string, but we have an IReadOnlyList<string>
        var prompt = string.Join(
            OpenIdProtocol.ParameterSeparatorChar,
            authorizationRequest.PromptTypes
        );
        authenticationProperties.SetParameter(OpenIdProtocol.Parameters.Prompt, prompt);

        // OpenIdConnectHandler requires an ICollection<string> but we have an IReadOnlyList<string>
        var scopes = authorizationRequest.Scopes.ToList();
        authenticationProperties.SetParameter(OpenIdProtocol.Parameters.Scope, scopes);

        authenticationProperties.SetParameter(
            OpenIdProtocol.Parameters.MaxAge,
            // OpenIdConnectHandler uses TimeSpan? just like we do
            authorizationRequest.MaxAge
        );

        // The end-user's preferred UI languages reach the login surface the challenge redirects to, which renders in
        // the first one it serves and the tenant supports (OpenID Connect Core 1.0 §3.1.2.1).
        // See ADR-1402.
        if (authorizationRequest.UiLocales.Count > 0)
        {
            var uiLocales = string.Join(
                OpenIdProtocol.ParameterSeparatorChar,
                authorizationRequest.UiLocales
            );
            authenticationProperties.SetParameter(OpenIdProtocol.Parameters.UiLocales, uiLocales);
        }

        // additional authentication properties can be set via mediator middleware

        var challengeCommand = new ChallengeCommand(
            openIdContext,
            openIdClient,
            authorizationRequest,
            authenticationProperties
        );
        var disposition = await mediator.SendAsync<ChallengeCommand, ReadOnlyEndpointDisposition>(
            challengeCommand,
            cancellationToken
        );
        return disposition;
    }

    private async ValueTask<ReadOnlyEndpointDisposition> RequestConsentAsync(
        OpenIdContext openIdContext,
        OpenIdClient openIdClient,
        IAuthorizationRequest authorizationRequest,
        CancellationToken cancellationToken
    )
    {
        var mediator = openIdContext.Mediator;

        // The same parked continuation the login challenge uses resumes the authorization request after consent.
        var continueUrl = await GetContinueUrlAsync(
            openIdContext,
            openIdClient,
            authorizationRequest,
            cancellationToken
        );

        var consentChallengeCommand = new ConsentChallengeCommand(
            openIdContext,
            openIdClient,
            authorizationRequest,
            continueUrl
        );
        return await mediator.SendAsync<ConsentChallengeCommand, ReadOnlyEndpointDisposition>(
            consentChallengeCommand,
            cancellationToken
        );
    }

    private async ValueTask<string> GetContinueUrlAsync(
        OpenIdContext openIdContext,
        OpenIdClient openIdClient,
        IAuthorizationRequest authorizationRequest,
        CancellationToken cancellationToken
    )
    {
        var continueLifetime = openIdClient.Settings.GetValue(
            OpenIdSettingKeys.ContinueAuthorizationLifetime
        );
        var continueUrl = await ContinueService.GetContinueUrlAsync(
            openIdContext,
            OpenIdConstants.ContinueCodes.Authorization,
            openIdClient.ClientId,
            subjectId: null,
            continueLifetime,
            authorizationRequest,
            cancellationToken
        );

        return continueUrl;
    }
}
