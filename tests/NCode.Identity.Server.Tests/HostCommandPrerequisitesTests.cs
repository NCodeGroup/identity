// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Moq;
using NCode.Identity.Registration;
using Xunit;

namespace NCode.Identity.Server.Tests;

public class HostCommandPrerequisitesTests : BaseTests
{
    [Fact]
    public async Task PrepareAsync_WithTwoPrerequisites_RunsEachInRegistrationOrder()
    {
        var order = new List<string>();
        var first = CreateStrictMock<IStartupPrerequisite>();
        first
            .Setup(x => x.PrepareAsync(It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("first"))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);
        var second = CreateStrictMock<IStartupPrerequisite>();
        second
            .Setup(x => x.PrepareAsync(It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("second"))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(first.Object);
        serviceCollection.AddSingleton(second.Object);
        await using var services = serviceCollection.BuildServiceProvider();

        await HostCommandPrerequisites.PrepareAsync(services, CancellationToken.None);

        Assert.Equal(["first", "second"], order);
    }

    [Fact]
    public async Task PrepareAsync_WithNoPrerequisite_Completes()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();

        await HostCommandPrerequisites.PrepareAsync(services, CancellationToken.None);
    }
}
