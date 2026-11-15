// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public class DirectoryUrlsTests : BaseTests
{
    #region Describe Tests

    [Fact]
    public void Describe_WhenTheUrlCarriesCredentialsAQueryAndAFragment_KeepsOnlyTheSchemeServerAndPath()
    {
        var result = DirectoryUrls.Describe("https://user:secret@directory.example:8443/a/b?q=1#f");

        Assert.Equal("https://directory.example:8443/a/b", result);
    }

    [Theory]
    [InlineData("user:secret@directory.example")]
    [InlineData("user:secret@directory.example:8443/path")]
    [InlineData("https:\\\\user:secret@directory.example")]
    public void Describe_WhenTheTextIsACredentialPairWithoutAScheme_ReturnsAFixedText(string value)
    {
        var result = DirectoryUrls.Describe(value);

        Assert.Equal("<not an absolute URL>", result);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("directory.example:8443")]
    public void Describe_WhenTheTextHoldsNoAtSign_ReturnsTheText(string value)
    {
        var result = DirectoryUrls.Describe(value);

        Assert.Equal(value, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Describe_WhenTheTextIsNullOrEmpty_ReturnsAnEmptyString(string? value)
    {
        var result = DirectoryUrls.Describe(value);

        Assert.Equal(string.Empty, result);
    }

    #endregion
}
