// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Http;
using NCode.Identity.OpenId.Persistence.Tenants;

namespace NCode.Identity.OpenId.Management.Endpoints;

public class ControlPlaneScopeEndpointFilterTests
{
    private static EndpointFilterInvocationContext CreateContext() =>
        EndpointFilterInvocationContext.Create(new DefaultHttpContext());

    #region InvokeAsync Tests

    [Fact]
    public async Task InvokeAsync_WhenTheEndpointRuns_TheScopeIsUnscopedWithTheControlPlaneReason()
    {
        var accessor = new StubAmbientTenantAccessor();
        var filter = new ControlPlaneScopeEndpointFilter(accessor);
        var context = CreateContext();
        bool? isUnscoped = null;
        string? reason = null;
        string? tenantId = null;
        EndpointFilterDelegate next = _ =>
        {
            isUnscoped = accessor.IsUnscoped;
            reason = accessor.UnscopedReason;
            tenantId = accessor.TenantId;
            return ValueTask.FromResult<object?>(null);
        };

        await filter.InvokeAsync(context, next);

        Assert.True(isUnscoped);
        Assert.Equal("management control plane", reason);
        Assert.Null(tenantId);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheEndpointSucceeds_ReturnsItsResultAndRestoresTheScope()
    {
        var accessor = new StubAmbientTenantAccessor();
        var filter = new ControlPlaneScopeEndpointFilter(accessor);
        var context = CreateContext();
        var expected = TypedResults.NoContent();
        EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>(expected);

        var result = await filter.InvokeAsync(context, next);

        Assert.Same(expected, result);
        Assert.False(accessor.IsUnscoped);
        Assert.Null(accessor.UnscopedReason);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheEndpointThrows_PropagatesAndRestoresTheScope()
    {
        var accessor = new StubAmbientTenantAccessor();
        var filter = new ControlPlaneScopeEndpointFilter(accessor);
        var context = CreateContext();
        EndpointFilterDelegate next = _ =>
            ValueTask.FromException<object?>(new InvalidOperationException("failure"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            filter.InvokeAsync(context, next).AsTask()
        );

        Assert.False(accessor.IsUnscoped);
        Assert.Null(accessor.UnscopedReason);
    }

    #endregion

    private sealed class StubAmbientTenantAccessor : IAmbientTenantAccessor
    {
        private Stack<(string? TenantId, string? Reason)> States { get; } = new();

        public bool IsScoped => TenantId is not null;

        public string? TenantId => States.TryPeek(out var state) ? state.TenantId : null;

        public bool IsUnscoped => States.TryPeek(out var state) && state.Reason is not null;

        public string? UnscopedReason => States.TryPeek(out var state) ? state.Reason : null;

        public IDisposable BeginScope(string tenantId)
        {
            States.Push((tenantId, null));
            return new Scope(States);
        }

        public IDisposable BeginUnscoped(string reason)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);

            States.Push((null, reason));
            return new Scope(States);
        }

        private sealed class Scope(Stack<(string? TenantId, string? Reason)> states) : IDisposable
        {
            public void Dispose() => states.Pop();
        }
    }
}
