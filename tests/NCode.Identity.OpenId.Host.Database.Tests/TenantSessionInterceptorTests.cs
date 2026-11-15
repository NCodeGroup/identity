// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NCode.Identity.OpenId.Persistence.Tenants;
using Xunit;

namespace NCode.Identity.OpenId.Host.Database.Tests;

public sealed class TenantSessionInterceptorTests
{
    private const string SetConfigPrefix = "SELECT set_config('app.tenant_id'";

    private static (TenantSessionInterceptor Interceptor, FakeAccessor Accessor) CreateInterceptor()
    {
        var accessor = new FakeAccessor();
        return (new TenantSessionInterceptor(accessor), accessor);
    }

    private static FakeCommand CreateCommand(
        FakeConnection connection,
        FakeTransaction? transaction = null
    ) => new(connection) { Transaction = transaction };

    private static void AssertSetConfig(
        ExecutedCommand executed,
        string expectedTenant,
        string expectedUnscoped
    )
    {
        Assert.StartsWith(SetConfigPrefix, executed.CommandText, StringComparison.Ordinal);
        Assert.Equal(
            [("t", expectedTenant), ("u", expectedUnscoped)],
            executed.Parameters.Select(x => (x.Name, x.Value))
        );
    }

    #region Command Tests

    [Fact]
    public void NonQueryExecuting_UnsetScope_SendsEmptyTenantAndOff()
    {
        var (interceptor, _) = CreateInterceptor();
        var connection = new FakeConnection();

        interceptor.NonQueryExecuting(
            CreateCommand(connection),
            null!,
            default(InterceptionResult<int>)
        );

        AssertSetConfig(Assert.Single(connection.Executed), "", "off");
    }

    [Fact]
    public void NonQueryExecuting_UnscopedScope_SendsEmptyTenantAndOn()
    {
        var (interceptor, accessor) = CreateInterceptor();
        accessor.IsUnscoped = true;
        var connection = new FakeConnection();

        interceptor.NonQueryExecuting(
            CreateCommand(connection),
            null!,
            default(InterceptionResult<int>)
        );

        AssertSetConfig(Assert.Single(connection.Executed), "", "on");
    }

    [Fact]
    public void NonQueryExecuting_TenantScope_SendsTenantAndOff()
    {
        var (interceptor, accessor) = CreateInterceptor();
        accessor.TenantId = "tenant-a";
        var connection = new FakeConnection();

        interceptor.NonQueryExecuting(
            CreateCommand(connection),
            null!,
            default(InterceptionResult<int>)
        );

        AssertSetConfig(Assert.Single(connection.Executed), "tenant-a", "off");
    }

    [Fact]
    public void NonQueryExecuting_MixedCaseTenantScope_SendsLowerInvariantTenantAndOff()
    {
        var (interceptor, accessor) = CreateInterceptor();
        accessor.TenantId = "Tenant-A";
        var connection = new FakeConnection();

        interceptor.NonQueryExecuting(
            CreateCommand(connection),
            null!,
            default(InterceptionResult<int>)
        );

        AssertSetConfig(Assert.Single(connection.Executed), "tenant-a", "off");
    }

    [Fact]
    public void NonQueryExecuting_AnyScope_ReturnsTheResultUnchanged()
    {
        var (interceptor, _) = CreateInterceptor();
        var connection = new FakeConnection();

        var result = interceptor.NonQueryExecuting(
            CreateCommand(connection),
            null!,
            InterceptionResult<int>.SuppressWithResult(5)
        );

        Assert.True(result.HasResult);
        Assert.Equal(5, result.Result);
    }

    [Fact]
    public void NonQueryExecuting_InTransaction_RunsSetConfigInThatTransaction()
    {
        var (interceptor, _) = CreateInterceptor();
        var connection = new FakeConnection();
        var transaction = new FakeTransaction(connection);

        interceptor.NonQueryExecuting(
            CreateCommand(connection, transaction),
            null!,
            default(InterceptionResult<int>)
        );

        Assert.Same(transaction, Assert.Single(connection.Executed).Transaction);
    }

    [Fact]
    public void ReaderExecuting_UnchangedState_SendsOnce()
    {
        var (interceptor, accessor) = CreateInterceptor();
        accessor.TenantId = "tenant-a";
        var connection = new FakeConnection();

        interceptor.ReaderExecuting(
            CreateCommand(connection),
            null!,
            default(InterceptionResult<DbDataReader>)
        );
        interceptor.ScalarExecuting(
            CreateCommand(connection),
            null!,
            default(InterceptionResult<object>)
        );
        interceptor.NonQueryExecuting(
            CreateCommand(connection),
            null!,
            default(InterceptionResult<int>)
        );

        AssertSetConfig(Assert.Single(connection.Executed), "tenant-a", "off");
    }

