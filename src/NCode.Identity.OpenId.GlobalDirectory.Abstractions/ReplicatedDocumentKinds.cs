// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Contains the kinds of <see cref="ReplicatedDocument"/>.
/// </summary>
[PublicAPI]
public static class ReplicatedDocumentKinds
{
    /// <summary>
    /// Contains the kind of the platform's authority document: 'platform-authority'. Its key is empty.
    /// </summary>
    public const string PlatformAuthority = "platform-authority";

    /// <summary>
    /// Contains the kind of a team's authority document: 'team-authority'. Its key is the team identifier.
    /// </summary>
    public const string TeamAuthority = "team-authority";

    /// <summary>
    /// Contains the kind of the management keys document: 'management-keys'. Its key is empty.
    /// </summary>
    public const string ManagementKeys = "management-keys";
}
