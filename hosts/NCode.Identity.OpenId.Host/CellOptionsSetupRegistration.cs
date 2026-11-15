// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NCode.Identity.Secrets.AwsKms.Options;
using NCode.Identity.Secrets.GoogleCloudKms.Options;
using NCode.Identity.SharedState.Valkey.Options;

namespace NCode.Identity.OpenId.Host;

/// <summary>
/// Registers the option setups that carry the cell's name and cloud regions into the Valkey and KMS adapters.
/// </summary>
internal static class CellOptionsSetupRegistration
{
    extension(IServiceCollection serviceCollection)
    {
        /// <summary>
        /// Registers <see cref="CellValkeyOptionsSetup"/>, which scopes every Valkey key and channel to the cell, and
        /// <see cref="CellKmsRegionOptionsSetup"/> for each AWS KMS and Google Cloud KMS options type, which limits the
        /// adapters to the cell's cloud regions. Calling it more than once registers each setup once.
        /// </summary>
        /// <returns>The same <see cref="IServiceCollection"/> instance for method chaining.</returns>
        public IServiceCollection AddCellOptionsSetups()
        {
            ArgumentNullException.ThrowIfNull(serviceCollection);

            serviceCollection.TryAddEnumerable(
                ServiceDescriptor.Singleton<
                    IPostConfigureOptions<ValkeyOptions>,
                    CellValkeyOptionsSetup
                >()
            );
            serviceCollection.TryAddEnumerable(
                ServiceDescriptor.Singleton<
                    IPostConfigureOptions<AwsKmsKeyWrapperOptions>,
                    CellKmsRegionOptionsSetup
                >()
            );
            serviceCollection.TryAddEnumerable(
                ServiceDescriptor.Singleton<
                    IPostConfigureOptions<AwsKmsRemoteKeyProviderOptions>,
                    CellKmsRegionOptionsSetup
                >()
            );
            serviceCollection.TryAddEnumerable(
                ServiceDescriptor.Singleton<
                    IPostConfigureOptions<GoogleCloudKmsKeyWrapperOptions>,
                    CellKmsRegionOptionsSetup
                >()
            );
            serviceCollection.TryAddEnumerable(
                ServiceDescriptor.Singleton<
                    IPostConfigureOptions<GoogleCloudKmsRemoteKeyProviderOptions>,
                    CellKmsRegionOptionsSetup
                >()
            );

            return serviceCollection;
        }
    }
}
