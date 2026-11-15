// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory.Server.Stores;

/// <summary>
/// Identifies the outcome of a tenant write that <see cref="IDirectoryTenantWriter"/> authorizes and commits in one
/// transaction.
/// </summary>
// See ADR-1411.
internal enum DirectoryTenantWriteOutcome
{
    /// <summary>
    /// The write is committed, or it has nothing to write because the directory already holds its result.
    /// </summary>
    Done,

    /// <summary>
    /// The calling cell neither holds the tenant nor is the control-plane cell.
    /// </summary>
    Forbidden,

    /// <summary>
    /// The tenant has no record to write to.
    /// </summary>
    NotFound,

    /// <summary>
    /// The write conflicts with the record of the tenant, with another tenant or with a retired identifier.
    /// </summary>
    Conflict,
}
