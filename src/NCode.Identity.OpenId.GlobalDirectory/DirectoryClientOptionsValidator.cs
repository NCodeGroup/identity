// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Validates <see cref="DirectoryClientOptions"/> and the control-plane issuer when the host starts, so a cell that
/// cannot authenticate to the directory stops the host instead of failing the first time it reserves an identifier.
/// </summary>
// See ADR-1285 (the endpoint rule) and ADR-1137 (configuration is validated at startup).
internal sealed class DirectoryClientOptionsValidator(IOptions<CellOptions> cellOptions)
    : IValidateOptions<DirectoryClientOptions>
{
    private CellOptions CellOptions { get; } = cellOptions.Value;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, DirectoryClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        if (!IsAllowedEndpoint(options.Url))
        {
            failures.Add(
                $"{DirectoryClientOptions.SectionName}:{nameof(DirectoryClientOptions.Url)} must be an absolute https URL, or an http URL on a loopback host, with no query or fragment."
            );
        }

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add(
                $"{DirectoryClientOptions.SectionName}:{nameof(DirectoryClientOptions.ClientId)} must name the machine client of the cell."
            );
        }

        if (string.IsNullOrWhiteSpace(options.KeyRing))
        {
            failures.Add(
                $"{DirectoryClientOptions.SectionName}:{nameof(DirectoryClientOptions.KeyRing)} must name the key ring that signs the client assertion."
            );
        }

        if (options.PollInterval <= TimeSpan.Zero)
        {
            failures.Add(
                $"{DirectoryClientOptions.SectionName}:{nameof(DirectoryClientOptions.PollInterval)} must be a positive time span."
            );
        }

        if (!IsAllowedEndpoint(CellOptions.ControlPlane.Issuer))
        {
            failures.Add(
                $"{CellOptions.SectionName}:{nameof(CellOptions.ControlPlane)}:{nameof(ControlPlaneOptions.Issuer)} must be an absolute https URL, or an http URL on a loopback host, when {DirectoryClientOptions.SectionName}:{nameof(DirectoryClientOptions.Url)} is set."
            );
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsAllowedEndpoint(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Any(char.IsWhiteSpace))
            return false;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            return false;

        return uri.Scheme == Uri.UriSchemeHttps
            || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
    }
}
