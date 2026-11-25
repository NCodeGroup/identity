// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory.Logging;

/// <summary>
/// Defines the reserved logging event IDs for the <c>NCode.Identity.OpenId.GlobalDirectory</c> package.
/// </summary>
/// <remarks>
/// This package reserves the <c>31000</c>–<c>31999</c> band per the logging conventions. Values are append-only: never
/// renumber or reuse a shipped identifier. Each constant is named exactly as the <c>Log</c> method it identifies.
/// </remarks>
internal static class EventIds
{
    private const int Base = 31000;

    /// <summary>
    /// Identifies the warning that registering the cell with the global directory failed because the directory is
    /// unavailable; the registration is retried after a delay.
    /// </summary>
    public const int CellRegistrationFailed = Base + 1;

    /// <summary>
    /// Identifies the warning that a pass over the global directory's change feed failed because the directory is
    /// unavailable; the next interval runs the pass again.
    /// </summary>
    public const int DirectoryChangeFeedUnavailable = Base + 2;

    /// <summary>
    /// Identifies the error that a pass over the global directory's change feed failed; the next interval runs the pass
    /// again.
    /// </summary>
    public const int DirectoryChangeFeedPassFailed = Base + 3;

    /// <summary>
    /// Identifies the error that applying a change from the global directory's change feed failed; the pass applies no
    /// change after it, and the next interval retries it.
    /// </summary>
    public const int DirectoryChangeApplyFailed = Base + 4;

    /// <summary>
    /// Identifies the warning that publishing the control-plane cell's outbox failed because the directory is
    /// unavailable; the publisher retries after a delay.
    /// </summary>
    public const int DirectoryOutboxUnavailable = Base + 5;

    /// <summary>
    /// Identifies the error that publishing the control-plane cell's outbox failed; the publisher retries after a
    /// delay.
    /// </summary>
    public const int DirectoryOutboxPublishFailed = Base + 6;

    /// <summary>
    /// Identifies the information that the publisher republished a document whose local revision was behind the
    /// directory's.
    /// </summary>
    public const int DirectoryOutboxRepublished = Base + 7;

    /// <summary>
    /// Identifies the warning that the directory refused the cell's client authentication because it has no
    /// registration for the cell yet; the registration is retried after a delay.
    /// </summary>
    public const int CellNotRegisteredYet = Base + 8;

    /// <summary>
    /// Identifies the error that publishing one document of the control-plane cell's outbox failed; the document stays
    /// pending and the publisher sends the documents after it.
    /// </summary>
    public const int DirectoryOutboxDocumentFailed = Base + 9;

    /// <summary>
    /// Identifies the error that republishing one document of the control-plane cell's outbox failed; the documents
    /// after it are still republished and the pass is retried.
    /// </summary>
    public const int DirectoryOutboxRepublishFailed = Base + 10;
}
