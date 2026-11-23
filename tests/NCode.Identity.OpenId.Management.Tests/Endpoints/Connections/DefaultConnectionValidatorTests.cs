// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Microsoft.AspNetCore.Http;
using NCode.Identity.Json;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Management.Endpoints.Connections;

public sealed class DefaultConnectionValidatorTests : IDisposable
{
    private const string TenantId = "tenant-1";
    private const string StrategyName = "test";

    private MockRepository MockRepository { get; }
    private Mock<IConnectionStrategyCatalog> MockCatalog { get; }
    private Mock<IConnectionStrategy> MockStrategy { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<IConnectionStore> MockConnectionStore { get; }
    private DefaultConnectionValidator Validator { get; }

    public DefaultConnectionValidatorTests()
    {
        MockRepository = new MockRepository(MockBehavior.Strict);
        MockCatalog = MockRepository.Create<IConnectionStrategyCatalog>();
        MockStrategy = MockRepository.Create<IConnectionStrategy>();
        MockStoreManager = MockRepository.Create<IStoreManager>();
        MockConnectionStore = MockRepository.Create<IConnectionStore>();

        Validator = new DefaultConnectionValidator(MockCatalog.Object);
    }

    public void Dispose()
    {
        MockRepository.Verify();
    }

    #region Helpers

    private static PersistedConnection CreateConnection(
        string name = "corp",
        string strategy = StrategyName,
        string displayName = "Corporate",
        JsonElement? options = null
    ) =>
        new()
        {
            TenantId = TenantId,
            ConnectionId = "connection-1",
            Name = name,
            Strategy = strategy,
            DisplayName = displayName,
            IsSystem = false,
            IsEnabled = true,
            Options = options ?? JsonSerializer.SerializeToElement(new { store = "default" }),
            Settings = new PersistedConnectionSettings
            {
                TenantId = TenantId,
                ConnectionId = "connection-1",
                Value = JsonElements.EmptyObject,
            },
        };

    private void SetupStore(PersistedConnection? existing)
    {
        MockStoreManager
            .Setup(x => x.GetStore<IConnectionStore>())
            .Returns(MockConnectionStore.Object)
            .Verifiable();
        MockConnectionStore
            .Setup(x =>
                x.GetByNameOrDefaultAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(existing)
            .Verifiable();
    }

    private void SetupStrategy(string? optionsError, string? changeError = null)
    {
        var strategy = MockStrategy.Object;
        MockCatalog
            .Setup(x => x.TryGetStrategy(StrategyName, out strategy))
            .Returns(true)
            .Verifiable();
        MockStrategy
            .Setup(x => x.ValidateOptions(It.IsAny<JsonElement>()))
            .Returns(optionsError)
            .Verifiable();
        MockStrategy
            .Setup(x => x.ValidateOptionsChange(It.IsAny<JsonElement>(), It.IsAny<JsonElement>()))
            .Returns(changeError);
    }

    #endregion

    [Fact]
    public async Task ValidateCreateAsync_InvalidName_ReturnsBadRequest()
    {
        var result = await Validator.ValidateCreateAsync(
            CreateConnection(name: "-bad"),
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task ValidateCreateAsync_NameInUse_ReturnsConflict()
    {
        SetupStore(CreateConnection());

        var result = await Validator.ValidateCreateAsync(
            CreateConnection(),
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
    }

    [Fact]
    public async Task ValidateCreateAsync_UnregisteredStrategy_ReturnsBadRequest()
    {
        SetupStore(null);
        IConnectionStrategy? missing = null;
        MockCatalog
            .Setup(x => x.TryGetStrategy("unknown", out missing))
            .Returns(false)
            .Verifiable();

        var result = await Validator.ValidateCreateAsync(
            CreateConnection(strategy: "unknown"),
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task ValidateCreateAsync_InvalidOptions_ReturnsStrategyError()
    {
        SetupStore(null);
        SetupStrategy("The options are invalid.");

        var result = await Validator.ValidateCreateAsync(
            CreateConnection(options: JsonElements.EmptyObject),
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Equal("The options are invalid.", result.Detail);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\nname")]
    public async Task ValidateCreateAsync_InvalidDisplayName_ReturnsBadRequest(string displayName)
    {
        SetupStore(null);
        SetupStrategy(null);

        var result = await Validator.ValidateCreateAsync(
            CreateConnection(displayName: displayName),
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task ValidateCreateAsync_DisplayNameTooLong_ReturnsBadRequest()
    {
        SetupStore(null);
        SetupStrategy(null);

        var result = await Validator.ValidateCreateAsync(
            CreateConnection(displayName: new string('a', 301)),
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task ValidateCreateAsync_ValidConnection_ReturnsNull()
    {
        SetupStore(null);
        SetupStrategy(null);

        var result = await Validator.ValidateCreateAsync(
            CreateConnection(),
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateUpdateAsync_InvalidOptions_ReturnsStrategyError()
    {
        SetupStrategy("The options are invalid.");

        var result = await Validator.ValidateUpdateAsync(
            CreateConnection(options: JsonElements.EmptyObject),
            JsonElements.EmptyObject,
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(result);
        Assert.Equal("The options are invalid.", result.Detail);
    }

    [Fact]
    public async Task ValidateUpdateAsync_ChangedOptionsTheStrategyRefuses_ReturnsBadRequest()
    {
        SetupStrategy(null, "The store of a local connection cannot change.");

        var result = await Validator.ValidateUpdateAsync(
            CreateConnection(),
            JsonSerializer.SerializeToElement(new { store = "other" }),
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Equal("The store of a local connection cannot change.", result.Detail);
    }

    [Fact]
    public async Task ValidateUpdateAsync_ChangedDisplayName_ReturnsNull()
    {
        SetupStrategy(null);

        var result = await Validator.ValidateUpdateAsync(
            CreateConnection(displayName: "Corporate Sign-in"),
            JsonSerializer.SerializeToElement(new { store = "default" }),
            MockStoreManager.Object,
            CancellationToken.None
        );

        Assert.Null(result);
    }
}
