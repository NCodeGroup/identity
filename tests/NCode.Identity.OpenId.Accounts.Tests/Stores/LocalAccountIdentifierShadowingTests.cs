// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Accounts.Stores;

public sealed class LocalAccountIdentifierShadowingTests
{
    private sealed class FakeStore : ILocalAccountStore
    {
        public Dictionary<string, PersistedLocalAccount> ByUserName { get; } = [];
        public Dictionary<string, PersistedLocalAccount> ByEmail { get; } = [];

        public TStore GetStore<TStore>()
            where TStore : IStore => throw new NotSupportedException();

        public object? GetService(Type serviceType) => throw new NotSupportedException();

        public ValueTask<PersistedLocalAccount?> GetByIdOrDefaultAsync(
            string localAccountId,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public ValueTask<PersistedLocalAccount?> GetByUserNameOrDefaultAsync(
            string connectionId,
            string userName,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(ByUserName.GetValueOrDefault(userName));

        public ValueTask<PersistedLocalAccount?> GetByEmailOrDefaultAsync(
            string connectionId,
            string email,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(ByEmail.GetValueOrDefault(email));

        public ValueTask<PagedResult<PersistedLocalAccount>> GetPageAsync(
            string connectionId,
            PageRequest page,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public ValueTask AddAsync(
            PersistedLocalAccount account,
            ReadOnlyMemory<byte>? password,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public ValueTask UpdateAsync(
            PersistedLocalAccount account,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public ValueTask RemoveAsync(string localAccountId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask ReplaceClaimsAsync(
            string localAccountId,
            IReadOnlyList<PersistedLocalAccountClaim> claims,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public ValueTask SetPasswordAsync(
            string localAccountId,
            ReadOnlyMemory<byte> password,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public ValueTask<LocalAccountVerificationResult> VerifyCredentialAsync(
            LocalAccountSignInIdentifier identifier,
            ReadOnlyMemory<byte> password,
            Func<string, CancellationToken, ValueTask<bool>> isRefusedAsync,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();
    }

    private static PersistedLocalAccount CreateAccount(string localAccountId) =>
        new()
        {
            TenantId = "tenant",
            ConnectionId = "connection-id",
            LocalAccountId = localAccountId,
            UserName = null,
            Email = null,
            DisplayName = localAccountId,
            EmailVerified = false,
            IsEnabled = true,
            Claims = [],
            ConcurrencyToken = string.Empty,
        };

    #region FindAsync Tests

    [Fact]
    public async Task FindAsync_WhenNoAccountMatches_ReportsNothing()
    {
        var store = new FakeStore();
        var proposal = new LocalAccountIdentifierProposal
        {
            UserName = "ada",
            Email = "ada@example.test",
        };

        var result = await LocalAccountIdentifierShadowing.FindAsync(
            store,
            "connection-id",
            proposal,
            CancellationToken.None
        );

        Assert.False(result.UserNameIsAnEmail);
        Assert.False(result.EmailIsAUserName);
    }

    [Fact]
    public async Task FindAsync_WhenTheUserNameIsAnotherAccountsEmail_ReportsIt()
    {
        var store = new FakeStore();
        store.ByEmail["ada"] = CreateAccount("other");
        var proposal = new LocalAccountIdentifierProposal { UserName = "ada" };

        var result = await LocalAccountIdentifierShadowing.FindAsync(
            store,
            "connection-id",
            proposal,
            CancellationToken.None
        );

        Assert.True(result.UserNameIsAnEmail);
        Assert.False(result.EmailIsAUserName);
    }

    [Fact]
    public async Task FindAsync_WhenTheEmailIsAnotherAccountsUserName_ReportsIt()
    {
        var store = new FakeStore();
        store.ByUserName["ada@example.test"] = CreateAccount("other");
        var proposal = new LocalAccountIdentifierProposal { Email = "ada@example.test" };

        var result = await LocalAccountIdentifierShadowing.FindAsync(
            store,
            "connection-id",
            proposal,
            CancellationToken.None
        );

        Assert.False(result.UserNameIsAnEmail);
        Assert.True(result.EmailIsAUserName);
    }

    [Fact]
    public async Task FindAsync_WhenTheMatchIsTheAccountBeingUpdated_ReportsNothing()
    {
        var store = new FakeStore();
        store.ByEmail["ada"] = CreateAccount("self");
        store.ByUserName["ada@example.test"] = CreateAccount("self");
        var proposal = new LocalAccountIdentifierProposal
        {
            UserName = "ada",
            Email = "ada@example.test",
            LocalAccountId = "self",
        };

        var result = await LocalAccountIdentifierShadowing.FindAsync(
            store,
            "connection-id",
            proposal,
            CancellationToken.None
        );

        Assert.False(result.UserNameIsAnEmail);
        Assert.False(result.EmailIsAUserName);
    }

    #endregion
}
