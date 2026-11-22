// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Client;

/// <summary>
/// Represents an OAuth 2.0 token exchange request (RFC 8693).
/// </summary>
/// <param name="subjectToken">The token that represents the identity of the party on behalf of whom the request is made.</param>
/// <param name="subjectTokenType">The type identifier of the subject token.
/// See <see cref="OpenIdProtocol.TokenTypeIdentifiers"/> for the known values.</param>
[PublicAPI]
public sealed class TokenExchangeTokenRequest(string subjectToken, string subjectTokenType)
    : TokenRequest
{
    /// <inheritdoc />
    public override string GrantType => OpenIdProtocol.GrantTypes.TokenExchange;

    /// <summary>
    /// Gets the subject token.
    /// </summary>
    public string SubjectToken { get; } = ValidateNotEmpty(subjectToken);

    /// <summary>
    /// Gets the type identifier of the subject token.
    /// </summary>
    public string SubjectTokenType { get; } = ValidateNotEmpty(subjectTokenType);

    /// <summary>
    /// Gets the optional actor token.
    /// </summary>
    public string? ActorToken { get; init; }

    /// <summary>
    /// Gets the optional type identifier of the actor token.
    /// </summary>
    public string? ActorTokenType { get; init; }

    /// <summary>
    /// Gets the optional type identifier of the requested token.
    /// </summary>
    public string? RequestedTokenType { get; init; }

    /// <summary>
    /// Gets the logical names of the target services, each written as its own <c>audience</c> parameter.
    /// </summary>
    public List<string> Audiences { get; init; } = [];

    /// <inheritdoc />
    public override void AddGrantParameters(ICollection<KeyValuePair<string, string>> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var subjectTokenParameter = new KeyValuePair<string, string>(
            OpenIdProtocol.Parameters.SubjectToken,
            SubjectToken
        );
        parameters.Add(subjectTokenParameter);
        var subjectTokenTypeParameter = new KeyValuePair<string, string>(
            OpenIdProtocol.Parameters.SubjectTokenType,
            SubjectTokenType
        );
        parameters.Add(subjectTokenTypeParameter);

        if (ActorToken is not null)
        {
            var actorTokenParameter = new KeyValuePair<string, string>(
                OpenIdProtocol.Parameters.ActorToken,
                ActorToken
            );
            parameters.Add(actorTokenParameter);
        }

        if (ActorTokenType is not null)
        {
            var actorTokenTypeParameter = new KeyValuePair<string, string>(
                OpenIdProtocol.Parameters.ActorTokenType,
                ActorTokenType
            );
            parameters.Add(actorTokenTypeParameter);
        }

        if (RequestedTokenType is not null)
        {
            var requestedTokenTypeParameter = new KeyValuePair<string, string>(
                OpenIdProtocol.Parameters.RequestedTokenType,
                RequestedTokenType
            );
            parameters.Add(requestedTokenTypeParameter);
        }

        foreach (var audience in Audiences)
        {
            var audienceParameter = new KeyValuePair<string, string>(
                OpenIdProtocol.Parameters.Audience,
                audience
            );
            parameters.Add(audienceParameter);
        }
    }

    private static string ValidateNotEmpty(
        string value,
        [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(value))]
            string? paramName = null
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(value, paramName);
        return value;
    }
}
