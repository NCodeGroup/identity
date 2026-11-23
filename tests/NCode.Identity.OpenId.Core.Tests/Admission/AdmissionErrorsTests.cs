// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Moq;
using NCode.Identity.OpenId.Authentication.Admission;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Environments;
using NCode.Identity.OpenId.Errors;
using NCode.Identity.OpenId.Messages;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Admission;

public sealed class AdmissionErrorsTests : BaseTests
{
    private Mock<OpenIdContext> MockContext { get; }

    private Mock<OpenIdEnvironment> MockEnvironment { get; }

    public AdmissionErrorsTests()
    {
        MockContext = CreateStrictMock<OpenIdContext>();
        MockEnvironment = CreateStrictMock<OpenIdEnvironment>();
    }

    #region Scaffolding

    private void SetupErrorFactory()
    {
        var mockErrorFactory = CreateStrictMock<IOpenIdErrorFactory>();
        mockErrorFactory
            .Setup(x => x.Create(OpenIdProtocol.ErrorCodes.TemporarilyUnavailable))
            .Returns<string>(code => new DefaultOpenIdError(MockEnvironment.Object, code))
            .Verifiable();
        MockContext.Setup(x => x.ErrorFactory).Returns(mockErrorFactory.Object).Verifiable();
    }

    #endregion

    #region Shed Tests

    [Fact]
    public void Shed_Always_AnswersTemporarilyUnavailableThatNoCacheKeeps()
    {
        SetupErrorFactory();

        var error = AdmissionErrors.Shed(MockContext.Object, TimeSpan.FromSeconds(2));

        Assert.Equal(OpenIdProtocol.ErrorCodes.TemporarilyUnavailable, error.Code);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, error.StatusCode);
        var headers = error.ResponseHeaders;
        Assert.NotNull(headers);
        var retryAfter = headers[HeaderNames.RetryAfter].ToString();
        var cacheControl = headers[HeaderNames.CacheControl].ToString();
        Assert.Equal("2", retryAfter);
        Assert.Equal("no-store", cacheControl);
    }

    [Theory]
    [InlineData(1.5, "2")]
    [InlineData(2.0, "2")]
    [InlineData(0.2, "1")]
    [InlineData(0.0, "1")]
    public void Shed_Always_RoundsRetryAfterUpToAWholeSecondOfAtLeastOne(
        double seconds,
        string expected
    )
    {
        SetupErrorFactory();

        var error = AdmissionErrors.Shed(MockContext.Object, TimeSpan.FromSeconds(seconds));

        var headers = error.ResponseHeaders;
        Assert.NotNull(headers);
        var retryAfter = headers[HeaderNames.RetryAfter].ToString();
        Assert.Equal(expected, retryAfter);
    }

    #endregion
}
