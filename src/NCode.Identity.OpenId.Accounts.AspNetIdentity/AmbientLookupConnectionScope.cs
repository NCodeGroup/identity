// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.Accounts.AspNetIdentity;

/// <summary>
/// The scope that <see cref="AmbientLookupConnection.BeginScope"/> opens. Disposing it restores the connection that was
/// current when it opened.
/// </summary>
/// <param name="previous">The identifier of the connection to restore, or <c>null</c> for none.</param>
internal sealed class AmbientLookupConnectionScope(string? previous) : IDisposable
{
    private bool IsDisposed { get; set; }

    private string? Previous { get; } = previous;

    /// <inheritdoc />
    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        AmbientLookupConnection.Restore(Previous);
    }
}
