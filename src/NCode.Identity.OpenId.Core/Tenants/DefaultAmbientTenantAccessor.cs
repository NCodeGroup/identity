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
    private sealed record ScopeState(string? TenantId, bool IsUnscoped, string? Reason);

    private AsyncLocal<ScopeState?> AmbientState { get; } = new();

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

        return Begin(new ScopeState(tenantId, IsUnscoped: false, Reason: null));
    }

    /// <inheritdoc />
    public IDisposable BeginUnscoped(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return Begin(new ScopeState(TenantId: null, IsUnscoped: true, reason));
    }

    private Scope Begin(ScopeState state)
    {
        var previous = AmbientState.Value;
        AmbientState.Value = state;
        return new Scope(this, previous);
    }

    private sealed class Scope(DefaultAmbientTenantAccessor owner, ScopeState? previous)
        : IDisposable
    {
        private bool Disposed { get; set; }

        public void Dispose()
        {
            if (Disposed)
                return;

            Disposed = true;
            owner.AmbientState.Value = previous;
        }
    }
}
