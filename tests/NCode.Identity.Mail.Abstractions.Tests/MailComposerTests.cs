// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Moq;
using Xunit;

namespace NCode.Identity.Mail.Abstractions.Tests;

public class MailComposerTests : BaseTests
{
    private Mock<MailComposer<string>> ComposerMock { get; }

    private IMailComposer Composer => ComposerMock.Object;

    public MailComposerTests()
    {
        ComposerMock = CreateStrictMock<MailComposer<string>>();
        ComposerMock.CallBase = true;
    }

    #region ModelType Tests

    [Fact]
    public void ModelType_Always_ReturnsTheTypeOfTheModel()
    {
        Assert.Equal(typeof(string), Composer.ModelType);
    }

    #endregion

    #region ComposeAsync Tests

    [Fact]
    public async Task ComposeAsync_WithAModelOfItsType_ComposesIt()
    {
        var message = new MailMessage { Subject = "Your code" };
        ComposerMock
            .Setup(x => x.ComposeAsync("model", CancellationToken.None))
            .ReturnsAsync(message)
            .Verifiable();

        var result = await Composer.ComposeAsync("model", CancellationToken.None);

        Assert.Same(message, result);
    }

    [Fact]
    public async Task ComposeAsync_WithAModelOfAnotherType_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await Composer.ComposeAsync(42, CancellationToken.None)
        );
    }

    #endregion
}
