// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory.Server.Stores;

/// <summary>
/// Publishes the team documents that the directory's route receives, refusing a document whose team, or a team beneath
/// it, would be nested deeper than the directory stores.
/// </summary>
internal interface IDirectoryTeamWriter
{
    /// <summary>
    /// Publishes a team document for the directory's route and refuses a document whose team, or a team beneath it,
    /// would be nested deeper than the directory stores. A refused document stores nothing.
    /// </summary>
    /// <param name="document">The <see cref="TeamDocument"/> to publish.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="DirectoryTeamWriteResult"/> that is done, or refused.</returns>
    ValueTask<DirectoryTeamWriteResult> PublishAsync(
        TeamDocument document,
        CancellationToken cancellationToken
    );
}
