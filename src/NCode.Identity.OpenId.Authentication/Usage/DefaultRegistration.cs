// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NCode.Identity.Registration;

namespace NCode.Identity.OpenId.Authentication.Usage;

internal static class DefaultRegistration
{
    extension(IServiceBuilder<OpenIdAuthenticationLibrary> builder)
    {
        public IServiceBuilder<OpenIdAuthenticationLibrary> AddTenantUsageServices()
        {
            var serviceCollection = builder.ServiceCollection;

            serviceCollection.AddOptions<TenantUsageOptions>().ValidateOnStart();
            var optionsValidator = ServiceDescriptor.Singleton<
                IValidateOptions<TenantUsageOptions>,
                TenantUsageOptionsValidator
            >();
            serviceCollection.TryAddEnumerable(optionsValidator);
            serviceCollection.TryAddSingleton(TimeProvider.System);
            serviceCollection.TryAddSingleton<DefaultTenantUsageRecorder>();
            serviceCollection.TryAddSingleton<ITenantUsageRecorder>(serviceProvider =>
                serviceProvider.GetRequiredService<DefaultTenantUsageRecorder>()
            );
            serviceCollection.AddHostedService<TenantUsageFlushService>();
            serviceCollection.AddHostedService<TenantUsagePruneService>();

            return builder;
        }
    }
}
