// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.Accounts.AspNetIdentity;

/// <summary>
/// Holds the connection that the current asynchronous flow's ASP.NET Core Identity calls are scoped to. The store opens
/// a scope around each <c>UserManager&lt;TUser&gt;</c> call, and <see cref="TenantLookupNormalizer"/> reads the
/// connection from it, because ASP.NET Core Identity's <c>ILookupNormalizer</c> takes only the value to normalize.
/// </summary>
internal static class AmbientLookupConnection
{
    private static AsyncLocal<string?> Current { get; } = new();

    /// <summary>
    /// Gets the identifier of the connection of the current scope, or <c>null</c> outside a connection scope.
    /// </summary>
    public static string? ConnectionId => Current.Value;

    /// <summary>
    /// Opens a scope in which lookups are normalized for a connection. Disposing it restores the scope that was current
    /// when it opened.
    /// </summary>
    /// <param name="connectionId">The identifier of the connection that the scope's lookups belong to.</param>
    /// <returns>The <see cref="IDisposable"/> that closes the scope.</returns>
    public static IDisposable BeginScope(string connectionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionId);

        var previous = Current.Value;
        Current.Value = connectionId;
        return new Scope(previous);
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        private bool IsDisposed { get; set; }

        private string? Previous { get; } = previous;

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;
            Current.Value = Previous;
        }
    }
}
