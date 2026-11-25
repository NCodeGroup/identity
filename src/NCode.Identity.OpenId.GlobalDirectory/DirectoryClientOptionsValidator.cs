// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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
    private const int MaxRetryAttemptsLimit = 10;

    private static string Prefix => DirectoryClientOptions.SectionName;

    private static TimeSpan MinTimeout { get; } = TimeSpan.FromMilliseconds(10);

    private static TimeSpan MaxTimeout { get; } = TimeSpan.FromMinutes(5);

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, DirectoryClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        if (!AbsoluteUri.TryParseServiceUrl(options.Url, out _))
        {
            var url = DirectoryUrls.Describe(options.Url);
            failures.Add(
                $"{Prefix}:{nameof(DirectoryClientOptions.Url)} '{url}' must be an absolute https URL, or an http URL on a loopback host, with no user information, query or fragment."
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
                $"{Prefix}:{nameof(DirectoryClientOptions.ClientId)} '{options.ClientId}' must be '{GlobalDirectoryProtocol.CellClientIdPrefix}' followed by the cell's name: {CellLabelRules.Description}."
            );
        }

        if (string.IsNullOrWhiteSpace(options.KeyRing))
        {
            failures.Add(
                $"{Prefix}:{nameof(DirectoryClientOptions.KeyRing)} '{options.KeyRing}' must name the key ring that signs the client assertion."
            );
        }

        if (!string.IsNullOrEmpty(options.CaCertificatesPath))
        {
            var failure = DescribeCaCertificatesFailure(options.CaCertificatesPath);
            if (failure is not null)
                failures.Add(
                    $"{Prefix}:{nameof(DirectoryClientOptions.CaCertificatesPath)} '{options.CaCertificatesPath}' {failure}."
                );
        }

        if (!TimerPeriod.IsValid(options.PollInterval))
        {
            failures.Add(
                $"{Prefix}:{nameof(DirectoryClientOptions.PollInterval)} '{options.PollInterval}' must be a time span from {TimerPeriod.MinValue} to {TimerPeriod.MaxValue}."
            );
        }

        if (!TimerPeriod.IsValid(options.PublishInterval))
        {
            failures.Add(
                $"{Prefix}:{nameof(DirectoryClientOptions.PublishInterval)} '{options.PublishInterval}' must be a time span from {TimerPeriod.MinValue} to {TimerPeriod.MaxValue}."
            );
        }

        AddHttpFailures(options, failures);

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

    /// <summary>
    /// Adds the failures of the limits that bound a call to the directory.
    /// </summary>
    /// <param name="options">The options to validate.</param>
    /// <param name="failures">The list that receives each failure.</param>
    private static void AddHttpFailures(DirectoryClientOptions options, List<string> failures)
    {
        if (options.AttemptTimeout < MinTimeout || options.AttemptTimeout > MaxTimeout)
        {
            failures.Add(
                $"{Prefix}:{nameof(DirectoryClientOptions.AttemptTimeout)} '{options.AttemptTimeout}' must be between {MinTimeout} and {MaxTimeout}."
            );
        }

        if (options.TotalTimeout < MinTimeout || options.TotalTimeout > MaxTimeout)
        {
            failures.Add(
                $"{Prefix}:{nameof(DirectoryClientOptions.TotalTimeout)} '{options.TotalTimeout}' must be between {MinTimeout} and {MaxTimeout}."
            );
        }

        if (options.AttemptTimeout > options.TotalTimeout)
        {
            failures.Add(
                $"{Prefix}:{nameof(DirectoryClientOptions.AttemptTimeout)} '{options.AttemptTimeout}' must not be greater than {Prefix}:{nameof(DirectoryClientOptions.TotalTimeout)} '{options.TotalTimeout}'."
            );
        }

        if (options.MaxRetryAttempts < 0 || options.MaxRetryAttempts > MaxRetryAttemptsLimit)
        {
            failures.Add(
                $"{Prefix}:{nameof(DirectoryClientOptions.MaxRetryAttempts)} '{options.MaxRetryAttempts}' must be between 0 and {MaxRetryAttemptsLimit}."
            );
        }

        if (options.RetryDelay < TimeSpan.Zero || options.RetryDelay > MaxTimeout)
        {
            failures.Add(
                $"{Prefix}:{nameof(DirectoryClientOptions.RetryDelay)} '{options.RetryDelay}' must be between {TimeSpan.Zero} and {MaxTimeout}."
            );
        }
    }

    /// <summary>
    /// Checks that the file at the given path holds at least one usable CA certificate.
    /// </summary>
    /// <param name="path">The path of the CA certificate file.</param>
    /// <returns>A description of why the file is unusable, or <see langword="null"/> for a usable file.</returns>
    private static string? DescribeCaCertificatesFailure(string path)
    {
        if (!File.Exists(path))
            return "must name an existing file";

        try
        {
            var authorities = new X509Certificate2Collection();
            authorities.ImportFromPemFile(path);
            return authorities.Count == 0 ? "must hold at least one PEM certificate" : null;
        }
        catch (Exception exception) when (exception is CryptographicException or IOException)
        {
            return "must be a readable PEM file of certificates";
        }
    }
}
