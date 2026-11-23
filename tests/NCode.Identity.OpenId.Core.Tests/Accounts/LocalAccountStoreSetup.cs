// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Moq;
using NCode.Identity.Json;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Settings;

namespace NCode.Identity.OpenId.Core.Tests.Accounts;

// Builds the connection and store catalog a local-account test runs against, and makes a strict store mock ask the gate
// about the account key, as a store does after its lookup.
internal static class LocalAccountStoreSetup
{
    public const string ConnectionId = "connection-1";

    public const string ConnectionName = "local";

    // Creates a resolved connection of the given strategy whose settings are the given settings.
    public static ResolvedConnection CreateConnection(
        string tenantId,
        IReadOnlySettingCollection settings,
        string strategy = ConnectionStrategyNames.Local,
        string connectionId = ConnectionId,
        string name = ConnectionName
    )
    {
        var options = new LocalConnectionOptions { Store = LocalAccountStoreNames.Default };
        return new ResolvedConnection
        {
            Connection = new PersistedConnection
            {
                TenantId = tenantId,
                ConnectionId = connectionId,
                Name = name,
                Strategy = strategy,
                DisplayName = name,
                IsEnabled = true,
                Options = JsonSerializer.SerializeToElement(options),
                Settings = new PersistedConnectionSettings
                {
                    TenantId = tenantId,
                    ConnectionId = connectionId,
                    Value = JsonElements.EmptyObject,
                },
            },
            Strategy = new Mock<IConnectionStrategy>().Object,
            Settings = settings,
        };
    }

    // Creates a catalog whose default store is the mocked store.
    public static LocalAccountStoreCatalog CreateCatalog(Mock<ILocalAccountStore> mockStore) =>
        new([
            new LocalAccountStoreRegistration
            {
                Name = LocalAccountStoreNames.Default,
                CreateStore = _ => mockStore.Object,
                CreateTransferStore = _ => new Mock<ILocalAccountTransferStore>().Object,
            },
        ]);

    // The identifier a connection whose local_account_identifier is email_or_user_name builds from a submitted value.
    public static LocalAccountSignInIdentifier EmailOrUserName(string submitted) =>
        new()
        {
            ConnectionId = ConnectionId,
            UserName = submitted,
            Email = submitted,
        };

    public static void SetupVerification(
        Mock<ILocalAccountStore> mockStore,
        LocalAccountSignInIdentifier identifier,
        string accountKey,
        PersistedLocalAccount? verified
    ) =>
        mockStore
            .Setup(x =>
                x.VerifyCredentialAsync(
                    It.Is<LocalAccountSignInIdentifier>(i => Matches(i, identifier)),
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    It.IsAny<Func<string, CancellationToken, ValueTask<bool>>>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(
                (
                    string _,
                    ReadOnlyMemory<byte> _,
                    Func<string, CancellationToken, ValueTask<bool>> isRefusedAsync,
                    CancellationToken cancellationToken
                ) => VerifyAsync(isRefusedAsync, accountKey, verified, cancellationToken)
            )
            .Verifiable();

    // Makes a strict store mock ask the gate and then verify the password whatever the gate answered, as a store that
    // ignores a refusal does.
    public static void SetupVerificationIgnoringARefusal(
        Mock<ILocalAccountStore> mockStore,
        LocalAccountSignInIdentifier identifier,
        string accountKey,
        PersistedLocalAccount verified
    ) =>
        mockStore
            .Setup(x =>
                x.VerifyCredentialAsync(
                    It.Is<LocalAccountSignInIdentifier>(i => Matches(i, identifier)),
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    It.IsAny<Func<string, CancellationToken, ValueTask<bool>>>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(
                (
                    string _,
                    ReadOnlyMemory<byte> _,
                    Func<string, CancellationToken, ValueTask<bool>> isRefusedAsync,
                    CancellationToken cancellationToken
                ) =>
                    VerifyIgnoringARefusalAsync(
                        isRefusedAsync,
                        accountKey,
                        verified,
                        cancellationToken
                    )
            )
            .Verifiable();

    // Makes a strict store mock verify the password without ever asking the gate, as a store that skips the limit does.
    public static void SetupVerificationWithoutTheGate(
        Mock<ILocalAccountStore> mockStore,
        LocalAccountSignInIdentifier identifier,
        string accountKey,
        PersistedLocalAccount verified
    ) =>
        mockStore
            .Setup(x =>
                x.VerifyCredentialAsync(
                    It.Is<LocalAccountSignInIdentifier>(i => Matches(i, identifier)),
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    It.IsAny<Func<string, CancellationToken, ValueTask<bool>>>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                new LocalAccountVerificationResult { AccountKey = accountKey, Account = verified }
            )
            .Verifiable();

    private static bool Matches(
        LocalAccountSignInIdentifier actual,
        LocalAccountSignInIdentifier expected
    ) =>
        actual.ConnectionId == expected.ConnectionId
        && actual.UserName == expected.UserName
        && actual.Email == expected.Email;

    private static async ValueTask<LocalAccountVerificationResult> VerifyAsync(
        Func<string, CancellationToken, ValueTask<bool>> isRefusedAsync,
        string accountKey,
        PersistedLocalAccount? verified,
        CancellationToken cancellationToken
    )
    {
        var isRefused = await isRefusedAsync(accountKey, cancellationToken);
        return new LocalAccountVerificationResult
        {
            AccountKey = accountKey,
            Account = isRefused ? null : verified,
            IsRefused = isRefused,
        };
    }

    private static async ValueTask<LocalAccountVerificationResult> VerifyIgnoringARefusalAsync(
        Func<string, CancellationToken, ValueTask<bool>> isRefusedAsync,
        string accountKey,
        PersistedLocalAccount verified,
        CancellationToken cancellationToken
    )
    {
        await isRefusedAsync(accountKey, cancellationToken);
        return new LocalAccountVerificationResult { AccountKey = accountKey, Account = verified };
    }
}