    [Fact]
    public void ReaderExecuting_ChangedState_SendsAgain()
    {
        var (interceptor, accessor) = CreateInterceptor();
        accessor.TenantId = "tenant-a";
        var connection = new FakeConnection();
        interceptor.ReaderExecuting(
            CreateCommand(connection),
            null!,
            default(InterceptionResult<DbDataReader>)
        );

        accessor.TenantId = "tenant-b";
        interceptor.ReaderExecuting(
            CreateCommand(connection),
            null!,
            default(InterceptionResult<DbDataReader>)
        );
        accessor.TenantId = null;
        accessor.IsUnscoped = true;
        interceptor.ReaderExecuting(
            CreateCommand(connection),
            null!,
            default(InterceptionResult<DbDataReader>)
        );

        Assert.Equal(3, connection.Executed.Count);
        AssertSetConfig(connection.Executed[1], "tenant-b", "off");
        AssertSetConfig(connection.Executed[2], "", "on");
    }

    [Fact]
    public void ReaderExecuting_TwoConnections_TracksEachConnectionSeparately()
    {
        var (interceptor, _) = CreateInterceptor();
        var first = new FakeConnection();
        var second = new FakeConnection();

        interceptor.ReaderExecuting(
            CreateCommand(first),
            null!,
            default(InterceptionResult<DbDataReader>)
        );
        interceptor.ReaderExecuting(
            CreateCommand(second),
            null!,
            default(InterceptionResult<DbDataReader>)
        );
        interceptor.ReaderExecuting(
            CreateCommand(first),
            null!,
            default(InterceptionResult<DbDataReader>)
        );

        Assert.Single(first.Executed);
        Assert.Single(second.Executed);
    }

    [Fact]
    public void ReaderExecuting_CommandWithoutConnection_SendsNothing()
    {
        var (interceptor, _) = CreateInterceptor();
        var command = new FakeCommand(null);

        var result = interceptor.ReaderExecuting(
            command,
            null!,
            default(InterceptionResult<DbDataReader>)
        );

        Assert.False(result.HasResult);
    }

    [Fact]
    public async Task ReaderExecutingAsync_UnsetScope_SendsEmptyTenantAndOff()
    {
        var (interceptor, _) = CreateInterceptor();
        var connection = new FakeConnection();

        await interceptor.ReaderExecutingAsync(
            CreateCommand(connection),
            null!,
            default(InterceptionResult<DbDataReader>),
            CancellationToken.None
        );

        AssertSetConfig(Assert.Single(connection.Executed), "", "off");
    }

    [Fact]
    public async Task NonQueryExecutingAsync_TenantScope_SendsTenantOnceAndReturnsTheResult()
    {
        var (interceptor, accessor) = CreateInterceptor();
        accessor.TenantId = "tenant-a";
        var connection = new FakeConnection();

        var first = await interceptor.NonQueryExecutingAsync(
            CreateCommand(connection),
            null!,
            InterceptionResult<int>.SuppressWithResult(7),
            CancellationToken.None
        );
        await interceptor.NonQueryExecutingAsync(
            CreateCommand(connection),
            null!,
            default(InterceptionResult<int>),
            CancellationToken.None
        );

        Assert.Equal(7, first.Result);
        AssertSetConfig(Assert.Single(connection.Executed), "tenant-a", "off");
    }

    [Fact]
    public async Task ScalarExecutingAsync_UnscopedScope_SendsEmptyTenantAndOn()
    {
        var (interceptor, accessor) = CreateInterceptor();
        accessor.IsUnscoped = true;
        var connection = new FakeConnection();

        await interceptor.ScalarExecutingAsync(
            CreateCommand(connection),
            null!,
            default(InterceptionResult<object>),
            CancellationToken.None
        );

        AssertSetConfig(Assert.Single(connection.Executed), "", "on");
    }

    #endregion

    #region Connection Tests

