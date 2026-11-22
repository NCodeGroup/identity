// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Http;
using NCode.Identity.Jose;
using NCode.Identity.OpenId.Management;

namespace NCode.Identity.OpenId.Accounts.Management;

/// <summary>
/// Provides the rules for the claims that the management API stores on a local account.
/// </summary>
internal static class LocalAccountClaimRules
{
    /// <summary>
    /// Gets the error for a claim type that the account's own fields supply, or <c>null</c> when none of the
    /// <paramref name="claimTypes"/> is reserved. The <c>name</c> claim is the account's display name.
    /// </summary>
    /// <param name="claimTypes">The types of the claims that a request supplies.</param>
    public static ManagementError? GetReservedClaimErrorOrDefault(IEnumerable<string> claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            if (string.Equals(claimType, JoseClaimNames.Payload.Name, StringComparison.Ordinal))
            {
                return new ManagementError
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    Detail =
                        "The 'name' claim is the account's display name; set displayName instead.",
                };
            }
        }

        return null;
    }
}
