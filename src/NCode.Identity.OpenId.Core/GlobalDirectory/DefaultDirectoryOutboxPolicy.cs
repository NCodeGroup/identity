// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Tenants;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Provides the default implementation of <see cref="IDirectoryOutboxPolicy"/> and
/// <see cref="IDirectoryOutboxNotifier"/>. A cell publishes the replicated documents when it is the control-plane cell
/// of a platform with a directory, and a commit that wrote outbox entries wakes the publisher.
/// </summary>
// See ADR-1536.
internal sealed class DefaultDirectoryOutboxPolicy(
    IPlatformTopology platformTopology,
    ITenantResolver tenantResolver,
    IDirectoryOutboxSignal signal
) : IDirectoryOutboxPolicy, IDirectoryOutboxNotifier
{
    private IPlatformTopology PlatformTopology { get; } = platformTopology;
    private ITenantResolver TenantResolver { get; } = tenantResolver;
    private IDirectoryOutboxSignal Signal { get; } = signal;

    /// <inheritdoc />
    public bool IsPublishing =>
        PlatformTopology.HasDirectory && PlatformTopology.IsControlPlaneCell;

    /// <inheritdoc />
    public string ControlPlaneTenantId => TenantResolver.GetControlPlaneTenantId();

    /// <inheritdoc />
    public void Notify() => Signal.Notify();
}
