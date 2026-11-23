// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.Accounts.Authentication.EmailCodes;

/// <summary>
/// Contains the purposes a one-time email code is sent for. A code of one purpose never completes a flow of another,
/// because the purpose is part of the key the flow is stored under.
/// </summary>
// See ADR-1533.
internal static class EmailCodePurposes
{
    /// <summary>
    /// Contains the purpose of a code that verifies the account's email address.
    /// </summary>
    public const string VerifyEmail = "verify_email";

    /// <summary>
    /// Contains the purpose of a code that resets the account's forgotten password.
    /// </summary>
    public const string ResetPassword = "reset_password";

    /// <summary>
    /// Contains the purpose of a code that completes an email second factor of a sign-in.
    /// </summary>
    public const string SignIn = "sign_in";
}
