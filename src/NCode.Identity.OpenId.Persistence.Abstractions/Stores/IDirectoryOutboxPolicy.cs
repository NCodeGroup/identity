// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Persistence.Stores;

/// <summary>
/// Provides an abstraction that decides whether the persistence layer writes the documents that the cell publishes to
/// the global directory into the outbox, in the same unit of work as the change that touches them.
/// </summary>
[PublicAPI]
public interface IDirectoryOutboxPolicy
{
    /// <summary>
    /// Gets a value indicating whether the cell is the control-plane cell of a platform with a directory, which is the
    /// only cell that publishes replicated documents.
    /// </summary>
    bool IsPublishing { get; }

    /// <summary>
    /// Gets the identifier of the control-plane tenant, whose clients and signing secrets the replicated documents
    /// carry.
    /// </summary>
    string ControlPlaneTenantId { get; }
}
