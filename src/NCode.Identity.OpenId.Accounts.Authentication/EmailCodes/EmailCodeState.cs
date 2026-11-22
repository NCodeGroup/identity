// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;

namespace NCode.Identity.OpenId.Accounts.Authentication.EmailCodes;

/// <summary>
/// Represents the state of one email-code flow that is stored in the shared <c>IExpiringValueSet</c>: the account the
/// code was sent for, the hash of the code, and the wrong guesses counted so far. The code itself is never stored.
/// </summary>
/// <param name="LocalAccountId">The identifier of the local account the code was sent for.</param>
/// <param name="CodeHash">The SHA-256 hash of the flow's handle and the code.</param>
/// <param name="FailedAttempts">The number of wrong codes presented to the flow.</param>
/// <param name="ExpiresWhen">The instant after which the flow no longer exists.</param>
// See ADR-1533.
// A data record with no behavior to test.
[ExcludeFromCodeCoverage]
internal sealed record EmailCodeState(
    string LocalAccountId,
    byte[] CodeHash,
    int FailedAttempts,
    DateTimeOffset ExpiresWhen
);