    [Fact]
    public void ConnectionOpened_AfterStateWasApplied_SendsAgain()
    {
        var (interceptor, accessor) = CreateInterceptor();
        accessor.TenantId = "tenant-a";
        var connection = new FakeConnection();
        interceptor.ReaderExecuting(
            CreateCommand(connection),
            null!,
            default(InterceptionResult<DbDataReader>)
        );

        interceptor.ConnectionOpened(connection, null!);
        interceptor.ReaderExecuting(
            CreateCommand(connection),
            null!,
            default(InterceptionResult<DbDataReader>)
        );

        Assert.Equal(2, connection.Executed.Count);
        AssertSetConfig(connection.Executed[1], "tenant-a", "off");
    }

    [Fact]
    public async Task ConnectionOpenedAsync_AfterStateWasApplied_SendsAgain()
    {
        var (interceptor, accessor) = CreateInterceptor();
        accessor.TenantId = "tenant-a";
        var connection = new FakeConnection();
        interceptor.ReaderExecuting(
            CreateCommand(connection),
            null!,
            default(InterceptionResult<DbDataReader>)
        );

        await interceptor.ConnectionOpenedAsync(connection, null!, CancellationToken.None);
        interceptor.ReaderExecuting(
            CreateCommand(connection),
            null!,
            default(InterceptionResult<DbDataReader>)
        );

        Assert.Equal(2, connection.Executed.Count);
    }

    #endregion

    #region Transaction Tests

    [Fact]
    public void TransactionRolledBack_StateAppliedInIt_SendsAgain()
    {
        var (interceptor, accessor) = CreateInterceptor();
        accessor.TenantId = "tenant-a";
        var connection = new FakeConnection();
        var transaction = new FakeTransaction(connection);
        interceptor.ReaderExecuting(
            CreateCommand(connection, transaction),
            null!,
            default(InterceptionResult<DbDataReader>)
        );

        interceptor.TransactionRolledBack(transaction, null!);
        interceptor.ReaderExecuting(
            CreateCommand(connection),
            null!,
            default(InterceptionResult<DbDataReader>)
        );

        Assert.Equal(2, connection.Executed.Count);
        AssertSetConfig(connection.Executed[1], "tenant-a", "off");
    }

    [Fact]
    public async Task TransactionRolledBackAsync_StateAppliedInIt_SendsAgain()
    {
        var (interceptor, accessor) = CreateInterceptor();
        accessor.TenantId = "tenant-a";
        var connection = new FakeConnection();
        var transaction = new FakeTransaction(connection);
        interceptor.ReaderExecuting(
            CreateCommand(connection, transaction),
            null!,
            default(InterceptionResult<DbDataReader>)
        );

        await interceptor.TransactionRolledBackAsync(transaction, null!, CancellationToken.None);
        interceptor.ReaderExecuting(
            CreateCommand(connection),
            null!,
            default(InterceptionResult<DbDataReader>)
        );

        Assert.Equal(2, connection.Executed.Count);
    }

    [Fact]
    public async Task RolledBackToSavepoint_StateAppliedInIt_SendsAgain()
    {
        var (interceptor, accessor) = CreateInterceptor();
        accessor.TenantId = "tenant-a";
        var connection = new FakeConnection();
        var transaction = new FakeTransaction(connection);
        interceptor.ReaderExecuting(
            CreateCommand(connection, transaction),
            null!,
            default(InterceptionResult<DbDataReader>)
        );

        interceptor.RolledBackToSavepoint(transaction, null!);
        interceptor.ReaderExecuting(
            CreateCommand(connection, transaction),
            null!,
            default(InterceptionResult<DbDataReader>)
        );
        await interceptor.RolledBackToSavepointAsync(transaction, null!, CancellationToken.None);
        interceptor.ReaderExecuting(
            CreateCommand(connection, transaction),
            null!,
            default(InterceptionResult<DbDataReader>)
        );

        Assert.Equal(3, connection.Executed.Count);
    }

    [Fact]
    public async Task TransactionFailed_StateAppliedInIt_SendsAgain()
    {
        var (interceptor, accessor) = CreateInterceptor();
        accessor.TenantId = "tenant-a";
        var connection = new FakeConnection();
        var transaction = new FakeTransaction(connection);
        interceptor.ReaderExecuting(
            CreateCommand(connection, transaction),
            null!,
            default(InterceptionResult<DbDataReader>)
        );

        interceptor.TransactionFailed(transaction, null!);
        interceptor.ReaderExecuting(
            CreateCommand(connection, transaction),
            null!,
            default(InterceptionResult<DbDataReader>)
        );
        await interceptor.TransactionFailedAsync(transaction, null!, CancellationToken.None);
        interceptor.ReaderExecuting(
            CreateCommand(connection, transaction),
            null!,
            default(InterceptionResult<DbDataReader>)
        );

        Assert.Equal(3, connection.Executed.Count);
    }

