// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NCode.Identity.OpenId.Authentication.Endpoints.Authorization.Commands;
using NCode.Identity.Registration.AspNetCore;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Authentication.Interactive;

/// <summary>
/// Handles the <see cref="ConsentChallengeCommand"/> for the interactive surface by redirecting the user-agent to the
/// consent page, carrying the parked continuation as the return URL so the authorization request resumes once the
/// consent decision is recorded, and the request's <c>ui_locales</c> so the page renders in the end-user's preferred
/// language. This replaces the core no-op handler when the interactive surface is registered.
/// </summary>
internal sealed class InteractiveConsentChallengeHandler(InteractiveSurfacePaths surfacePaths)
    : ICommandResponseHandler<ConsentChallengeCommand, ReadOnlyEndpointDisposition>
{
    private InteractiveSurfacePaths SurfacePaths { get; } = surfacePaths;

    /// <inheritdoc />
    public ValueTask<ReadOnlyEndpointDisposition> HandleAsync(
        ConsentChallengeCommand command,
        CancellationToken cancellationToken
    )
    {
        var consentPath = SurfacePaths.GetSurfacePath(
            command.OpenIdContext.Http,
            InteractiveAuthenticationDefaults.ConsentPath
        );
        var escapedContinueUrl = Uri.EscapeDataString(command.ContinueUrl);
        var location = InteractiveLocaleSelector.AppendUiLocalesQuery(
            $"{consentPath}?ReturnUrl=" + escapedContinueUrl,
            command.AuthorizationRequest.UiLocales
        );

        RedirectHttpResult result = TypedResults.Redirect(location);
        var disposition = ReadOnlyEndpointDisposition.Handled(result);
        return ValueTask.FromResult(disposition);
    }
}
