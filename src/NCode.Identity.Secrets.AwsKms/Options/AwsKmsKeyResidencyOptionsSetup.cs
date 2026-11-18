// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.Secrets.Options;

namespace NCode.Identity.Secrets.AwsKms.Options;

/// <summary>
/// Limits the AWS Key Management Service key wrapper and remote key provider to the cloud regions that
/// <see cref="KeyResidencyOptions.CloudRegions"/> names, by replacing
/// <see cref="AwsKmsKeyWrapperOptions.AllowedRegions"/> and <see cref="AwsKmsRemoteKeyProviderOptions.AllowedRegions"/>
/// with them. When it names no region, each list stays as configured.
/// </summary>
/// <param name="keyResidencyOptionsAccessor">The options that name the cloud regions of the deployment's keys.</param>
// See ADR-1235.
internal sealed class AwsKmsKeyResidencyOptionsSetup(
    IOptions<KeyResidencyOptions> keyResidencyOptionsAccessor
)
    : IPostConfigureOptions<AwsKmsKeyWrapperOptions>,
        IPostConfigureOptions<AwsKmsRemoteKeyProviderOptions>
{
    /// <summary>
    /// Gets the options that name the cloud regions of the deployment's keys.
    /// </summary>
    private IOptions<KeyResidencyOptions> KeyResidencyOptionsAccessor { get; } =
        keyResidencyOptionsAccessor;

    /// <inheritdoc />
    public void PostConfigure(string? name, AwsKmsKeyWrapperOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (KeyResidencyOptionsAccessor.Value.CloudRegions is { Count: > 0 } cloudRegions)
            options.AllowedRegions = [.. cloudRegions];
    }

    /// <inheritdoc />
    public void PostConfigure(string? name, AwsKmsRemoteKeyProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (KeyResidencyOptionsAccessor.Value.CloudRegions is { Count: > 0 } cloudRegions)
            options.AllowedRegions = [.. cloudRegions];
    }
}
