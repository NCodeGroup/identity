// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NCode.Identity.Secrets.Collections;
using NCode.Identity.Secrets.RemoteKeys;

namespace NCode.Identity.OpenId.Client.Options;

/// <summary>
/// Validates <see cref="ClientKeyRingsOptions"/> when the host starts, so a key ring that cannot load stops the host
/// instead of failing on the first request. A key ring that a keyed <see cref="ISecretKeyCollectionProvider"/> already
/// provides in code is refused. A key ring's name and each key's id are letters and digits in groups separated by
/// single '.', '_' or '-', and two ids of a ring are compared without case. Each key ring is named
/// <c>{SectionPath}:{name}</c> in the failures, and a relative PEM path is read against
/// <see cref="IHostEnvironment.ContentRootPath"/>.
/// </summary>
// See ADR-1439.
// See ADR-1479.
internal sealed class ClientKeyRingsOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<ClientKeyRingsOptions>
{
    /// <summary>
    /// Checks one remote key of a key ring: its provider is a valid remote key provider name, its key id, algorithm and
    /// secret type are not blank, and its key size is positive. Each failure is named <paramref name="key"/>, and a
    /// refused provider name or key size is stated in its failure.
    /// </summary>
    /// <param name="key">The configuration path of the remote key, <c>{keyRingKey}:RemoteKeys:{id}</c>.</param>
    /// <param name="remoteKey">The options of the remote key, or <c>null</c> when its section is empty.</param>
    /// <param name="failures">The list that collects every failure found.</param>
    private static void ValidateRemoteKey(
        string key,
        ClientRemoteKeyOptions? remoteKey,
        List<string> failures
    )
    {
        var provider = remoteKey?.Provider;
        if (!RemoteKeyReference.IsValidProviderName(provider))
        {
            var actual = provider is null ? "it is not set" : $"it is '{provider}'";
            failures.Add(
                $"{key}:Provider must name a remote key provider: lowercase letters and digits in groups separated by single hyphens; {actual}."
            );
        }

        if (string.IsNullOrWhiteSpace(remoteKey?.KeyId))
            failures.Add($"{key}:KeyId must not be blank.");

        if (string.IsNullOrWhiteSpace(remoteKey?.Algorithm))
            failures.Add($"{key}:Algorithm must not be blank.");

        if (string.IsNullOrWhiteSpace(remoteKey?.SecretType))
            failures.Add($"{key}:SecretType must not be blank.");

        if (remoteKey is null || remoteKey.KeySizeBits <= 0)
        {
            var actual = remoteKey is null ? "it is not set" : $"it is {remoteKey.KeySizeBits}";
            failures.Add($"{key}:KeySizeBits must be positive; {actual}.");
        }
    }

    private IHostEnvironment Environment { get; } = environment;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ClientKeyRingsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];
        ValidateKeyRings(options.SectionPath, options.KeyRings, failures);
        foreach (var keyRingName in options.RegisteredInCode.Order(StringComparer.Ordinal))
        {
            failures.Add(
                $"{options.SectionPath}:{keyRingName} names a key ring that a keyed {nameof(ISecretKeyCollectionProvider)} already provides; a key ring is registered once, in code or in configuration."
            );
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// Checks every configured key ring, in ordinal order of its name: the section is set, each key ring has a name and
    /// lists at least one PEM file or remote key, each PEM path is not blank and names a file that exists under
    /// <see cref="IHostEnvironment.ContentRootPath"/>, each remote key is valid, and no two keys of a key ring share an
    /// id. A PEM file's id is its file name without its extension.
    /// </summary>
    /// <param name="sectionPath">The configuration path of the key rings section, which prefixes every failure.</param>
    /// <param name="keyRings">The configured key rings by name, or <c>null</c> when the section is not set.</param>
    /// <param name="failures">The list that collects every failure found.</param>
    private void ValidateKeyRings(
        string sectionPath,
        Dictionary<string, ClientKeyRingOptions>? keyRings,
        List<string> failures
    )
    {
        if (keyRings is null)
        {
            failures.Add($"{sectionPath} must be set.");
            return;
        }

        foreach (var (name, keyRing) in keyRings.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                failures.Add($"{sectionPath} has a key ring without a name.");
                continue;
            }

            if (!KeyRingIdentifiers.IsValid(name))
            {
                failures.Add($"{sectionPath}:{name} must be {KeyRingIdentifiers.Description}.");
                continue;
            }

            var keyRingKey = $"{sectionPath}:{name}";
            var pemFiles = keyRing?.PemFiles ?? [];
            var remoteKeys = keyRing?.RemoteKeys ?? [];
            if (pemFiles.Count == 0 && remoteKeys.Count == 0)
            {
                failures.Add($"{keyRingKey} must list at least one PEM file or remote key.");
                continue;
            }

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var duplicateIds = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < pemFiles.Count; i++)
            {
                var path = pemFiles[i];
                if (string.IsNullOrWhiteSpace(path))
                {
                    failures.Add($"{keyRingKey}:PemFiles:{i} must not be blank.");
                    continue;
                }

                var id = Path.GetFileNameWithoutExtension(path);
                if (!ids.Add(id))
                    duplicateIds.Add(id);

                if (!KeyRingIdentifiers.IsValid(id))
                {
                    failures.Add(
                        $"{keyRingKey}:PemFiles:{i} names '{path}', whose file name without its extension must be {KeyRingIdentifiers.Description}."
                    );
                }

                var fullPath = Path.GetFullPath(path, Environment.ContentRootPath);
                if (!File.Exists(fullPath))
                {
                    failures.Add(
                        $"{keyRingKey}:PemFiles:{i} names '{path}', which does not exist."
                    );
                }
            }

            foreach (var (id, remoteKey) in remoteKeys.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    failures.Add($"{keyRingKey}:RemoteKeys has a key without an id.");
                    continue;
                }

                if (!KeyRingIdentifiers.IsValid(id))
                {
                    failures.Add(
                        $"{keyRingKey}:RemoteKeys:{id} must have an id that is {KeyRingIdentifiers.Description}."
                    );
                    continue;
                }

                if (!ids.Add(id))
                    duplicateIds.Add(id);

                var remoteKeyPath = $"{keyRingKey}:RemoteKeys:{id}";
                ValidateRemoteKey(remoteKeyPath, remoteKey, failures);
            }

            foreach (var id in duplicateIds)
            {
                failures.Add(
                    $"{keyRingKey} holds more than one key with the id '{id}', compared without case."
                );
            }
        }
    }
}
