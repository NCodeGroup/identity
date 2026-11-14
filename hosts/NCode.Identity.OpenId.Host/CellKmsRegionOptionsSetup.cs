// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.Secrets.AwsKms.Options;
using NCode.Identity.Secrets.GoogleCloudKms.Options;

namespace NCode.Identity.OpenId.Host;

/// <summary>
/// Limits the AWS KMS and Google Cloud KMS adapters to the cloud regions of the cell, by setting the allowed regions or
/// locations of each adapter's options from <see cref="CellOptions.CloudRegions"/>. A cell with no cloud regions leaves
/// each list as configured.
/// </summary>
/// <param name="cellOptionsAccessor">The options that name the cell's cloud regions.</param>
internal sealed class CellKmsRegionOptionsSetup(IOptions<CellOptions> cellOptionsAccessor)
    : IPostConfigureOptions<AwsKmsKeyWrapperOptions>,
        IPostConfigureOptions<AwsKmsRemoteKeyProviderOptions>,
        IPostConfigureOptions<GoogleCloudKmsKeyWrapperOptions>,
        IPostConfigureOptions<GoogleCloudKmsRemoteKeyProviderOptions>
{
    private IOptions<CellOptions> CellOptionsAccessor { get; } = cellOptionsAccessor;

    /// <inheritdoc />
    public void PostConfigure(string? name, AwsKmsKeyWrapperOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (CellOptionsAccessor.Value.CloudRegions is { Length: > 0 } cloudRegions)
            options.AllowedRegions = [.. cloudRegions];
    }

    /// <inheritdoc />
    public void PostConfigure(string? name, AwsKmsRemoteKeyProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (CellOptionsAccessor.Value.CloudRegions is { Length: > 0 } cloudRegions)
            options.AllowedRegions = [.. cloudRegions];
    }

    /// <inheritdoc />
    public void PostConfigure(string? name, GoogleCloudKmsKeyWrapperOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (CellOptionsAccessor.Value.CloudRegions is { Length: > 0 } cloudRegions)
            options.AllowedLocations = [.. cloudRegions];
    }

    /// <inheritdoc />
    public void PostConfigure(string? name, GoogleCloudKmsRemoteKeyProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (CellOptionsAccessor.Value.CloudRegions is { Length: > 0 } cloudRegions)
            options.AllowedLocations = [.. cloudRegions];
    }
}
