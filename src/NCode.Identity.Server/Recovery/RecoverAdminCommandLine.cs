// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Setup;
using NCode.Identity.Server.Logging;

namespace NCode.Identity.Server.Recovery;

/// <summary>
/// The headless <c>recover-admin</c> command of the host: it creates or restores the administrator of the control-plane
/// cell from the <c>Recovery:Admin:UserName</c>, <c>Recovery:Admin:DisplayName</c>, <c>Recovery:Admin:Email</c> and
/// <c>Recovery:Admin:Password</c> configuration through <see cref="IAdministratorRecoveryService"/>, and exits without
/// starting the server. An operator runs it as a one-off Job when every administrator's sign-in is lost.
/// </summary>
/// <remarks>
/// The exit code is <c>0</c> when the administrator is recovered, <c>1</c> when recovery is refused or fails, which the
/// command logs, and <c>2</c> for a usage error, when the configuration lacks the administrator's username or password,
/// or when the cell is not the control-plane cell.
/// </remarks>
// See ADR-1536.
[PublicAPI]
public sealed class RecoverAdminCommandLine
{
    /// <summary>
    /// The first argument that selects this command.
    /// </summary>
    public const string CommandName = "recover-admin";

    private RecoverAdminCommandLine() { }

    /// <summary>
    /// Gets the usage text of the command.
    /// </summary>
    public static string Usage { get; } =
        $"Usage: {CommandName} (the administrator comes from the {RecoveryOptions.SectionName}:Admin:UserName, "
        + $"{RecoveryOptions.SectionName}:Admin:DisplayName, {RecoveryOptions.SectionName}:Admin:Email and "
        + $"{RecoveryOptions.SectionName}:Admin:Password configuration)";

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
    public static RecoverAdminCommandLine? Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Count == 0 || !string.Equals(args[0], CommandName, StringComparison.Ordinal))
            return null;

        return args.Count == 1
            ? new RecoverAdminCommandLine()
            : new RecoverAdminCommandLine
            {
                Error =
                    $"The {CommandName} command takes no arguments; '{args[1]}' is not recognized.",
            };
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
            .CreateLogger<RecoverAdminCommandLine>();

        try
        {
            return await RecoverAsync(services, logger, cancellationToken);
        }
        // Only a cancellation the caller requested propagates; any other failure is logged and ends the command with 1,
        // because an exception that escapes Main does not end a process that runs as PID 1 in its container.
        catch (Exception exception)
            when (exception is not OperationCanceledException
                || !cancellationToken.IsCancellationRequested
            )
        {
            logger.RecoverAdminCommandFailed(exception);
            return 1;
        }
    }

    private async ValueTask<int> RecoverAsync(
        IServiceProvider services,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        if (Error is { } error)
        {
            logger.RecoverAdminCommandUsageInvalid(error, Usage);
            return 2;
        }

        // A cell with a management tenant of its own recovers its administrator through the same command once the
        // cell's management tenant exists; until then only the control-plane cell has an administrator to recover.
        var cellOptions = services.GetRequiredService<IOptions<CellOptions>>().Value;
        if (!cellOptions.IsControlPlane)
        {
            logger.RecoverAdminCommandNotOnTheControlPlane();
            return 2;
        }

        var admin = services.GetRequiredService<IOptions<RecoveryOptions>>().Value.Admin;

        var userName = admin.UserName;
        var isUserNameMissing = string.IsNullOrWhiteSpace(userName);
        if (isUserNameMissing)
            logger.RecoverAdminCommandUserNameMissing();

        var isPasswordMissing = string.IsNullOrEmpty(admin.Password);
        if (isPasswordMissing)
            logger.RecoverAdminCommandPasswordMissing();

        if (isUserNameMissing || isPasswordMissing)
            return 2;

        var recoveryService = services.GetRequiredService<IAdministratorRecoveryService>();
        var result = await recoveryService.RecoverAsync(admin, cancellationToken);
        if (!result.Succeeded)
        {
            logger.RecoverAdminCommandRefused(result.Error.StatusCode);
            return 1;
        }

        logger.RecoverAdminCommandRecovered(
            userName ?? string.Empty,
            result.TenantId,
            result.Created
        );
        return 0;
    }
}
