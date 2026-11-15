// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NCode.Identity.OpenId.Persistence.Tenants;

namespace NCode.Identity.OpenId.Host.Database;

/// <summary>
/// Carries the ambient tenant scope into the Postgres session, so the row-level security policies read the same
/// scope the global query filter does. Before a command runs, it sets <c>app.tenant_id</c> and <c>app.unscoped</c> on
/// the command's connection whenever the ambient state differs from the state last applied to that connection. A
/// connection that opens forgets the state applied to it, so a pooled connection never keeps another request's scope.
/// Postgres undoes a <c>set_config</c> that ran inside a transaction which rolls back, so a rollback, a rollback to a
/// savepoint or a failure of that transaction forgets the state applied in it, and the next command sets it again.
/// </summary>
/// <param name="ambientTenantAccessor">The accessor that holds the ambient tenant scope.</param>
[PublicAPI]
public sealed class TenantSessionInterceptor(IAmbientTenantAccessor ambientTenantAccessor)
    : DbConnectionInterceptor,
        IDbCommandInterceptor,
        IDbTransactionInterceptor
{
    private const string SetConfigCommandText =
        "SELECT set_config('app.tenant_id', @t, false), set_config('app.unscoped', @u, false)";

    private const string UnscopedOn = "on";
    private const string UnscopedOff = "off";

    private ConditionalWeakTable<
        DbConnection,
        StrongBox<(string Tenant, string Unscoped)>
    > AppliedStates { get; } = new();

    private ConditionalWeakTable<DbTransaction, DbConnection> AppliedTransactions { get; } = new();

    #region Connection

    /// <inheritdoc />
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        AppliedStates.Remove(connection);
        base.ConnectionOpened(connection, eventData);
    }

    /// <inheritdoc />
    public override Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        AppliedStates.Remove(connection);
        return base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    #endregion

    #region Command

    /// <inheritdoc />
    public InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result
    )
    {
        ApplyAmbientState(command);
        return result;
    }

    /// <inheritdoc />
    public async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        await ApplyAmbientStateAsync(command, cancellationToken);
        return result;
    }

    /// <inheritdoc />
    public InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result
    )
    {
        ApplyAmbientState(command);
        return result;
    }

    /// <inheritdoc />
    public async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        await ApplyAmbientStateAsync(command, cancellationToken);
        return result;
    }

    /// <inheritdoc />
    public InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result
    )
    {
        ApplyAmbientState(command);
        return result;
    }

    /// <inheritdoc />
    public async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default
    )
    {
        await ApplyAmbientStateAsync(command, cancellationToken);
        return result;
    }

    #endregion

    #region Transaction

    /// <inheritdoc />
    public void TransactionRolledBack(
        DbTransaction transaction,
        TransactionEndEventData eventData
    ) => ForgetStateAppliedIn(transaction);

    /// <inheritdoc />
    public Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        ForgetStateAppliedIn(transaction);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void RolledBackToSavepoint(DbTransaction transaction, TransactionEventData eventData) =>
        ForgetStateAppliedIn(transaction);

    /// <inheritdoc />
    public Task RolledBackToSavepointAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        ForgetStateAppliedIn(transaction);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData) =>
        ForgetStateAppliedIn(transaction);

    /// <inheritdoc />
    public Task TransactionFailedAsync(
        DbTransaction transaction,
        TransactionErrorEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        ForgetStateAppliedIn(transaction);
        return Task.CompletedTask;
    }

    #endregion

    private void ForgetStateAppliedIn(DbTransaction transaction)
    {
        if (AppliedTransactions.TryGetValue(transaction, out var connection))
            AppliedStates.Remove(connection);
    }

    private void RecordAppliedState(
        DbConnection connection,
        DbTransaction? transaction,
        (string Tenant, string Unscoped) state
    )
    {
        AppliedStates.AddOrUpdate(connection, new StrongBox<(string, string)>(state));
        if (transaction is not null)
            AppliedTransactions.AddOrUpdate(transaction, connection);
    }

    // The tenant is sent in its lower-invariant form, the form NormalizedTenantId stores, so row-level security and
    // the EF Core global query filter match the same rows for a mixed-case route tenant.
    private (string Tenant, string Unscoped) GetAmbientState() =>
        ambientTenantAccessor.IsUnscoped
            ? (string.Empty, UnscopedOn)
            : (ambientTenantAccessor.TenantId?.ToLowerInvariant() ?? string.Empty, UnscopedOff);

    private bool TryGetPendingState(
        DbCommand command,
        [NotNullWhen(true)] out DbConnection? connection,
        out (string Tenant, string Unscoped) state
    )
    {
        state = GetAmbientState();
        connection = command.Connection;
        return connection is not null
            && (!AppliedStates.TryGetValue(connection, out var applied) || applied.Value != state);
    }

    private void ApplyAmbientState(DbCommand command)
    {
        if (!TryGetPendingState(command, out var connection, out var state))
            return;

        using var setConfig = CreateSetConfigCommand(connection, command.Transaction, state);
        setConfig.ExecuteNonQuery();

        RecordAppliedState(connection, command.Transaction, state);
    }

    private async ValueTask ApplyAmbientStateAsync(
        DbCommand command,
        CancellationToken cancellationToken
    )
    {
        if (!TryGetPendingState(command, out var connection, out var state))
            return;

        await using var setConfig = CreateSetConfigCommand(connection, command.Transaction, state);
        await setConfig.ExecuteNonQueryAsync(cancellationToken);

        RecordAppliedState(connection, command.Transaction, state);
    }

    private static DbCommand CreateSetConfigCommand(
        DbConnection connection,
        DbTransaction? transaction,
        (string Tenant, string Unscoped) state
    )
    {
        var setConfig = connection.CreateCommand();
        setConfig.CommandText = SetConfigCommandText;
        setConfig.Transaction = transaction;
        setConfig.Parameters.Add(CreateParameter(setConfig, "t", state.Tenant));
        setConfig.Parameters.Add(CreateParameter(setConfig, "u", state.Unscoped));
        return setConfig;
    }

    private static DbParameter CreateParameter(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = DbType.String;
        parameter.Value = value;
        return parameter;
    }
}
