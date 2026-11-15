// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NCode.Identity.OpenId.Host.Database.EndToEnd.Tests.Infrastructure;
using NCode.Identity.OpenId.Persistence.EntityFramework;
using NCode.Identity.OpenId.Persistence.Tenants;
using Npgsql;
using Xunit;

namespace NCode.Identity.OpenId.Host.Database.EndToEnd.Tests;

/// <summary>
/// Proves on a real Postgres database that row-level security confines the runtime role to the ambient tenant scope.
/// Each test reads and writes the <c>ResourceServers</c> table through an <see cref="OpenIdDbContext"/> on the runtime
/// connection, with raw SQL so the context's global query filter never applies and the database's policy alone decides
/// which rows the role sees.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RowLevelSecurityEndToEndTests(PostgresFixture fixture)
{
    private const string RlsViolationSqlState = "42501";

    #region Query Tests

    [Fact]
    public async Task Query_UnsetScope_SeesNoRows()
    {
        var tenantA = await SeedTenantAsync(CancellationToken.None);
        var tenantB = await SeedTenantAsync(CancellationToken.None);
        var accessor = new TestAmbientTenantAccessor();

        var visible = await QueryVisibleAsync(
            accessor,
            [tenantA.ResourceServerId, tenantB.ResourceServerId],
            CancellationToken.None
        );

        Assert.Empty(visible);
    }

    [Fact]
    public async Task Query_TenantScope_SeesOnlyItsTenant()
    {
        var tenantA = await SeedTenantAsync(CancellationToken.None);
        var tenantB = await SeedTenantAsync(CancellationToken.None);
        var accessor = new TestAmbientTenantAccessor();
        using var scope = accessor.BeginScope(tenantA.TenantId);

        var visible = await QueryVisibleAsync(
            accessor,
            [tenantA.ResourceServerId, tenantB.ResourceServerId],
            CancellationToken.None
        );

        Assert.Equal(tenantA.ResourceServerId, Assert.Single(visible));
    }

    [Fact]
    public async Task Query_Unscoped_SeesAll()
    {
        var tenantA = await SeedTenantAsync(CancellationToken.None);
        var tenantB = await SeedTenantAsync(CancellationToken.None);
        var accessor = new TestAmbientTenantAccessor();
        using var scope = accessor.BeginUnscoped("end-to-end test");

        var visible = await QueryVisibleAsync(
            accessor,
            [tenantA.ResourceServerId, tenantB.ResourceServerId],
            CancellationToken.None
        );

        Assert.Equal(
            new[] { tenantA.ResourceServerId, tenantB.ResourceServerId }.Order(),
            visible.Order()
        );
    }

    #endregion

    #region Insert Tests

    [Fact]
    public async Task Insert_OtherTenantRow_IsRejectedByPolicy()
    {
        var tenantA = await SeedTenantAsync(CancellationToken.None);
        var tenantB = await SeedTenantAsync(CancellationToken.None);
        var accessor = new TestAmbientTenantAccessor();
        using var scope = accessor.BeginScope(tenantA.TenantId);
        await using var context = CreateContext(accessor);

        var resourceServerId = Random.Shared.NextInt64(1, long.MaxValue);
        var name = $"rls-{Guid.NewGuid():N}";
        const string settingsJson = "{}";

        var exception = await Assert.ThrowsAsync<PostgresException>(async () =>
            await context.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO "ResourceServers" (
                    "Id", "TenantId", "NormalizedTenantId", "ResourceServerId", "NormalizedResourceServerId",
                    "Identifier", "NormalizedIdentifier", "ConcurrencyToken", "ScopesConcurrencyToken",
                    "SettingsConcurrencyToken", "SecretsConcurrencyToken", "Name", "IsSystem", "IsDisabled",
                    "SettingsJson")
                VALUES (
                    {resourceServerId}, {tenantB.TenantRowId}, {tenantB.TenantId}, {name}, {name},
                    {name}, {name}, 'c', 'c',
                    'c', 'c', {name}, false, false,
                    {settingsJson})
                """,
                CancellationToken.None
            )
        );

        Assert.Equal(RlsViolationSqlState, exception.SqlState);
    }

    #endregion

    #region RuntimeRolePostureCheck Tests

    [Fact]
    public async Task RuntimeRole_OwnerConnection_IsRefusedByPostureCheck()
    {
        var runtimeCheck = new RuntimeRolePostureCheck(
            fixture.RuntimeConnectionString,
            localDevelopment: false,
            NullLogger<RuntimeRolePostureCheck>.Instance
        );
        var ownerCheck = new RuntimeRolePostureCheck(
            fixture.OwnerConnectionString,
            localDevelopment: false,
            NullLogger<RuntimeRolePostureCheck>.Instance
        );

        var runtimeException = await Record.ExceptionAsync(() =>
            runtimeCheck.StartAsync(CancellationToken.None)
        );

        Assert.Null(runtimeException);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ownerCheck.StartAsync(CancellationToken.None)
        );
    }

    #endregion

    private OpenIdDbContext CreateContext(IAmbientTenantAccessor accessor)
    {
        var optionsBuilder = new DbContextOptionsBuilder<OpenIdDbContext>();
        optionsBuilder.UseOpenIdNpgsql(fixture.RuntimeConnectionString, accessor);
        optionsBuilder.UseApplicationServiceProvider(fixture.ServiceProvider);

        return new OpenIdDbContext(
            optionsBuilder.Options,
            accessor,
            fixture.ServiceProvider.GetServices<IOpenIdModelContributor>()
        );
    }

    private async ValueTask<List<long>> QueryVisibleAsync(
        IAmbientTenantAccessor accessor,
        long[] resourceServerIds,
        CancellationToken cancellationToken
    )
    {
        await using var context = CreateContext(accessor);

        return await context
            .Database.SqlQuery<long>(
                $"""SELECT "Id" AS "Value" FROM "ResourceServers" WHERE "Id" = ANY ({resourceServerIds})"""
            )
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Seeds a team, a tenant and one of its resource servers through the owner connection, which is a superuser and
    /// so bypasses row-level security. Every call uses fresh identifiers, so tests that share the container never see
    /// each other's rows.
    /// </summary>
    private async ValueTask<(
        string TenantId,
        long TenantRowId,
        long ResourceServerId
    )> SeedTenantAsync(CancellationToken cancellationToken)
    {
        var tenantId = $"rls-{Guid.NewGuid():N}";
        var teamRowId = Random.Shared.NextInt64(1, long.MaxValue);
        var tenantRowId = Random.Shared.NextInt64(1, long.MaxValue);
        var resourceServerId = Random.Shared.NextInt64(1, long.MaxValue);

        await using var connection = new NpgsqlConnection(fixture.OwnerConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO "Teams" (
                "Id", "TeamId", "NormalizedTeamId", "Path", "ConcurrencyToken", "SettingsConcurrencyToken",
                "IsDisabled", "DisplayName", "SettingsJson", "RootKeysJson", "DirectorySequence")
            VALUES (@teamRowId, @name, @name, @name, 'c', 'c', false, @name, '{}', '{}', 0);

            INSERT INTO "Tenants" (
                "Id", "TenantId", "NormalizedTenantId", "ConcurrencyToken", "SettingsConcurrencyToken",
                "SecretsConcurrencyToken", "IsDisabled", "DisplayName", "SettingsJson", "TeamId")
            VALUES (@tenantRowId, @name, @name, 'c', 'c', 'c', false, @name, '{}', @teamRowId);

            INSERT INTO "ResourceServers" (
                "Id", "TenantId", "NormalizedTenantId", "ResourceServerId", "NormalizedResourceServerId",
                "Identifier", "NormalizedIdentifier", "ConcurrencyToken", "ScopesConcurrencyToken",
                "SettingsConcurrencyToken", "SecretsConcurrencyToken", "Name", "IsSystem", "IsDisabled",
                "SettingsJson")
            VALUES (
                @resourceServerId, @tenantRowId, @name, @name, @name,
                @name, @name, 'c', 'c',
                'c', 'c', @name, false, false,
                '{}');
            """,
            connection
        );
        command.Parameters.AddWithValue("teamRowId", teamRowId);
        command.Parameters.AddWithValue("tenantRowId", tenantRowId);
        command.Parameters.AddWithValue("resourceServerId", resourceServerId);
        command.Parameters.AddWithValue("name", tenantId);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return (tenantId, tenantRowId, resourceServerId);
    }

    /// <summary>
    /// An <see cref="IAmbientTenantAccessor"/> that keeps its state in fields, so a scope a test opens applies to every
    /// context the test creates afterwards.
    /// </summary>
    private sealed class TestAmbientTenantAccessor : IAmbientTenantAccessor
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
}
