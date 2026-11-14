// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NCode.Identity.Secrets.RemoteKeys;

namespace NCode.Identity.OpenId.Client.KeyRings;

/// <summary>
/// Validates <see cref="ClientKeyRingsOptions"/> when the host starts, so a key ring that cannot load stops the host
/// instead of failing on the first request. Each key ring is named <c>{SectionPath}:{name}</c> in the failures, and a
/// relative PEM path is read against <see cref="IHostEnvironment.ContentRootPath"/>.
/// </summary>
// See ADR-1439.
// See ADR-1479.
internal sealed class ClientKeyRingsOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<ClientKeyRingsOptions>
{
    private IHostEnvironment Environment { get; } = environment;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ClientKeyRingsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];
        ValidateKeyRings(options.SectionPath, options.KeyRings, failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

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

            var keyRingKey = $"{sectionPath}:{name}";
            var pemFiles = keyRing?.PemFiles ?? [];
            var remoteKeys = keyRing?.RemoteKeys ?? [];
            if (pemFiles.Count == 0 && remoteKeys.Count == 0)
            {
                failures.Add($"{keyRingKey} must list at least one PEM file or remote key.");
                continue;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var duplicateIds = new SortedSet<string>(StringComparer.Ordinal);

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

                if (!File.Exists(Path.GetFullPath(path, Environment.ContentRootPath)))
                {
                    failures.Add(
                        $"{keyRingKey}:PemFiles:{i} names '{path}', which does not exist."
                    );
                }
            }

            foreach (var (id, remoteKey) in remoteKeys.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(id) && !ids.Add(id))
                    duplicateIds.Add(id);

                ValidateRemoteKey(keyRingKey, id, remoteKey, failures);
            }

            foreach (var id in duplicateIds)
                failures.Add($"{keyRingKey} holds more than one key with the id '{id}'.");
        }
    }

    private static void ValidateRemoteKey(
        string keyRingKey,
        string id,
        ClientRemoteKeyOptions? remoteKey,
        List<string> failures
    )
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            failures.Add($"{keyRingKey}:RemoteKeys has a key without an id.");
            return;
        }

        var key = $"{keyRingKey}:RemoteKeys:{id}";

        if (!RemoteKeyReference.IsValidProviderName(remoteKey?.Provider))
        {
            failures.Add(
                $"{key}:Provider must name a remote key provider: lowercase letters and digits in groups separated by single hyphens."
            );
        }

        if (string.IsNullOrWhiteSpace(remoteKey?.KeyId))
            failures.Add($"{key}:KeyId must not be blank.");

        if (string.IsNullOrWhiteSpace(remoteKey?.Algorithm))
            failures.Add($"{key}:Algorithm must not be blank.");

        if (string.IsNullOrWhiteSpace(remoteKey?.SecretType))
            failures.Add($"{key}:SecretType must not be blank.");

        if (remoteKey is null || remoteKey.KeySizeBits <= 0)
            failures.Add($"{key}:KeySizeBits must be positive.");
    }
}
