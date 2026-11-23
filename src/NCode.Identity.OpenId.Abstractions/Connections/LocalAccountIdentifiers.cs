// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Connections;

/// <summary>
/// Contains the values of the <c>local_account_identifier</c> setting.
/// </summary>
// See ADR-1535.
[PublicAPI]
public static class LocalAccountIdentifiers
{
    /// <summary>
    /// An account requires an email address.
    /// </summary>
    public const string Email = "email";

    /// <summary>
    /// An account requires a user name.
    /// </summary>
    public const string UserName = "user_name";

    /// <summary>
    /// An account requires at least one of an email address and a user name.
    /// </summary>
    public const string EmailOrUserName = "email_or_user_name";
}
