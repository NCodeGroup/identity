// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using NCode.Identity.Secrets.RemoteKeys;

namespace NCode.Identity.OpenId.Management.Transfers;

/// <summary>
/// Represents what an <see cref="ITenantTransferContributor"/> reports after it imports its section.
/// </summary>
// See ADR-1514.
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class TenantTransferImportResult
{
    /// <summary>
    /// Gets the remote keys that the imported secrets reference, which the cell that receives the tenant retags to its
    /// own deployment.
    /// </summary>
    public required IReadOnlyList<RemoteKeyReference> RemoteKeys { get; init; }
}
