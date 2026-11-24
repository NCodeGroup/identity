// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Moq;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Authentication.Passwords;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Settings;
using Xunit;

namespace NCode.Identity.Server.Tests;

public class SeededLocalAccountValidatorTests : BaseTests
{
    private const string TenantId = "default";

    private Mock<IEffectiveSettingsResolver> MockEffectiveSettingsResolver { get; }
    private Mock<IPasswordPolicy> MockPasswordPolicy { get; }
    private Mock<ILocalAccountStore> MockLocalAccountStore { get; }

    private SeededLocalAccountValidator Validator { get; }

    public SeededLocalAccountValidatorTests()
    {
        MockEffectiveSettingsResolver = CreateStrictMock<IEffectiveSettingsResolver>();
        MockPasswordPolicy = CreateStrictMock<IPasswordPolicy>();
        MockLocalAccountStore = CreateStrictMock<ILocalAccountStore>();

        Validator = new SeededLocalAccountValidator(
            MockEffectiveSettingsResolver.Object,
            MockPasswordPolicy.Object
        );
    }

    #region Scaffolding

    private static PersistedTenant CreateTenant() =>
        new PersistedTenantBuilder { TenantId = TenantId }.Build();

    private static PersistedLocalAccount CreateAccount() =>
        new()
        {
            TenantId = TenantId,
            ConnectionId = LocalConnectionTestData.ConnectionId,
            LocalAccountId = "account-1",
            UserName = "admin",
            DisplayName = "admin",
            Email = "admin@localhost",
            EmailVerified = true,
            IsEnabled = true,
            Claims = [],
            ConcurrencyToken = string.Empty,
        };

    private SeededLocalAccount CreateSeeded() =>
        new()
        {
            AccountKind = "developer administrator",
            Tenant = CreateTenant(),
            Connection = LocalConnectionTestData.CreateConnection(TenantId),
            Store = MockLocalAccountStore.Object,
            Account = CreateAccount(),
        };

    private IReadOnlySettingCollection SetupTenantResolution()
    {
        var platform = CreateLooseMock<ISettingCollection>().Object;
        var tenantSettings = CreateLooseMock<ISettingCollection>().Object;

        MockEffectiveSettingsResolver
            .Setup(x => x.ResolveTeamAsync("platform", It.IsAny<CancellationToken>()))
            .ReturnsAsync(platform)
            .Verifiable();
        MockEffectiveSettingsResolver
            .Setup(x => x.Resolve(platform, OpenIdSettingScopes.Tenant, It.IsAny<JsonElement>()))
            .Returns(tenantSettings)
            .Verifiable();

        var connectionSettings = CreateLooseMock<ISettingCollection>().Object;
        MockEffectiveSettingsResolver
            .Setup(x =>
                x.ResolveStored(
                    tenantSettings,
                    OpenIdSettingScopes.Connection,
                    LocalConnectionTestData.ConnectionId,
                    It.IsAny<JsonElement>()
                )
            )
            .Returns(connectionSettings)
            .Verifiable();

        return connectionSettings;
    }

    private void SetupLookups(
        PersistedLocalAccount? accountWithTheUserNameAsEmail = null,
        PersistedLocalAccount? accountWithTheEmailAsUserName = null
    )
    {
        MockLocalAccountStore
            .Setup(x =>
                x.GetByEmailOrDefaultAsync(
                    LocalConnectionTestData.ConnectionId,
                    "admin",
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(accountWithTheUserNameAsEmail)
            .Verifiable();
        MockLocalAccountStore
            .Setup(x =>
                x.GetByUserNameOrDefaultAsync(
                    LocalConnectionTestData.ConnectionId,
                    "admin@localhost",
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(accountWithTheEmailAsUserName);
    }

    private void SetupPolicy(
        IReadOnlySettingCollection connectionSettings,
        PasswordPolicyResult result
    ) =>
        MockPasswordPolicy
            .Setup(x =>
                x.ValidateAsync(
                    It.Is<PasswordPolicyContext>(c =>
                        c.Settings == connectionSettings
                        && c.UserName == "admin"
                        && c.Email == "admin@localhost"
                    ),
                    It.IsAny<ReadOnlyMemory<char>>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(result)
            .Verifiable();

    #endregion

    #region EnsureCanBeSeededAsync Tests

    [Fact]
    public async Task EnsureCanBeSeededAsync_WhenThePolicyAcceptsThePassword_Completes()
    {
        SetupLookups();
        var connectionSettings = SetupTenantResolution();
        SetupPolicy(connectionSettings, PasswordPolicyResult.Accepted);

        await Validator.EnsureCanBeSeededAsync(
            CreateSeeded(),
            "dev-admin-password",
            CancellationToken.None
        );
    }

    [Fact]
    public async Task EnsureCanBeSeededAsync_WhenThePolicyRefusesThePassword_ThrowsNamingTheAccount()
    {
        SetupLookups();
        var connectionSettings = SetupTenantResolution();
        SetupPolicy(
            connectionSettings,
            PasswordPolicyResult.Rejected(
                PasswordPolicyErrors.TooShort,
                "The password must have at least 15 characters."
            )
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Validator.EnsureCanBeSeededAsync(CreateSeeded(), "short", CancellationToken.None)
        );

        Assert.Contains("'admin'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("at least 15", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnsureCanBeSeededAsync_WhenTheUserNameIsAnotherAccountsEmail_Throws()
    {
        SetupLookups(accountWithTheUserNameAsEmail: CreateAccount());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Validator.EnsureCanBeSeededAsync(
                CreateSeeded(),
                "dev-admin-password",
                CancellationToken.None
            )
        );

        Assert.Contains(
            "another account's email address",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task EnsureCanBeSeededAsync_WhenTheEmailIsAnotherAccountsUserName_Throws()
    {
        SetupLookups(accountWithTheEmailAsUserName: CreateAccount());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Validator.EnsureCanBeSeededAsync(
                CreateSeeded(),
                "dev-admin-password",
                CancellationToken.None
            )
        );

        Assert.Contains("another account's user name", exception.Message, StringComparison.Ordinal);
    }

    #endregion
}
