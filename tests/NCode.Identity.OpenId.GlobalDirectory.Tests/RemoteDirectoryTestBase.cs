// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using Moq;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public abstract class RemoteDirectoryTestBase : BaseTests
{
    protected const string AccessToken = "directory-access-token";

    protected const string BaseUrl = "https://directory.example.test/";

    private static IOptions<DirectoryClientOptions> ClientOptions { get; } =
        Options.Create(new DirectoryClientOptions { Url = BaseUrl });

    internal DirectoryHttpClient CreateClient(HttpMessageHandler handler)
    {
        var factoryMock = CreateStrictMock<IHttpClientFactory>();
        factoryMock
            .Setup(x => x.CreateClient(DirectoryClientDefaults.HttpClientName))
            .Returns(() => new HttpClient(handler, disposeHandler: false))
            .Verifiable(Times.AtLeastOnce);

        var tokenSourceMock = CreateStrictMock<ICellTokenSource>();
        tokenSourceMock
            .Setup(x => x.GetAccessTokenAsync(It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<string>(AccessToken))
            .Verifiable(Times.AtLeastOnce);

        return new DirectoryHttpClient(factoryMock.Object, tokenSourceMock.Object, ClientOptions);
    }
}
