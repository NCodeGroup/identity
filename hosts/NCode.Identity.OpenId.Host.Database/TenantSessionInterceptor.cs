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
/// Carries the ambient tenant scope into the Postgres session, so the row-level security policies read the same scope
/// the global query filter does. Before a command runs, it sets <c>app.tenant_id</c> and <c>app.unscoped</c> on the
/// command's connection whenever the ambient state differs from the state last applied to that connection. A connection
/// that opens forgets the state applied to it, so a pooled connection never keeps another request's scope. Postgres
/// undoes a <c>set_config</c> that ran inside a transaction which rolls back, so a rollback, a rollback to a savepoint
/// or a failure of that transaction forgets the state applied in it, and the next command sets it again. A transaction
/// that ends without a commit or a rollback leaves its state recorded, so a command whose transaction is not the one
/// the state was applied in sets it again; a commit makes the state session-durable.
/// </summary>
/// <param name="ambientTenantAccessor">The accessor that holds the ambient tenant scope.</param>
[PublicAPI]
public sealed class TenantSessionInterceptor(IAmbientTenantAccessor ambientTenantAccessor)
    : DbConnectionInterceptor,
        IDbCommandInterceptor,
        IDbTransactionInterceptor
{
    /// <summary>
    /// The statement that sets <c>app.tenant_id</c> and <c>app.unscoped</c> for the session, taking the tenant value as
    /// parameter <c>@t</c> and the unscoped value as parameter <c>@u</c>.
    /// </summary>
    private const string SetConfigCommandText =
        $"SELECT set_config('{TenantSessionSettings.TenantId}', @t, false), set_config('{TenantSessionSettings.Unscoped}', @u, false)";

    /// <summary>
    /// Creates the command that sets <c>app.tenant_id</c> and <c>app.unscoped</c> for the session, in the transaction
    /// of the command it precedes so the two run on the same connection state.
    /// </summary>
    /// <param name="connection">The connection that runs the command.</param>
    /// <param name="transaction">The transaction to enlist the command in, or <see langword="null"/> when none.</param>
    /// <param name="state">The tenant and unscoped values to set.</param>
    /// <returns>The <c>set_config</c> command, ready to execute.</returns>
    private static DbCommand CreateSetConfigCommand(
        DbConnection connection,
        DbTransaction? transaction,
        (string Tenant, string Unscoped) state
    )
    {
        var setConfig = connection.CreateCommand();
        setConfig.CommandText = SetConfigCommandText;
        setConfig.Transaction = transaction;
        var tenantParameter = CreateParameter(setConfig, "t", state.Tenant);
        setConfig.Parameters.Add(tenantParameter);
        var unscopedParameter = CreateParameter(setConfig, "u", state.Unscoped);
        setConfig.Parameters.Add(unscopedParameter);
        return setConfig;
    }

    /// <summary>
    /// Creates a string parameter for the <c>set_config</c> command.
    /// </summary>
    /// <param name="command">The command that owns the parameter.</param>
    /// <param name="name">The parameter name, without a prefix.</param>
    /// <param name="value">The string value of the parameter.</param>
    /// <returns>The parameter, typed as a string.</returns>
    private static DbParameter CreateParameter(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = DbType.String;
        parameter.Value = value;
        return parameter;
    }

    /// <summary>
    /// Gets the state last applied to each open connection, with the transaction it was applied in, or with no
    /// transaction once that transaction committed, so a command on a connection that already holds the ambient state
    /// sets nothing. A connection that opens removes its entry, and so does a rollback, a rollback to a savepoint or a
    /// failure of the transaction that applied it. The table holds a connection weakly, so a connection that is gone
    /// leaves no entry behind.
    /// </summary>
    private ConditionalWeakTable<
        DbConnection,
        StrongBox<(string Tenant, string Unscoped, DbTransaction? Transaction)>
    > AppliedStates { get; } = new();

    /// <summary>
    /// Gets the connection of each transaction that applied a state, so a transaction that rolls back, rolls back to a
    /// savepoint or fails finds the connection whose state it must clear. A commit removes the entry, and the table
    /// holds a transaction weakly, so a transaction that is gone leaves no entry behind.
    /// </summary>
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
    public void TransactionCommitted(
        DbTransaction transaction,
        TransactionEndEventData eventData
    ) => MakeStateAppliedInDurable(transaction);

    /// <inheritdoc />
    public Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        MakeStateAppliedInDurable(transaction);
        return Task.CompletedTask;
    }

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

    /// <summary>
    /// Forgets the state recorded for the connection of a transaction that did not commit, because Postgres undoes a
    /// <c>set_config</c> that ran inside such a transaction. Nothing is forgotten when the transaction applied no
    /// state.
    /// </summary>
    /// <param name="transaction">The transaction that rolled back, rolled back to a savepoint or failed.</param>
    private void ForgetStateAppliedIn(DbTransaction transaction)
    {
        if (AppliedTransactions.TryGetValue(transaction, out var connection))
            AppliedStates.Remove(connection);
    }

    /// <summary>
    /// Makes the state a committed transaction applied session-durable: Postgres keeps a <c>set_config</c> that ran
    /// inside a transaction that commits, so the recorded state belongs to no transaction.
    /// </summary>
    /// <param name="transaction">The transaction that committed.</param>
    private void MakeStateAppliedInDurable(DbTransaction transaction)
    {
        if (!AppliedTransactions.TryGetValue(transaction, out var connection))
            return;

        AppliedTransactions.Remove(transaction);

        if (
            AppliedStates.TryGetValue(connection, out var applied)
            && ReferenceEquals(applied.Value.Transaction, transaction)
        )
        {
            applied.Value = (applied.Value.Tenant, applied.Value.Unscoped, null);
        }
    }

    /// <summary>
    /// Records the state last applied to a connection together with the transaction it was applied in, so a later
    /// command can tell whether the database still holds that state.
    /// </summary>
    /// <param name="connection">The connection the state was applied to.</param>
    /// <param name="transaction">The transaction the state was applied in, or <see langword="null"/> when none.</param>
    /// <param name="state">The tenant and unscoped values that were applied.</param>
    private void RecordAppliedState(
        DbConnection connection,
        DbTransaction? transaction,
        (string Tenant, string Unscoped) state
    )
    {
        var appliedState = new StrongBox<(string, string, DbTransaction?)>(
            (state.Tenant, state.Unscoped, transaction)
        );
        AppliedStates.AddOrUpdate(connection, appliedState);
        if (transaction is not null)
            AppliedTransactions.AddOrUpdate(transaction, connection);
    }

    /// <summary>
    /// Reads the ambient tenant scope as the tenant and unscoped values the session settings carry. The tenant is sent
    /// in its lower-invariant form, the form <c>NormalizedTenantId</c> stores, so row-level security and the EF Core
    /// global query filter match the same rows for a mixed-case route tenant.
    /// </summary>
    /// <returns>The tenant value and the unscoped value of the ambient state.</returns>
    private (string Tenant, string Unscoped) GetAmbientState() =>
        ambientTenantAccessor.IsUnscoped
            ? (string.Empty, TenantSessionSettings.On)
            : (
                ambientTenantAccessor.TenantId?.ToLowerInvariant() ?? string.Empty,
                TenantSessionSettings.Off
            );

    /// <summary>
    /// Decides whether the ambient state must be sent before a command runs. The state is pending when no state was
    /// applied to the connection, when the applied state differs from the ambient state, or when the applied state was
    /// set in a transaction that is not the command's current transaction, which covers a transaction disposed without
    /// a commit and a command that runs outside the transaction after it.
    /// </summary>
    /// <param name="command">The command about to run.</param>
    /// <param name="connection">The connection of the command, when it has one.</param>
    /// <param name="state">The ambient state to send.</param>
    /// <returns><see langword="true"/> when the state must be sent; otherwise, <see langword="false"/>.</returns>
    private bool TryGetPendingState(
        DbCommand command,
        [NotNullWhen(true)] out DbConnection? connection,
        out (string Tenant, string Unscoped) state
    )
    {
        state = GetAmbientState();
        connection = command.Connection;
        if (connection is null)
            return false;

        if (!AppliedStates.TryGetValue(connection, out var applied))
            return true;

        var (tenant, unscoped, transaction) = applied.Value;
        return (tenant, unscoped) != state
            || (transaction is not null && !ReferenceEquals(transaction, command.Transaction));
    }

    /// <summary>
    /// Sends the ambient state to the command's connection when it is pending, then records it.
    /// </summary>
    /// <param name="command">The command about to run.</param>
    private void ApplyAmbientState(DbCommand command)
    {
        if (!TryGetPendingState(command, out var connection, out var state))
            return;

        using var setConfig = CreateSetConfigCommand(connection, command.Transaction, state);
        setConfig.ExecuteNonQuery();

        RecordAppliedState(connection, command.Transaction, state);
    }

    /// <summary>
    /// Sends the ambient state to the command's connection when it is pending, then records it.
    /// </summary>
    /// <param name="command">The command about to run.</param>
    /// <param name="cancellationToken">The token that cancels the <c>set_config</c> command.</param>
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
}
