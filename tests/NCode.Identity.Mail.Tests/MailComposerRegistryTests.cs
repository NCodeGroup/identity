// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace NCode.Identity.Mail.Tests;

public class MailComposerRegistryTests : BaseTests
{
    private sealed record FirstModel(string Value);

    private sealed record SecondModel(string Value);

    private IMailComposer CreateComposer(Type modelType)
    {
        var mockComposer = CreateStrictMock<IMailComposer>();
        mockComposer.Setup(x => x.ModelType).Returns(modelType).Verifiable();
        return mockComposer.Object;
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithTwoComposersOfOneModel_Throws()
    {
        var first = CreateComposer(typeof(FirstModel));
        var second = CreateComposer(typeof(FirstModel));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new MailComposerRegistry([first, second])
        );

        Assert.Contains(typeof(FirstModel).ToString(), exception.Message);
    }

    #endregion

    #region Get Tests

    [Fact]
    public void Get_WithoutAComposer_Throws()
    {
        var registry = new MailComposerRegistry([CreateComposer(typeof(FirstModel))]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Get(typeof(SecondModel))
        );

        Assert.Equal(
            $"No mail composer is registered for the model type '{typeof(SecondModel)}'.",
            exception.Message
        );
    }

    [Fact]
    public void Get_WithAComposer_ReturnsIt()
    {
        var first = CreateComposer(typeof(FirstModel));
        var second = CreateComposer(typeof(SecondModel));
        var registry = new MailComposerRegistry([first, second]);

        Assert.Same(second, registry.Get(typeof(SecondModel)));
        Assert.Same(first, registry.Get(typeof(FirstModel)));
    }

    #endregion
}
