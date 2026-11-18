// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NCode.Buffers;
using NCode.Identity.Jose;
using NCode.Identity.OpenId.Client.KeyRings;
using NCode.Identity.OpenId.Client.Options;
using NCode.Identity.Registration;
using NCode.Identity.Secrets;
using NCode.Identity.Secrets.Collections;
using NCode.Identity.Secrets.RemoteKeys;
using Xunit;

namespace NCode.Identity.OpenId.Client.Tests.KeyRings;

public sealed class DefaultRegistrationTests : BaseTests, IAsyncLifetime
{
    private const string SectionPath = "Test:KeyRings";

    private DirectoryInfo KeysDirectory { get; } = Directory.CreateTempSubdirectory("client-keys-");

    private FakeRemoteKeyProvider RemoteKeyProvider { get; } = new();

    private ServiceCollection ServiceCollection { get; } = new();

    private ServiceProvider? Provider { get; set; }

    public DefaultRegistrationTests()
    {
        ServiceCollection.AddLogging();
        ServiceCollection.AddSecretsLibrary().AddJoseLibrary();
        ServiceCollection.AddSingleton<IRemoteKeyProvider>(RemoteKeyProvider);
        ServiceCollection.AddSingleton<IRemoteKeyDescriptorSource>(RemoteKeyProvider);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (Provider is not null)
            await Provider.DisposeAsync();

        RemoteKeyProvider.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            KeysDirectory.Delete(true);

        base.Dispose(disposing);
    }

    private void WriteEcPem(string fileName)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(
            Path.Combine(KeysDirectory.FullName, fileName),
            key.ExportPkcs8PrivateKeyPem(),
            SecureEncoding.UTF8
        );
    }

    private IConfigurationSection CreateSection(params string[] ringNames)
    {
        var values = new Dictionary<string, string?>();
        foreach (var ringName in ringNames)
        {
            WriteEcPem($"{ringName}-key.pem");
            values[$"{SectionPath}:{ringName}:PemFiles:0"] = $"{ringName}-key.pem";
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build()
            .GetSection(SectionPath);
    }

    private ServiceProvider BuildProvider()
    {
        var mockEnvironment = CreateStrictMock<IHostEnvironment>();
        mockEnvironment.Setup(x => x.ContentRootPath).Returns(KeysDirectory.FullName).Verifiable();
        ServiceCollection.AddSingleton(mockEnvironment.Object);

        Provider = ServiceCollection.BuildServiceProvider();
        return Provider;
    }

    #region AddOpenIdClientKeyRings Tests

    [Fact]
    public async Task AddOpenIdClientKeyRings_WithTwoKeyRings_RegistersAKeyedProviderForEach()
    {
        ServiceCollection
            .NewBuilder<OpenIdClientLibrary>()
            .AddOpenIdClientKeyRings(CreateSection("alpha", "beta"));
        var provider = BuildProvider();

        var loader = provider.GetServices<IHostedService>().OfType<ClientKeyRingLoader>().Single();
        await loader.StartingAsync(CancellationToken.None);
        var alpha = provider.GetRequiredKeyedService<ISecretKeyCollectionProvider>("alpha");
        var beta = provider.GetRequiredKeyedService<ISecretKeyCollectionProvider>("beta");

        Assert.NotSame(alpha, beta);
        Assert.Equal("alpha-key", Assert.Single(alpha.Collection).KeyId);
        Assert.Equal("beta-key", Assert.Single(beta.Collection).KeyId);
    }

    [Fact]
    public void AddOpenIdClientKeyRings_WithAKeyRing_BindsTheSectionPath()
    {
        ServiceCollection
            .NewBuilder<OpenIdClientLibrary>()
            .AddOpenIdClientKeyRings(CreateSection("alpha"));
        var provider = BuildProvider();

        var options = provider.GetRequiredService<IOptions<ClientKeyRingsOptions>>().Value;

        Assert.Equal(SectionPath, options.SectionPath);
        Assert.Equal(["alpha"], options.KeyRings.Keys);
        Assert.Equal(["alpha-key.pem"], options.KeyRings["alpha"].PemFiles);
    }

    [Fact]
    public void AddOpenIdClientKeyRings_WhenResolvedBeforeTheHostStarts_ThrowsNamingTheKeyRing()
    {
        ServiceCollection
            .NewBuilder<OpenIdClientLibrary>()
            .AddOpenIdClientKeyRings(CreateSection("alpha"));
        var provider = BuildProvider();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            provider.GetRequiredKeyedService<ISecretKeyCollectionProvider>("alpha")
        );

        Assert.Contains("'alpha'", exception.Message);
        Assert.Contains($"'{SectionPath}'", exception.Message);
    }

    [Fact]
    public void AddOpenIdClientKeyRings_WithANameAlreadyRegistered_Throws()
    {
        var builder = ServiceCollection.NewBuilder<OpenIdClientLibrary>();
        builder.AddOpenIdClientKeyRings(CreateSection("alpha"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            builder.AddOpenIdClientKeyRings(CreateSection("alpha"))
        );

        Assert.Contains("'alpha'", exception.Message);
        Assert.Contains($"'{SectionPath}'", exception.Message);
    }

    [Fact]
    public void AddOpenIdClientKeyRings_WithANameRegisteredAsAKeyedProvider_Throws()
    {
        ServiceCollection.AddKeyedSingleton<ISecretKeyCollectionProvider>(
            "alpha",
            (_, _) => throw new NotSupportedException()
        );
        var builder = ServiceCollection.NewBuilder<OpenIdClientLibrary>();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            builder.AddOpenIdClientKeyRings(CreateSection("alpha"))
        );

        Assert.Contains("'alpha'", exception.Message);
    }

    #endregion
}
