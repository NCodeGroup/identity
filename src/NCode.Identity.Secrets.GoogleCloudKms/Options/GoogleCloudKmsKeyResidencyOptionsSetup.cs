// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.Secrets.Options;

namespace NCode.Identity.Secrets.GoogleCloudKms.Options;

/// <summary>
/// Limits the Google Cloud Key Management Service key wrapper and remote key provider to the Cloud KMS locations that
/// <see cref="KeyResidencyOptions.CloudRegions"/> names, by replacing
/// <see cref="GoogleCloudKmsKeyWrapperOptions.AllowedLocations"/> and
/// <see cref="GoogleCloudKmsRemoteKeyProviderOptions.AllowedLocations"/> with them. When it names no location, each
/// list stays as configured.
/// </summary>
/// <param name="keyResidencyOptionsAccessor">The options that name the cloud regions of the deployment's keys.</param>
// See ADR-1235.
internal sealed class GoogleCloudKmsKeyResidencyOptionsSetup(
    IOptions<KeyResidencyOptions> keyResidencyOptionsAccessor
)
    : IPostConfigureOptions<GoogleCloudKmsKeyWrapperOptions>,
        IPostConfigureOptions<GoogleCloudKmsRemoteKeyProviderOptions>
{
    /// <summary>
    /// Gets the options that name the cloud regions of the deployment's keys.
    /// </summary>
    private IOptions<KeyResidencyOptions> KeyResidencyOptionsAccessor { get; } =
        keyResidencyOptionsAccessor;

    /// <inheritdoc />
    public void PostConfigure(string? name, GoogleCloudKmsKeyWrapperOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (KeyResidencyOptionsAccessor.Value.CloudRegions is { Count: > 0 } cloudRegions)
            options.AllowedLocations = [.. cloudRegions];
    }

    /// <inheritdoc />
    public void PostConfigure(string? name, GoogleCloudKmsRemoteKeyProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (KeyResidencyOptionsAccessor.Value.CloudRegions is { Count: > 0 } cloudRegions)
            options.AllowedLocations = [.. cloudRegions];
    }
}
