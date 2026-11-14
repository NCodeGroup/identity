// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Transfers;

/// <summary>
/// Represents the part of a tenant's export that one <see cref="ITenantTransferContributor"/> writes and reads back.
/// </summary>
// See ADR-1514.
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class TenantTransferSection
{
    /// <summary>
    /// Gets the <see cref="ITenantTransferContributor.Name"/> of the contributor that wrote the section.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the contributor's records as a <c>JSON</c> document. Secret values stay as their protected envelopes.
    /// </summary>
    public required JsonElement Json { get; init; }
}
