// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Persistence.Tenants;

namespace NCode.Identity.OpenId.Tenants;

/// <summary>
/// Provides a default implementation of <see cref="IAmbientTenantAccessor"/> that stores the ambient tenant scope in
/// an <see cref="AsyncLocal{T}"/> so it flows across asynchronous calls within a request, including data-access
/// components created outside the current dependency injection scope.
/// </summary>
internal sealed class DefaultAmbientTenantAccessor : IAmbientTenantAccessor
{
    private AsyncLocal<AmbientTenantScopeState?> AmbientState { get; } = new();

    /// <inheritdoc />
    public bool IsScoped => AmbientState.Value?.TenantId is not null;

    /// <inheritdoc />
    public string? TenantId => AmbientState.Value?.TenantId;

    /// <inheritdoc />
    public bool IsUnscoped => AmbientState.Value?.IsUnscoped ?? false;

    /// <inheritdoc />
    public string? UnscopedReason =>
        AmbientState.Value is { IsUnscoped: true } state ? state.Reason : null;

    /// <inheritdoc />
    public IDisposable BeginScope(string tenantId)
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        return Begin(new AmbientTenantScopeState(tenantId, IsUnscoped: false, Reason: null));
    }

    /// <inheritdoc />
    public IDisposable BeginUnscoped(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return Begin(new AmbientTenantScopeState(TenantId: null, IsUnscoped: true, reason));
    }

    /// <summary>
    /// Makes the specified state the ambient state and returns the scope that restores the previous state when it is
    /// disposed.
    /// </summary>
    /// <param name="state">The ambient state to apply.</param>
    /// <returns>The scope that restores the previous ambient state when it is disposed.</returns>
    private AmbientTenantScope Begin(AmbientTenantScopeState state)
    {
        var previous = AmbientState.Value;
        AmbientState.Value = state;
        return new AmbientTenantScope(AmbientState, previous);
    }
}
