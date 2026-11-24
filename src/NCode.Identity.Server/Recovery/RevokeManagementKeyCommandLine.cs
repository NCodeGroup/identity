// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Server.Logging;

namespace NCode.Identity.Server.Recovery;

/// <summary>
/// The headless <c>revoke-management-key</c> command of the host: it records a management key identifier as revoked in
/// the database of a workload cell, so the cell refuses every management token that the key signed, and exits without
/// starting the server. An operator runs it as a one-off Job when the control plane cannot reach the cell.
/// </summary>
/// <remarks>
/// The exit code is <c>0</c> when the key is recorded as revoked, also when it already was, <c>1</c> when the command
/// fails, which it logs, and <c>2</c> for a usage error, when the key identifier is missing or the cell is the
/// control-plane cell, which revokes a key through the management API.
/// </remarks>
// See ADR-1536.
[PublicAPI]
public sealed class RevokeManagementKeyCommandLine
{
    /// <summary>
    /// The first argument that selects this command.
    /// </summary>
    public const string CommandName = "revoke-management-key";

    private RevokeManagementKeyCommandLine() { }

    /// <summary>
    /// Gets the usage text of the command.
    /// </summary>
    public static string Usage { get; } =
        $"Usage: {CommandName} <kid> (the identifier of the management key to refuse, in a workload cell)";

    /// <summary>
    /// Gets the identifier of the key to revoke, or <see langword="null"/> when the arguments are invalid.
    /// </summary>
    public string? KeyId { get; private init; }

    /// <summary>
    /// Gets the reason the arguments are invalid, or <see langword="null"/> when they are. A command with an error
    /// logs it with the <see cref="Usage"/> when it runs and exits with <c>2</c>.
    /// </summary>
    public string? Error { get; private init; }

    /// <summary>
    /// Parses the command line of the host.
    /// </summary>
    /// <param name="args">The command-line arguments of the host.</param>
    /// <returns>The command, or <see langword="null"/> when the arguments do not start with <see cref="CommandName"/>.
    /// A command whose <see cref="Error"/> is set names what is wrong with the arguments.</returns>
    public static RevokeManagementKeyCommandLine? Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Count == 0 || !string.Equals(args[0], CommandName, StringComparison.Ordinal))
            return null;

        if (args.Count == 1 || string.IsNullOrWhiteSpace(args[1]))
        {
            return new RevokeManagementKeyCommandLine
            {
                Error = $"The {CommandName} command needs the identifier of the key.",
            };
        }

        return args.Count > 2
            ? new RevokeManagementKeyCommandLine
            {
                Error =
                    $"The {CommandName} command takes one argument; '{args[2]}' is not recognized.",
            }
            : new RevokeManagementKeyCommandLine { KeyId = args[1] };
    }

    /// <summary>
    /// Gets the arguments that the host builds with: none when they select this command, because a command-line
    /// configuration provider would read its arguments as configuration, and the arguments otherwise.
    /// </summary>
    /// <param name="args">The command-line arguments of the host.</param>
    /// <returns>The arguments for the host builder.</returns>
    public static string[] GetHostArguments(string[] args)
    {
        return HostCommandLine.GetHostArguments(args, CommandName);
    }

    /// <summary>
    /// Runs the command against the services of a built host, without starting the host.
    /// </summary>
    /// <param name="services">The services of the built host.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the command.</param>
    /// <returns>The <see cref="ValueTask{TResult}"/> that represents the command, containing its exit code.</returns>
    public async ValueTask<int> RunAsync(
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(services);

        var logger = services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger<RevokeManagementKeyCommandLine>();

        try
        {
            return await RevokeAsync(services, logger, cancellationToken);
        }
        // Only a cancellation the caller requested propagates; any other failure is logged and ends the command with 1,
        // because an exception that escapes Main does not end a process that runs as PID 1 in its container.
        catch (Exception exception)
            when (exception is not OperationCanceledException
                || !cancellationToken.IsCancellationRequested
            )
        {
            logger.RevokeManagementKeyCommandFailed(exception);
            return 1;
        }
    }

    private async ValueTask<int> RevokeAsync(
        IServiceProvider services,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        if (KeyId is not { } keyId)
        {
            logger.RevokeManagementKeyCommandUsageInvalid(Error ?? "No key was given.", Usage);
            return 2;
        }

        var cellOptions = services.GetRequiredService<IOptions<CellOptions>>().Value;
        if (cellOptions.IsControlPlane)
        {
            logger.RevokeManagementKeyCommandOnTheControlPlane();
            return 2;
        }

        var timeProvider = services.GetRequiredService<TimeProvider>();
        var storeManagerFactory = services.GetRequiredService<IStoreManagerFactory>();
        await using var storeManager = await storeManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IReplicatedDocumentStore>();
        var revokedKey = new PersistedRevokedManagementKey
        {
            KeyId = keyId,
            RevokedWhen = timeProvider.GetUtcNow(),
            Source = PersistedRevokedManagementKey.OfflineSource,
        };
        await store.AddRevokedKeyAsync(revokedKey, cancellationToken);
        await storeManager.SaveChangesAsync(cancellationToken);

        logger.RevokeManagementKeyCommandRevoked(keyId);
        return 0;
    }
}
