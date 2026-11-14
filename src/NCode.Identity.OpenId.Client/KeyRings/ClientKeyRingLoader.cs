// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NCode.Identity.Secrets.Collections;
using NCode.Identity.Secrets.KeyMaterial;
using NCode.Identity.Secrets.Keys;

namespace NCode.Identity.OpenId.Client.KeyRings;

/// <summary>
/// Loads every configured client key ring when the host starts: the keys of its PEM files and the keys that a key
/// management service holds. A key that cannot be read, or a remote key that cannot be proved, stops the host.
/// </summary>
/// <remarks>
/// <see cref="IHostedLifecycleService.StartingAsync"/> runs after the options are validated and before any hosted
/// service starts, so the host never serves before its key rings are loaded. A key ring loads once per process: a
/// rotation takes effect on restart.
/// </remarks>
// See ADR-1439.
// See ADR-1479.
internal sealed class ClientKeyRingLoader(
    IOptions<ClientKeyRingsOptions> options,
    IHostEnvironment environment,
    ISecretKeyFactory keyFactory,
    ISecretKeyCollectionProviderFactory collectionFactory,
    ClientRemoteKeyReader remoteKeyReader,
    ClientKeyRings keyRings
) : IHostedLifecycleService
{
    private IOptions<ClientKeyRingsOptions> Options { get; } = options;

    private IHostEnvironment Environment { get; } = environment;

    private ISecretKeyFactory KeyFactory { get; } = keyFactory;

    private ISecretKeyCollectionProviderFactory CollectionFactory { get; } = collectionFactory;

    private ClientRemoteKeyReader RemoteKeyReader { get; } = remoteKeyReader;

    private ClientKeyRings KeyRings { get; } = keyRings;

    /// <inheritdoc />
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        var configured = Options.Value.KeyRings;
        foreach (var name in configured.Keys.Order(StringComparer.Ordinal))
        {
            var keyRing = configured[name];
            var keys = new List<SecretKey>();
            // The options validator reads a null list as an empty one.
            var pemFiles = keyRing.PemFiles ?? [];
            var remoteKeys = keyRing.RemoteKeys ?? [];
            try
            {
                foreach (var configuredPath in pemFiles)
                {
                    var path = Path.GetFullPath(configuredPath, Environment.ContentRootPath);
                    keys.Add(ClientPemKeyReader.Read(KeyFactory, name, path));
                }

                foreach (var id in remoteKeys.Keys.Order(StringComparer.Ordinal))
                {
                    keys.Add(
                        await RemoteKeyReader.ReadAsync(name, id, remoteKeys[id], cancellationToken)
                    );
                }
            }
            catch
            {
                foreach (var key in keys)
                {
                    (key as IDisposable)?.Dispose();
                }

                throw;
            }

            KeyRings.Add(name, CollectionFactory.CreateStatic(keys));
        }
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
