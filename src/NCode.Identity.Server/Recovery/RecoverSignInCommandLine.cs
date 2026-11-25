// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Setup;
using NCode.Identity.Server.Logging;

namespace NCode.Identity.Server.Recovery;

/// <summary>
/// The headless <c>recover-sign-in</c> command of the host: it repairs a sign-in that a setting blocks through
/// <see cref="ISignInRecoveryService"/>, removing the sign-in-critical settings' values and enabling the default local
/// connection, the management console's client and the management resource server, and exits without starting the
/// server. An operator runs it as a one-off Job when a setting refuses every sign-in.
/// </summary>
/// <remarks>
/// The exit code is <c>0</c> when the repair completes, <c>1</c> when it fails, which the command logs, and <c>2</c>
/// for a usage error or when the cell is a workload cell without a management tenant.
/// </remarks>
// See ADR-1536.
[PublicAPI]
public sealed class RecoverSignInCommandLine : IHostCommand
{
    /// <summary>
    /// The first argument that selects this command.
    /// </summary>
    public const string CommandName = "recover-sign-in";

    private RecoverSignInCommandLine() { }

    /// <summary>
    /// Gets the usage text of the command.
    /// </summary>
    public static string Usage { get; } = $"Usage: {CommandName} (no arguments)";

    /// <summary>
    /// Gets the reason the arguments are invalid, or <see langword="null"/> when they are. A command with an error logs
    /// it with the <see cref="Usage"/> when it runs and exits with <c>2</c>.
    /// </summary>
    public string? Error { get; private init; }

    /// <summary>
    /// Parses the command line of the host.
    /// </summary>
    /// <param name="args">The command-line arguments of the host.</param>
    /// <returns>The command, or <see langword="null"/> when the arguments do not start with <see cref="CommandName"/>.
    /// A command whose <see cref="Error"/> is set names what is wrong with the arguments.</returns>
    public static RecoverSignInCommandLine? Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Count == 0 || !string.Equals(args[0], CommandName, StringComparison.Ordinal))
            return null;

        return args.Count == 1
            ? new RecoverSignInCommandLine()
            : new RecoverSignInCommandLine
            {
                Error =
                    $"The {CommandName} command takes no arguments; '{args[1]}' is not recognized.",
            };
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
            .CreateLogger<RecoverSignInCommandLine>();

        try
        {
            return await RepairAsync(services, logger, cancellationToken);
        }
        // Only a cancellation the caller requested propagates; any other failure is logged and ends the command with 1,
        // because an exception that escapes Main does not end a process that runs as PID 1 in its container.
        catch (Exception exception)
            when (exception is not OperationCanceledException
                || !cancellationToken.IsCancellationRequested
            )
        {
            logger.RecoverSignInCommandFailed(exception);
            return 1;
        }
    }

    private async ValueTask<int> RepairAsync(
        IServiceProvider services,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        if (Error is { } error)
        {
            logger.RecoverSignInCommandUsageInvalid(error, Usage);
            return 2;
        }

        var topology = services.GetRequiredService<IPlatformTopology>();
        if (!topology.IsControlPlaneCell && !topology.HasManagementTenant)
        {
            logger.RecoverSignInCommandNoManagementTenant();
            return 2;
        }

        var recoveryService = services.GetRequiredService<ISignInRecoveryService>();
        var result = await recoveryService.RepairAsync(cancellationToken);

        foreach (var change in result.Changes)
            logger.RecoverSignInCommandChanged(change.Scope, change.Name, change.Before);

        logger.RecoverSignInCommandRepaired(result.Changes.Count);
        return 0;
    }
}
