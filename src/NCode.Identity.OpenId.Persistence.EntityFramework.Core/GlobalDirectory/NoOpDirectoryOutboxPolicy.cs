// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Core.GlobalDirectory;

/// <summary>
/// Provides the <see cref="IDirectoryOutboxPolicy"/> of a cell that publishes no replicated documents.
/// </summary>
internal sealed class NoOpDirectoryOutboxPolicy : IDirectoryOutboxPolicy
{
    /// <inheritdoc />
    public bool IsPublishing => false;

    /// <inheritdoc />
    public string ControlPlaneTenantId => string.Empty;
}
