// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.Time;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Validates <see cref="DirectoryClientOptions"/> alone when the host starts, so a cell that cannot authenticate to the
/// directory stops the host instead of failing the first time it reserves an identifier. The control-plane issuer is
/// checked by <see cref="DirectoryClientStartupCheck"/>.
/// </summary>
// See ADR-1285 (the endpoint rule) and ADR-1137 (configuration is validated at startup).
internal sealed class DirectoryClientOptionsValidator : IValidateOptions<DirectoryClientOptions>
{
    private static string Prefix => DirectoryClientOptions.SectionName;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, DirectoryClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        if (!AbsoluteUri.TryParseServiceUrl(options.Url, allowAnyHttp: false, out _))
        {
            failures.Add(
                $"{Prefix}:{nameof(DirectoryClientOptions.Url)} '{DirectoryUrls.Describe(options.Url)}' must be an absolute https URL, or an http URL on a loopback host, with no user information, query or fragment."
            );
        }

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add(
                $"{Prefix}:{nameof(DirectoryClientOptions.ClientId)} '{options.ClientId}' must name the machine client of the cell."
            );
        }
        else if (
            !options.ClientId.StartsWith(
                GlobalDirectoryProtocol.CellClientIdPrefix,
                StringComparison.Ordinal
            )
            || !CellLabelRules.IsValidLabel(
                options.ClientId[GlobalDirectoryProtocol.CellClientIdPrefix.Length..]
            )
        )
        {
            failures.Add(
                $"{Prefix}:{nameof(DirectoryClientOptions.ClientId)} '{options.ClientId}' must be '{GlobalDirectoryProtocol.CellClientIdPrefix}' followed by the cell's name, a lower-case DNS label."
            );
        }

        if (string.IsNullOrWhiteSpace(options.KeyRing))
        {
            failures.Add(
                $"{Prefix}:{nameof(DirectoryClientOptions.KeyRing)} '{options.KeyRing}' must name the key ring that signs the client assertion."
            );
        }

        if (!TimerPeriod.IsValid(options.PollInterval))
        {
            failures.Add(
                $"{Prefix}:{nameof(DirectoryClientOptions.PollInterval)} '{options.PollInterval}' must be a time span from {TimerPeriod.MinValue} to {TimerPeriod.MaxValue}."
            );
        }

        if (!TimerPeriod.IsValid(options.TokenRequestTimeout))
        {
            failures.Add(
                $"{Prefix}:{nameof(DirectoryClientOptions.TokenRequestTimeout)} '{options.TokenRequestTimeout}' must be a time span from {TimerPeriod.MinValue} to {TimerPeriod.MaxValue}."
            );
        }

        if (!TimerPeriod.IsValid(options.TokenFailureCacheDuration))
        {
            failures.Add(
                $"{Prefix}:{nameof(DirectoryClientOptions.TokenFailureCacheDuration)} '{options.TokenFailureCacheDuration}' must be a time span from {TimerPeriod.MinValue} to {TimerPeriod.MaxValue}."
            );
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
