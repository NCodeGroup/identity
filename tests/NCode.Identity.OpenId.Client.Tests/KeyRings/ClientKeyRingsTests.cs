// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Client.KeyRings;
using NCode.Identity.OpenId.Client.Options;
using NCode.Identity.Secrets.Collections;
using Xunit;

namespace NCode.Identity.OpenId.Client.Tests.KeyRings;

public sealed class ClientKeyRingsTests : BaseTests
{
    private static ClientKeyRings CreateKeyRings() =>
        new(
            Microsoft.Extensions.Options.Options.Create(
                new ClientKeyRingsOptions { SectionPath = "KeyRings" }
            )
        );

    #region Add Tests

    [Fact]
    public void Add_WithANameAlreadyLoaded_Throws()
    {
        var keyRings = CreateKeyRings();
        keyRings.Add("ring", CreateStrictMock<ISecretKeyCollectionProvider>().Object);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            keyRings.Add("ring", CreateStrictMock<ISecretKeyCollectionProvider>().Object)
        );

        Assert.Equal("The key ring 'ring' is loaded more than once.", exception.Message);
    }

    #endregion

    #region Get Tests

    [Fact]
    public void Get_WithALoadedKeyRing_ReturnsIt()
    {
        var keyRing = CreateStrictMock<ISecretKeyCollectionProvider>().Object;
        var keyRings = CreateKeyRings();
        keyRings.Add("ring", keyRing);

        var result = keyRings.Get("ring");

        Assert.Same(keyRing, result);
    }

    [Fact]
    public void Get_WithAKeyRingThatIsNotLoaded_Throws()
    {
        var keyRings = CreateKeyRings();

        var exception = Assert.Throws<InvalidOperationException>(() => keyRings.Get("missing"));

        Assert.Equal(
            "The key ring 'missing' is not loaded: the host loads the key rings of 'KeyRings' when it starts.",
            exception.Message
        );
    }

    #endregion

    #region DisposeAsync Tests

    [Fact]
    public async Task DisposeAsync_DisposesEveryLoadedKeyRing()
    {
        var first = CreateStrictMock<ISecretKeyCollectionProvider>();
        first.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();
        var second = CreateStrictMock<ISecretKeyCollectionProvider>();
        second.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();
        var keyRings = CreateKeyRings();
        keyRings.Add("first", first.Object);
        keyRings.Add("second", second.Object);

        await keyRings.DisposeAsync();
    }

    #endregion
}
