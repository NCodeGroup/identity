// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.Tenants;

/// <summary>
/// A scope that restores the enclosing ambient tenant scope when disposed.
/// </summary>
/// <param name="ambientState">The ambient state the scope was opened in.</param>
/// <param name="previous">The enclosing scope's state, which disposal restores.</param>
internal sealed class AmbientTenantScope(
    AsyncLocal<AmbientTenantScopeState?> ambientState,
    AmbientTenantScopeState? previous
) : IDisposable
{
    private bool Disposed { get; set; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Disposed)
            return;

        Disposed = true;
        ambientState.Value = previous;
    }
}
