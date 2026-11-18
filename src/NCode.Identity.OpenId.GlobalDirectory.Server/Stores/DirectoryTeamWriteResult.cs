// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;

namespace NCode.Identity.OpenId.GlobalDirectory.Server.Stores;

/// <summary>
/// Represents the outcome of a team publish through <see cref="IDirectoryTeamWriter"/>: the document is published, or
/// it is refused with the reason.
/// </summary>
/// <param name="detail">
/// The reason the publish was refused, or <c>null</c> when the document is published or has nothing to write.
/// </param>
[ExcludeFromCodeCoverage]
internal sealed class DirectoryTeamWriteResult(string? detail)
{
    /// <summary>
    /// Creates the result of a publish that is committed or has nothing to write.
    /// </summary>
    /// <returns>A <see cref="DirectoryTeamWriteResult"/> that is done.</returns>
    public static DirectoryTeamWriteResult Done() => new(null);

    /// <summary>
    /// Creates the result of a publish that the directory refuses.
    /// </summary>
    /// <param name="detail">The reason the publish was refused.</param>
    /// <returns>A <see cref="DirectoryTeamWriteResult"/> that is refused.</returns>
    public static DirectoryTeamWriteResult Refused(string detail) => new(detail);

    /// <summary>
    /// Gets a value indicating whether the publish is done.
    /// </summary>
    [MemberNotNullWhen(false, nameof(Detail))]
    public bool IsDone => Detail is null;

    /// <summary>
    /// Gets the reason the publish was refused, or <c>null</c> when it is done.
    /// </summary>
    public string? Detail { get; } = detail;
}
