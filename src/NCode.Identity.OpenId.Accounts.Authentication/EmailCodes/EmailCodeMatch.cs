// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;

namespace NCode.Identity.OpenId.Accounts.Authentication.EmailCodes;

/// <summary>
/// Represents the flow that a right code matched: the account the code was sent for and the address it was mailed to.
/// </summary>
/// <param name="LocalAccountId">The identifier of the local account the code was sent for.</param>
/// <param name="Email">The email address the code was mailed to.</param>
// See ADR-1533.
// A data record with no behavior to test.
[ExcludeFromCodeCoverage]
internal sealed record EmailCodeMatch(string LocalAccountId, string Email);
