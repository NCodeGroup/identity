// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text.Json;
using NCode.Identity.Json;

namespace NCode.Identity.Bff.Blazor.Client;

/// <summary>
/// Reads the document the BFF's user endpoint writes into a <see cref="BffClientUser"/>.
/// </summary>
internal static class BffClientUserReader
{
    private const string SubjectClaimType = "sub";
    private const string NameClaimType = "name";
    private const string RoleClaimType = "role";

    /// <summary>
    /// Reads <paramref name="userDocument"/> into a user.
    /// </summary>
    /// <param name="userDocument">The user document.</param>
    /// <param name="user">The user, when the document is an object with a string <c>sub</c>.</param>
    /// <returns>
    /// <see langword="true"/> when the document describes a user; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool TryRead(
        JsonElement userDocument,
        [NotNullWhen(true)] out BffClientUser? user
    )
    {
        user = null;

        if (userDocument.ValueKind != JsonValueKind.Object)
            return false;

        if (
            !userDocument.TryGetPropertyValue<string>(
                BffUserDocumentNames.Subject,
                out var subjectValue
            )
        )
            return false;

        List<Claim> claims = [new Claim(SubjectClaimType, subjectValue)];

        if (userDocument.TryGetPropertyValue<string>(BffUserDocumentNames.Name, out var nameValue))
        {
            var nameClaim = new Claim(NameClaimType, nameValue);
            claims.Add(nameClaim);
        }

        if (
            userDocument.TryGetPropertyValue<JsonElement>(
                BffUserDocumentNames.Claims,
                out var claimsElement
            )
            && claimsElement.ValueKind == JsonValueKind.Object
        )
        {
            foreach (var property in claimsElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in property.Value.EnumerateArray())
                    {
                        var elementValue = ValueOf(element);
                        var elementClaim = new Claim(property.Name, elementValue);
                        claims.Add(elementClaim);
                    }
                }
                else
                {
                    var propertyValue = ValueOf(property.Value);
                    var propertyClaim = new Claim(property.Name, propertyValue);
                    claims.Add(propertyClaim);
                }
            }
        }

        AddString(userDocument, BffUserDocumentNames.LogoutUrl, BffClaimTypes.LogoutUrl, claims);
        AddString(userDocument, BffUserDocumentNames.LogoutSid, BffClaimTypes.LogoutSid, claims);

        var identity = new ClaimsIdentity(
            claims,
            BffBlazorClientDefaults.AuthenticationType,
            NameClaimType,
            RoleClaimType
        );

        var principal = new ClaimsPrincipal(identity);
        var idleExpiresAt = ReadIdleExpiresAt(userDocument);
        user = new BffClientUser(principal, idleExpiresAt);
        return true;
    }

    private static string ValueOf(JsonElement element) =>
        element.TryGetValue<string>(out var value) ? value : element.GetRawText();

    private static void AddString(
        JsonElement userDocument,
        string propertyName,
        string claimType,
        List<Claim> claims
    )
    {
        if (userDocument.TryGetPropertyValue<string>(propertyName, out var value))
        {
            var claim = new Claim(claimType, value);
            claims.Add(claim);
        }
    }

    private static DateTimeOffset? ReadIdleExpiresAt(JsonElement userDocument)
    {
        if (
            userDocument.TryGetPropertyValue<JsonElement>(
                BffUserDocumentNames.Session,
                out var session
            )
            && session.ValueKind == JsonValueKind.Object
            && session.TryGetPropertyNumericDate(
                BffUserDocumentNames.IdleExpiresAt,
                out var instant
            )
        )
        {
            return instant;
        }

        return null;
    }
}
