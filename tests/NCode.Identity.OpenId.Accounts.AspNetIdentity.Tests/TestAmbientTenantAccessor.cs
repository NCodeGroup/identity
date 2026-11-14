// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Persistence.Tenants;

namespace NCode.Identity.OpenId.Accounts.AspNetIdentity;

/// <summary>
/// An <see cref="IAmbientTenantAccessor"/> for tests that keeps its state in fields, so a scope opened in a test's
/// constructor applies to the whole test.
/// </summary>
public sealed class TestAmbientTenantAccessor : IAmbientTenantAccessor
{
    private string? UnscopedReasonValue { get; set; }

    public bool IsScoped => TenantId is not null;

    public string? TenantId { get; private set; }

    public bool IsUnscoped { get; private set; }

    public string? UnscopedReason => IsUnscoped ? UnscopedReasonValue : null;

    public IDisposable BeginScope(string tenantId) => Begin(tenantId, false, null);

    public IDisposable BeginUnscoped(string reason) => Begin(null, true, reason);

    private Scope Begin(string? tenantId, bool isUnscoped, string? reason)
    {
        var scope = new Scope(this, TenantId, IsUnscoped, UnscopedReasonValue);
        TenantId = tenantId;
        IsUnscoped = isUnscoped;
        UnscopedReasonValue = reason;
        return scope;
    }

    private sealed class Scope(
        TestAmbientTenantAccessor owner,
        string? tenantId,
        bool isUnscoped,
        string? reason
    ) : IDisposable
    {
        public void Dispose()
        {
            owner.TenantId = tenantId;
            owner.IsUnscoped = isUnscoped;
            owner.UnscopedReasonValue = reason;
        }
    }
}
