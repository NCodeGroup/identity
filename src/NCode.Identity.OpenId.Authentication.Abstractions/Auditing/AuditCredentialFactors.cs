// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Authentication.Auditing;

/// <summary>
/// The well-known credential factors an end user proves as a second factor at sign-in, recorded as the
/// <c>details.factor</c> of a <see cref="SecondFactorAuditEvent"/>.
/// </summary>
/// <remarks>
/// The factors are an open vocabulary: an extension that adds a second factor of its own passes its own name. Names are
/// compared ordinally.
/// </remarks>
// See ADR-1150.
[PublicAPI]
public static class AuditCredentialFactors
{
    /// <summary>
    /// The second factor that a code mailed to the account's address proves.
    /// </summary>
    // See ADR-1533.
    public const string EmailCode = "email_code";
}