    [Fact]
    public void TransactionRolledBack_StateAppliedOutsideIt_SendsOnce()
    {
        var (interceptor, accessor) = CreateInterceptor();
        accessor.TenantId = "tenant-a";
        var connection = new FakeConnection();
        var transaction = new FakeTransaction(connection);
        interceptor.ReaderExecuting(
            CreateCommand(connection),
            null!,
            default(InterceptionResult<DbDataReader>)
        );

        interceptor.TransactionRolledBack(transaction, null!);
        interceptor.ReaderExecuting(
            CreateCommand(connection),
            null!,
            default(InterceptionResult<DbDataReader>)
        );

        AssertSetConfig(Assert.Single(connection.Executed), "tenant-a", "off");
    }

    [Fact]
    public void ReaderExecuting_AfterTransactionDisposedWithoutCommit_SendsSetConfigAgain()
    {
        var (interceptor, accessor) = CreateInterceptor();
        accessor.TenantId = "tenant-a";
        var connection = new FakeConnection();
        var transaction = new FakeTransaction(connection);
        interceptor.ReaderExecuting(
            CreateCommand(connection, transaction),
            null!,
            default(InterceptionResult<DbDataReader>)
        );

        interceptor.ReaderExecuting(
            CreateCommand(connection),
            null!,
            default(InterceptionResult<DbDataReader>)
        );

        Assert.Equal(2, connection.Executed.Count);
        AssertSetConfig(connection.Executed[0], "tenant-a", "off");
        AssertSetConfig(connection.Executed[1], "tenant-a", "off");
        Assert.Same(transaction, connection.Executed[0].Transaction);
        Assert.Null(connection.Executed[1].Transaction);
    }

    [Fact]
    public void ReaderExecuting_AfterTransactionCommitted_DoesNotSendSetConfigAgain()
    {
        var (interceptor, accessor) = CreateInterceptor();
        accessor.TenantId = "tenant-a";
        var connection = new FakeConnection();
        var transaction = new FakeTransaction(connection);
        interceptor.ReaderExecuting(
            CreateCommand(connection, transaction),
            null!,
            default(InterceptionResult<DbDataReader>)
        );

        interceptor.TransactionCommitted(transaction, null!);
        interceptor.ReaderExecuting(
            CreateCommand(connection),
            null!,
            default(InterceptionResult<DbDataReader>)
        );

        AssertSetConfig(Assert.Single(connection.Executed), "tenant-a", "off");
    }

    [Fact]
    public async Task ReaderExecuting_AfterTransactionCommittedAsync_DoesNotSendSetConfigAgain()
    {
        var (interceptor, accessor) = CreateInterceptor();
        accessor.TenantId = "tenant-a";
        var connection = new FakeConnection();
        var transaction = new FakeTransaction(connection);
        interceptor.ReaderExecuting(
            CreateCommand(connection, transaction),
            null!,
            default(InterceptionResult<DbDataReader>)
        );

        await interceptor.TransactionCommittedAsync(transaction, null!, CancellationToken.None);
        interceptor.ReaderExecuting(
            CreateCommand(connection),
            null!,
            default(InterceptionResult<DbDataReader>)
        );

        AssertSetConfig(Assert.Single(connection.Executed), "tenant-a", "off");
    }

    #endregion

    #region UseOpenIdNpgsql Tests

    [Fact]
    public void UseOpenIdNpgsql_WithAccessor_AddsTheTenantSessionInterceptor()
    {
        var builder = new DbContextOptionsBuilder();

        builder.UseOpenIdNpgsql("Host=localhost;Database=openiddb", new FakeAccessor());

        var interceptors = builder.Options.FindExtension<CoreOptionsExtension>()?.Interceptors;
        Assert.NotNull(interceptors);
        Assert.Single(interceptors.OfType<TenantSessionInterceptor>());
    }

    #endregion

    private sealed class FakeAccessor : IAmbientTenantAccessor
    {
        public bool IsScoped => TenantId is not null;

        public string? TenantId { get; set; }

        public bool IsUnscoped { get; set; }

        public string? UnscopedReason => IsUnscoped ? "test" : null;

        public IDisposable BeginScope(string tenantId) => throw new NotSupportedException();

