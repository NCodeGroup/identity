// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.Client.Options;
using NCode.Identity.Registration;
using NCode.Identity.Secrets.Collections;
using NCode.Identity.Secrets.KeyMaterial;
using NCode.Identity.Secrets.Keys;

namespace NCode.Identity.OpenId.Client.KeyRings;

/// <summary>
/// Loads every configured client key ring when the host starts, and before an offline command of the host runs, as an
/// <see cref="IStartupPrerequisite"/>: the keys of its PEM files and the keys that a key management service holds. A
/// key that cannot be read, or a remote key that cannot be proved, stops the host or fails the command.
/// </summary>
/// <remarks>
/// <see cref="IHostedLifecycleService.StartingAsync"/> runs after the options are validated and before any hosted
/// service starts, so the host never serves before its key rings are loaded. The key rings load once per process,
/// whichever of the host's start and a command asks first: a rotation takes effect on restart.
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
) : IHostedLifecycleService, IStartupPrerequisite, IDisposable
{
    private IOptions<ClientKeyRingsOptions> Options { get; } = options;

    private IHostEnvironment Environment { get; } = environment;

    private ISecretKeyFactory KeyFactory { get; } = keyFactory;

    private ISecretKeyCollectionProviderFactory CollectionFactory { get; } = collectionFactory;

    private ClientRemoteKeyReader RemoteKeyReader { get; } = remoteKeyReader;

    private ClientKeyRings KeyRings { get; } = keyRings;

    private SemaphoreSlim Gate { get; } = new(1, 1);

    private bool IsLoaded { get; set; }

    /// <inheritdoc />
    public async Task StartingAsync(CancellationToken cancellationToken) =>
        await PrepareAsync(cancellationToken);

    /// <inheritdoc />
    public async ValueTask PrepareAsync(CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (IsLoaded)
                return;

            await LoadAsync(cancellationToken);
            IsLoaded = true;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => Gate.Dispose();

    private async ValueTask LoadAsync(CancellationToken cancellationToken)
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
                    var pemKey = ClientPemKeyReader.Read(KeyFactory, name, path);
                    keys.Add(pemKey);
                }

                foreach (var id in remoteKeys.Keys.Order(StringComparer.Ordinal))
                {
                    var remoteKey = await RemoteKeyReader.ReadAsync(
                        name,
                        id,
                        remoteKeys[id],
                        cancellationToken
                    );
                    keys.Add(remoteKey);
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

            var keyRingCollection = CollectionFactory.CreateStatic(keys);
            KeyRings.Add(name, keyRingCollection);
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
