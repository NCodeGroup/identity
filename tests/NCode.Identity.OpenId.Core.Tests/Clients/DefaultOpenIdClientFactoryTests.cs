// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Moq;
using NCode.Identity.OpenId.Authentication.Clients;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.Secrets.Collections;
using NCode.Identity.Settings;
using NCode.PropertyBag;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Clients;

public class DefaultOpenIdClientFactoryTests : BaseTests
{
    #region CreatePublicClientAsync Tests

    [Fact]
    public async Task CreatePublicClientAsync_WhenCreated_GivesTheClientAChildOfTheContextsBag()
    {
        var definition = new OpenIdClientDefinition
        {
            ClientId = "client-1",
            PrincipalId = "principal-1",
            Settings = CreateStrictMock<IReadOnlySettingCollection>().Object,
            Secrets = [],
            SecretVerifiers = [],
        };

        var secretKeys = CreateStrictMock<ISecretKeyCollection>();
        var secretKeyCollectionFactory = CreateStrictMock<ISecretKeyCollectionFactory>();
        secretKeyCollectionFactory
            .Setup(x => x.Create(definition.Secrets))
            .Returns(secretKeys.Object)
            .Verifiable();

        IReadOnlyCollection<string> redirectUris = [];
        IPropertyBag? clientBag = null;
        var factory = CreatePartialMock<DefaultOpenIdClientFactory>(
            secretKeyCollectionFactory.Object
        );
        factory
            .Setup(x => x.GetRedirectUris(definition.Settings))
            .Returns(redirectUris)
            .Verifiable();
        factory
            .Setup(x =>
                x.CreatePublicClient(
                    definition,
                    secretKeys.Object,
                    redirectUris,
                    It.IsAny<IPropertyBag>()
                )
            )
            .Callback<
                OpenIdClientDefinition,
                ISecretKeyCollection,
                IReadOnlyCollection<string>,
                IPropertyBag
            >((_, _, _, bag) => clientBag = bag)
            .Returns(CreateStrictMock<OpenIdClient>().Object)
            .Verifiable();

        var contextBag = new PropertyBagDictionary();
        var openIdContext = CreateStrictMock<OpenIdContext>();
        openIdContext.SetupGet(x => x.PropertyBag).Returns(contextBag).Verifiable();

        await factory.Object.CreatePublicClientAsync(
            openIdContext.Object,
            definition,
            CancellationToken.None
        );

        Assert.Same(contextBag, Assert.IsType<LayeredPropertyBag>(clientBag).Parent);
    }

    #endregion
}
