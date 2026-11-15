// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.SecretProtection;
using NCode.Identity.Registration;
using NCode.Identity.Secrets;
using NCode.Identity.Secrets.Persistence;
using NCode.Identity.Secrets.Persistence.Protection;
using Xunit;

namespace NCode.Identity.OpenId.Core.SecretProtection;

public class TeamChainResolverRegistrationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SecretProtection_InEitherRegistrationOrder_ResolvesTheTeamChain(
        bool persistenceFirst
    )
    {
        var serviceCollection = new ServiceCollection();

        if (persistenceFirst)
        {
            AddPersistence(serviceCollection);
            AddSecretProtection(serviceCollection);
        }
        else
        {
            AddSecretProtection(serviceCollection);
            AddPersistence(serviceCollection);
        }

        var descriptor = serviceCollection.Last(x =>
            x.ServiceType == typeof(ISecretOwnerParentResolver)
        );

        Assert.Equal(typeof(TeamChainSecretOwnerParentResolver), descriptor.ImplementationType);
    }

    private static void AddPersistence(IServiceCollection serviceCollection) =>
        new ServiceBuilder<SecretsLibrary>(serviceCollection).AddPersistenceLibrary();

    private static void AddSecretProtection(IServiceCollection serviceCollection) =>
        new ServiceBuilder<OpenIdCoreLibrary>(serviceCollection).AddSecretProtectionServices();
}
