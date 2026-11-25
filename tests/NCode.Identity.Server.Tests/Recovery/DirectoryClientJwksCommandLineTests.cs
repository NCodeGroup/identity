// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NCode.Identity.OpenId.Client;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.Secrets;
using NCode.Identity.Secrets.Collections;
using NCode.Identity.Secrets.KeyMaterial;
using NCode.Identity.Secrets.Keys;
using NCode.Identity.Server.Recovery;
using Xunit;

namespace NCode.Identity.Server.Tests.Recovery;

public class DirectoryClientJwksCommandLineTests : BaseTests
{
    private const string KeyRingName = "directory-client";

    private static string[] PrivateParameters { get; } =
    ["d", "p", "q", "dp", "dq", "qi", "oth", "k"];

    private static DirectoryClientJwksCommandLine Parse(params string[] args)
    {
        var command = DirectoryClientJwksCommandLine.Parse(args);
        Assert.NotNull(command);
        return command;
    }

    private static ServiceProvider CreateServices(
        IClientKeyRingProvider? keyRingProvider,
        string? configuredKeyRing
    )
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddSecretsLibrary();
        if (keyRingProvider is not null)
            serviceCollection.AddSingleton(keyRingProvider);
        serviceCollection.AddSingleton(
            Options.Create(new DirectoryClientOptions { KeyRing = configuredKeyRing })
        );
        return serviceCollection.BuildServiceProvider();
    }

    private Mock<IClientKeyRingProvider> CreateKeyRingProvider(
        IServiceProvider services,
        params SecretKey[] keys
    )
    {
        var collection = services.GetRequiredService<ISecretKeyCollectionFactory>().Create(keys);
        var mock = CreateStrictMock<IClientKeyRingProvider>();
        mock.Setup(x => x.HasKeyRing(KeyRingName)).Returns(true).Verifiable();
        mock.Setup(x => x.GetKeyRing(KeyRingName)).Returns(collection).Verifiable();
        return mock;
    }

    #region Parse Tests

    [Theory]
    [InlineData]
    [InlineData("--urls", "http://localhost")]
    [InlineData("setup")]
    public void Parse_ArgsThatAreNotTheCommand_ReturnNull(params string[] args)
    {
        Assert.Null(DirectoryClientJwksCommandLine.Parse(args));
    }

    [Fact]
    public void Parse_TheCommandAlone_UsesTheConfiguredKeyRing()
    {
        var command = Parse("directory-client-jwks");

        Assert.Null(command.Error);
        Assert.Null(command.KeyRing);
    }

    [Fact]
    public void Parse_TheKeyRingOption_ReturnsTheKeyRing()
    {
        var command = Parse("directory-client-jwks", "--key-ring", KeyRingName);

        Assert.Null(command.Error);
        Assert.Equal(KeyRingName, command.KeyRing);
    }

    [Theory]
    [InlineData("directory-client-jwks", "--other")]
    [InlineData("directory-client-jwks", "--key-ring")]
    [InlineData("directory-client-jwks", "--key-ring", " ")]
    [InlineData("directory-client-jwks", "--key-ring", KeyRingName, "extra")]
    public void Parse_InvalidArguments_ReturnACommandWithAnError(params string[] args)
    {
        var command = Parse(args);

        Assert.False(string.IsNullOrWhiteSpace(command.Error));
        Assert.Null(command.KeyRing);
    }

    #endregion

    #region GetHostArguments Tests

    [Fact]
    public void GetHostArguments_TheCommand_ReturnsNoArguments()
    {
        Assert.Empty(
            DirectoryClientJwksCommandLine.GetHostArguments([
                "directory-client-jwks",
                "--key-ring",
                KeyRingName,
            ])
        );
    }

    [Fact]
    public void GetHostArguments_OtherArguments_ReturnsThem()
    {
        string[] args = ["--urls", "http://localhost"];

        Assert.Equal(args, DirectoryClientJwksCommandLine.GetHostArguments(args));
    }

    #endregion

    #region RunAsync Tests

    [Fact]
    public async Task RunAsync_InvalidArguments_ReturnsTwoAndWritesNothing()
    {
        using var services = CreateServices(null, KeyRingName);
        using var output = new StringWriter();

        var exitCode = await Parse("directory-client-jwks", "--other")
            .RunAsync(services, output, CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public async Task RunAsync_NoKeyRingNamed_ReturnsTwoAndWritesNothing()
    {
        using var services = CreateServices(null, configuredKeyRing: null);
        using var output = new StringWriter();

        var exitCode = await Parse("directory-client-jwks")
            .RunAsync(services, output, CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public async Task RunAsync_UnknownKeyRing_ReturnsTwoAndWritesNothing()
    {
        var keyRingProvider = CreateStrictMock<IClientKeyRingProvider>();
        keyRingProvider.Setup(x => x.HasKeyRing("missing")).Returns(false).Verifiable();
        using var services = CreateServices(keyRingProvider.Object, null);
        using var output = new StringWriter();

        var exitCode = await Parse("directory-client-jwks", "--key-ring", "missing")
            .RunAsync(services, output, CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public async Task RunAsync_TheConfiguredKeyRing_WritesItsPublicKeysAndNoPrivateMembers()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var rsa = RSA.Create(2048);
        using var bootstrap = CreateServices(null, null);
        var factory = bootstrap.GetRequiredService<ISecretKeyFactory>();
        SecretKey[] keys =
        {
            factory.CreateEcc(new KeyMetadata { KeyId = "ec-1" }, ec),
            factory.CreateRsa(new KeyMetadata { KeyId = "rsa-1" }, rsa),
            factory.CreateSymmetric(
                new KeyMetadata { KeyId = "shared-1" },
                RandomNumberGenerator.GetBytes(32)
            ),
        };
        var keyRingProvider = CreateKeyRingProvider(bootstrap, keys);
        using var services = CreateServices(keyRingProvider.Object, KeyRingName);
        using var output = new StringWriter();

        var exitCode = await Parse("directory-client-jwks")
            .RunAsync(services, output, CancellationToken.None);

        Assert.Equal(0, exitCode);
        var document = JsonNode.Parse(output.ToString());
        var set = Assert.IsType<JsonObject>(document);
        var published = set["keys"]!.AsArray();
        Assert.Equal(
            ["ec-1", "rsa-1"],
            published
                .Select(x => x!["kid"]!.GetValue<string>())
                .OrderBy(x => x, StringComparer.Ordinal)
        );
        Assert.All(
            published,
            key => Assert.DoesNotContain(key!.AsObject(), x => PrivateParameters.Contains(x.Key))
        );
    }

    [Fact]
    public async Task RunAsync_TheOptionNamesTheKeyRing_OverridesTheConfiguredOne()
    {
        using var bootstrap = CreateServices(null, null);
        var keyRingProvider = CreateKeyRingProvider(bootstrap);
        using var services = CreateServices(keyRingProvider.Object, "other");
        using var output = new StringWriter();

        var exitCode = await Parse("directory-client-jwks", "--key-ring", KeyRingName)
            .RunAsync(services, output, CancellationToken.None);

        Assert.Equal(0, exitCode);
        var set = Assert.IsType<JsonObject>(JsonNode.Parse(output.ToString()));
        Assert.Empty(set["keys"]!.AsArray());
    }

    [Fact]
    public async Task RunAsync_TheProviderFails_ReturnsOne()
    {
        var keyRingProvider = CreateStrictMock<IClientKeyRingProvider>();
        keyRingProvider
            .Setup(x => x.HasKeyRing(KeyRingName))
            .Throws(new InvalidOperationException("The key ring is unreadable."))
            .Verifiable();
        using var services = CreateServices(keyRingProvider.Object, KeyRingName);
        using var output = new StringWriter();

        var exitCode = await Parse("directory-client-jwks")
            .RunAsync(services, output, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, output.ToString());
    }

    #endregion
}
