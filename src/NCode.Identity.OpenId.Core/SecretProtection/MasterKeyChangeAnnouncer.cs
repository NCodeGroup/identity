// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.Dispatch;
using NCode.Identity.Secrets.Persistence.Options;
using NCode.Identity.Secrets.Persistence.Protection;
using NCode.Identity.SharedState;

namespace NCode.Identity.OpenId.SecretProtection;

/// <summary>
/// Tells every replica that the master keys of an owner changed, and after a rotation schedules the rewrap of the
/// values the retired key protects.
/// </summary>
/// <remarks>
/// The rewrap job is delayed by twice the refresh interval, by which time every replica has reloaded the owner's keys
/// whether or not it received the broadcast. The in-process dispatcher is not durable; the maintenance pass catches up
/// anything a restart dropped.
/// </remarks>
// See ADR-1407.
internal sealed class MasterKeyChangeAnnouncer(
    IReplicaBroadcaster replicaBroadcaster,
    IJobDispatcher jobDispatcher,
    IOptions<SecretProtectionOptions> options
) : IMasterKeyChangeObserver
{
    private const int RewrapMaxAttempts = 5;

    private IReplicaBroadcaster ReplicaBroadcaster { get; } = replicaBroadcaster;

    private IJobDispatcher JobDispatcher { get; } = jobDispatcher;

    private IOptions<SecretProtectionOptions> Options { get; } = options;

    /// <inheritdoc />
    public async ValueTask RotatedAsync(
        MasterKeyRotation rotation,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(rotation);

        var ownerName = rotation.Owner.ToString();
        await ReplicaBroadcaster.PublishAsync(
            MasterKeyBroadcast.ChannelName,
            ownerName,
            cancellationToken
        );

        if (rotation.RetiredKey is null)
            return;

        var rewrapJob = new MasterKeyRewrapJob
        {
            OwnerType = rotation.Owner.OwnerType,
            OwnerId = rotation.Owner.OwnerId,
        };
        var dispatchOptions = new DispatchOptions
        {
            Delay = Options.Value.MasterKeys.RefreshInterval * 2,
            MaxAttempts = RewrapMaxAttempts,
        };
        await JobDispatcher.DispatchAsync(rewrapJob, dispatchOptions, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask DestroyedAsync(MasterKeyInfo destroyed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destroyed);

        var destroyedOwnerName = destroyed.Owner.ToString();
        return ReplicaBroadcaster.PublishAsync(
            MasterKeyBroadcast.ChannelName,
            destroyedOwnerName,
            cancellationToken
        );
    }
}
