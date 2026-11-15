// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;

namespace NCode.Identity.OpenId.GlobalDirectory.Server.Stores;

/// <summary>
/// Represents the outcome of a tenant write through <see cref="IDirectoryTenantWriter"/>: the
/// <see cref="DirectoryTenantWriteOutcome"/> and, for every outcome but <see cref="DirectoryTenantWriteOutcome.Done"/>,
/// the reason.
/// </summary>
// See ADR-1411.
[ExcludeFromCodeCoverage]
internal sealed class DirectoryTenantWriteResult
{
    private DirectoryTenantWriteResult(DirectoryTenantWriteOutcome outcome, string? detail)
    {
        Outcome = outcome;
        Detail = detail;
    }

    /// <summary>
    /// Gets the outcome of the write.
    /// </summary>
    public DirectoryTenantWriteOutcome Outcome { get; }

    /// <summary>
    /// Gets the reason the write was refused, or <c>null</c> when the outcome is
    /// <see cref="DirectoryTenantWriteOutcome.Done"/>.
    /// </summary>
    public string? Detail { get; }

    /// <summary>
    /// Creates the result of a write that is committed or has nothing to write.
    /// </summary>
    /// <returns>A <see cref="DirectoryTenantWriteResult"/> whose outcome is <see cref="DirectoryTenantWriteOutcome.Done"/>.</returns>
    public static DirectoryTenantWriteResult Done() => new(DirectoryTenantWriteOutcome.Done, null);

    /// <summary>
    /// Creates the result of a write that the calling cell is not authorized to make.
    /// </summary>
    /// <param name="detail">The reason the write was refused.</param>
    /// <returns>A <see cref="DirectoryTenantWriteResult"/> whose outcome is <see cref="DirectoryTenantWriteOutcome.Forbidden"/>.</returns>
    public static DirectoryTenantWriteResult Forbidden(string detail) =>
        new(DirectoryTenantWriteOutcome.Forbidden, detail);

    /// <summary>
    /// Creates the result of a write to a tenant that has no record.
    /// </summary>
    /// <param name="detail">The reason the write was refused.</param>
    /// <returns>A <see cref="DirectoryTenantWriteResult"/> whose outcome is <see cref="DirectoryTenantWriteOutcome.NotFound"/>.</returns>
    public static DirectoryTenantWriteResult NotFound(string detail) =>
        new(DirectoryTenantWriteOutcome.NotFound, detail);

    /// <summary>
    /// Creates the result of a write that conflicts with the directory's records.
    /// </summary>
    /// <param name="detail">The reason the write was refused.</param>
    /// <returns>A <see cref="DirectoryTenantWriteResult"/> whose outcome is <see cref="DirectoryTenantWriteOutcome.Conflict"/>.</returns>
    public static DirectoryTenantWriteResult Conflict(string detail) =>
        new(DirectoryTenantWriteOutcome.Conflict, detail);
}
