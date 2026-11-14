// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.Secrets.Persistence.Options;

namespace NCode.Identity.Server;

/// <summary>
/// Makes the name of the cell the tag of every key the server creates in a key management service, so the background
/// sweep of a cell never destroys the keys of another cell that shares the service. Post-configuration sets
/// <see cref="RemoteKeyOptions.Deployment"/> to <see cref="CellOptions.Name"/> when no value is configured, and
/// validation fails when a configured value differs from the cell's name.
/// </summary>
/// <param name="cellOptionsAccessor">The options that name the cell.</param>
// See ADR-1478.
internal sealed class CellRemoteKeyOptionsSetup(IOptions<CellOptions> cellOptionsAccessor)
    : IPostConfigureOptions<SecretProtectionOptions>,
        IValidateOptions<SecretProtectionOptions>
{
    private IOptions<CellOptions> CellOptionsAccessor { get; } = cellOptionsAccessor;

    private static string DeploymentKey =>
        $"{SecretProtectionOptions.DefaultSectionName}:{nameof(SecretProtectionOptions.RemoteKeys)}:{nameof(RemoteKeyOptions.Deployment)}";

    /// <inheritdoc />
    public void PostConfigure(string? name, SecretProtectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var cellName = CellOptionsAccessor.Value.Name;
        if (string.IsNullOrEmpty(cellName) || !string.IsNullOrEmpty(options.RemoteKeys.Deployment))
            return;

        options.RemoteKeys.Deployment = cellName;
    }

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, SecretProtectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var cellName = CellOptionsAccessor.Value.Name;
        var deployment = options.RemoteKeys.Deployment;

        return
            string.IsNullOrEmpty(deployment)
            || string.Equals(deployment, cellName, StringComparison.Ordinal)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{DeploymentKey} '{deployment}' must be left unset or equal {CellOptions.SectionName}:{nameof(CellOptions.Name)} '{cellName}': the cell's name is the tag of the keys it creates."
            );
    }
}