        public IDisposable BeginUnscoped(string reason) => throw new NotSupportedException();
    }

    private sealed record ExecutedCommand(
        string CommandText,
        IReadOnlyList<(string Name, object? Value)> Parameters,
        DbTransaction? Transaction
    );

    private sealed class FakeConnection : DbConnection
    {
        public List<ExecutedCommand> Executed { get; } = [];

        [AllowNull]
        public override string ConnectionString { get; set; } = "";

        public override string Database => "fake";

        public override string DataSource => "fake";

        public override string ServerVersion => "0";

        public override ConnectionState State => ConnectionState.Open;

        public override void ChangeDatabase(string databaseName) =>
            throw new NotSupportedException();

        public override void Close() { }

        public override void Open() { }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            new FakeTransaction(this);

        protected override DbCommand CreateDbCommand() => new FakeCommand(this);
    }

    private sealed class FakeTransaction(FakeConnection connection) : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        protected override DbConnection DbConnection => connection;

        public override void Commit() { }

        public override void Rollback() { }
    }

    private sealed class FakeCommand(FakeConnection? connection) : DbCommand
    {
        private FakeParameterCollection ParameterCollection { get; } = new();

        private DbTransaction? CurrentTransaction { get; set; }

        [AllowNull]
        public override string CommandText { get; set; } = "";

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; }

        public override bool DesignTimeVisible { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection? DbConnection
        {
            get => connection;
            set => throw new NotSupportedException();
        }

        protected override DbParameterCollection DbParameterCollection => ParameterCollection;

        protected override DbTransaction? DbTransaction
        {
            get => CurrentTransaction;
            set => CurrentTransaction = value;
        }

        public override void Cancel() { }

        public override int ExecuteNonQuery()
        {
            var owner = connection ?? throw new InvalidOperationException("No connection.");
            owner.Executed.Add(
                new ExecutedCommand(
                    CommandText,
                    ParameterCollection.Items.Select(x => (x.ParameterName, x.Value)).ToList(),
                    CurrentTransaction
                )
            );
            return -1;
        }

        public override object? ExecuteScalar() => throw new NotSupportedException();

        public override void Prepare() { }

        protected override DbParameter CreateDbParameter() => new FakeParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
            throw new NotSupportedException();
    }

    private sealed class FakeParameter : DbParameter
    {
        public override DbType DbType { get; set; }

        public override ParameterDirection Direction { get; set; }

        public override bool IsNullable { get; set; }

        [AllowNull]
        public override string ParameterName { get; set; } = "";

        public override int Size { get; set; }

        [AllowNull]
        public override string SourceColumn { get; set; } = "";

        public override bool SourceColumnNullMapping { get; set; }

        public override object? Value { get; set; }

        public override void ResetDbType() { }
    }

    private sealed class FakeParameterCollection : DbParameterCollection
    {
        public List<DbParameter> Items { get; } = [];

        public override int Count => Items.Count;

        public override object SyncRoot => ((ICollection)Items).SyncRoot;

        public override int Add(object value)
        {
            Items.Add((DbParameter)value);
            return Items.Count - 1;
        }

        public override void AddRange(Array values)
        {
            foreach (var value in values)
                Add(value);
        }

        public override void Clear() => Items.Clear();

        public override bool Contains(object value) => Items.Contains((DbParameter)value);

        public override bool Contains(string value) => IndexOf(value) >= 0;

        public override void CopyTo(Array array, int index) =>
            ((ICollection)Items).CopyTo(array, index);

        public override IEnumerator GetEnumerator() => Items.GetEnumerator();

        public override int IndexOf(object value) => Items.IndexOf((DbParameter)value);

        public override int IndexOf(string parameterName) =>
            Items.FindIndex(x => x.ParameterName == parameterName);

        public override void Insert(int index, object value) =>
            Items.Insert(index, (DbParameter)value);

        public override void Remove(object value) => Items.Remove((DbParameter)value);

        public override void RemoveAt(int index) => Items.RemoveAt(index);

        public override void RemoveAt(string parameterName) => RemoveAt(IndexOf(parameterName));

        protected override DbParameter GetParameter(int index) => Items[index];

        protected override DbParameter GetParameter(string parameterName) =>
            Items[IndexOf(parameterName)];

        protected override void SetParameter(int index, DbParameter value) => Items[index] = value;

        protected override void SetParameter(string parameterName, DbParameter value) =>
            Items[IndexOf(parameterName)] = value;
    }
}
