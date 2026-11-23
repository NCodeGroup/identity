// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Authentication.Admission;

/// <summary>
/// Contains the options for admitting requests to a replica's OpenID protocol endpoints, where a replica sheds a
/// tenant's requests once its in-flight requests fill the share of its capacity that the tenant's tier allows.
/// </summary>
// See ADR-1523.
[PublicAPI]
public sealed class AdmissionOptions
{
    /// <summary>
    /// Gets the name of the subsection of the OpenID configuration section that binds these options.
    /// </summary>
    public const string SectionName = "Admission";

    /// <summary>
    /// Gets or sets the most metered protocol requests one replica serves at once; a request past its tenant's share of
    /// it is shed. Unset, the default, sheds nothing. It is at least one.
    /// </summary>
    public int? ConcurrencyLimit { get; set; }

    /// <summary>
    /// Gets or sets how long a shed request's caller waits before retrying, sent as <c>Retry-After</c> in whole
    /// seconds. The default is one second. It is at least one second and at most one hour.
    /// </summary>
    public TimeSpan RetryAfter { get; set; } = TimeSpan.FromSeconds(1);
}
