// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.Registration;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.GlobalDirectory;

public sealed class DefaultRegistrationTests : BaseTests
{
    private static IServiceBuilder<OpenIdCoreLibrary> CreateBuilder(
        IServiceCollection serviceCollection
    ) => new ServiceBuilder<OpenIdCoreLibrary>(serviceCollection);

    #region AddDirectoryMirrorServices Tests

    [Fact]
    public void AddDirectoryMirrorServices_Always_RegistersTheDefaultTeamApplierAsASingleton()
    {
        var serviceCollection = new ServiceCollection();

        CreateBuilder(serviceCollection).AddDirectoryMirrorServices();

        var descriptor = Assert.Single(
            serviceCollection,
            x => x.ServiceType == typeof(ITeamDocumentApplier)
        );
        Assert.Equal(typeof(DefaultTeamDocumentApplier), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddDirectoryMirrorServices_Always_RegistersTheDefaultPlatformApplierAsASingleton()
    {
        var serviceCollection = new ServiceCollection();

        CreateBuilder(serviceCollection).AddDirectoryMirrorServices();

        var descriptor = Assert.Single(
            serviceCollection,
            x => x.ServiceType == typeof(IPlatformDocumentApplier)
        );
        Assert.Equal(typeof(DefaultPlatformDocumentApplier), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddDirectoryMirrorServices_Always_RegistersTheDefaultFeedCheckpointAsASingleton()
    {
        var serviceCollection = new ServiceCollection();

        CreateBuilder(serviceCollection).AddDirectoryMirrorServices();

        var descriptor = Assert.Single(
            serviceCollection,
            x => x.ServiceType == typeof(IDirectoryFeedCheckpoint)
        );
        Assert.Equal(typeof(DefaultDirectoryFeedCheckpoint), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddDirectoryMirrorServices_Always_RegistersTheDefaultPostCommitCommandSenderAsASingleton()
    {
        var serviceCollection = new ServiceCollection();

        CreateBuilder(serviceCollection).AddDirectoryMirrorServices();

        var descriptor = Assert.Single(
            serviceCollection,
            x => x.ServiceType == typeof(IPostCommitCommandSender)
        );
        Assert.Equal(typeof(DefaultPostCommitCommandSender), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddDirectoryMirrorServices_Always_RegistersTheDefaultTeamRootKeyMirrorAsASingleton()
    {
        var serviceCollection = new ServiceCollection();

        CreateBuilder(serviceCollection).AddDirectoryMirrorServices();

        var descriptor = Assert.Single(
            serviceCollection,
            x => x.ServiceType == typeof(ITeamRootKeyMirror)
        );
        Assert.Equal(typeof(DefaultTeamRootKeyMirror), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddDirectoryMirrorServices_WhenAPostCommitCommandSenderIsRegistered_KeepsIt()
    {
        var serviceCollection = new ServiceCollection();
        var postCommitCommandSender = CreateStrictMock<IPostCommitCommandSender>().Object;
        serviceCollection.AddSingleton(postCommitCommandSender);

        CreateBuilder(serviceCollection).AddDirectoryMirrorServices();

        var descriptor = Assert.Single(
            serviceCollection,
            x => x.ServiceType == typeof(IPostCommitCommandSender)
        );
        Assert.Same(postCommitCommandSender, descriptor.ImplementationInstance);
    }

    [Fact]
    public void AddDirectoryMirrorServices_WhenATeamRootKeyMirrorIsRegistered_KeepsIt()
    {
        var serviceCollection = new ServiceCollection();
        var teamRootKeyMirror = CreateStrictMock<ITeamRootKeyMirror>().Object;
        serviceCollection.AddSingleton(teamRootKeyMirror);

        CreateBuilder(serviceCollection).AddDirectoryMirrorServices();

        var descriptor = Assert.Single(
            serviceCollection,
            x => x.ServiceType == typeof(ITeamRootKeyMirror)
        );
        Assert.Same(teamRootKeyMirror, descriptor.ImplementationInstance);
    }

    [Fact]
    public void AddDirectoryMirrorServices_WhenATeamApplierIsRegistered_KeepsIt()
    {
        var serviceCollection = new ServiceCollection();
        var teamDocumentApplier = CreateStrictMock<ITeamDocumentApplier>().Object;
        serviceCollection.AddSingleton(teamDocumentApplier);

        CreateBuilder(serviceCollection).AddDirectoryMirrorServices();

        var descriptor = Assert.Single(
            serviceCollection,
            x => x.ServiceType == typeof(ITeamDocumentApplier)
        );
        Assert.Same(teamDocumentApplier, descriptor.ImplementationInstance);
    }

    [Fact]
    public void AddDirectoryMirrorServices_WhenAPlatformApplierIsRegistered_KeepsIt()
    {
        var serviceCollection = new ServiceCollection();
        var platformDocumentApplier = CreateStrictMock<IPlatformDocumentApplier>().Object;
        serviceCollection.AddSingleton(platformDocumentApplier);

        CreateBuilder(serviceCollection).AddDirectoryMirrorServices();

        var descriptor = Assert.Single(
            serviceCollection,
            x => x.ServiceType == typeof(IPlatformDocumentApplier)
        );
        Assert.Same(platformDocumentApplier, descriptor.ImplementationInstance);
    }

    [Fact]
    public void AddDirectoryMirrorServices_WhenAFeedCheckpointIsRegistered_KeepsIt()
    {
        var serviceCollection = new ServiceCollection();
        var feedCheckpoint = CreateStrictMock<IDirectoryFeedCheckpoint>().Object;
        serviceCollection.AddSingleton(feedCheckpoint);

        CreateBuilder(serviceCollection).AddDirectoryMirrorServices();

        var descriptor = Assert.Single(
            serviceCollection,
            x => x.ServiceType == typeof(IDirectoryFeedCheckpoint)
        );
        Assert.Same(feedCheckpoint, descriptor.ImplementationInstance);
    }

    [Fact]
    public void AddDirectoryMirrorServices_Always_ReturnsTheBuilder()
    {
        var serviceCollection = new ServiceCollection();
        var builder = CreateBuilder(serviceCollection);

        var result = builder.AddDirectoryMirrorServices();

        Assert.Same(builder, result);
    }

    #endregion
}
