// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.GlobalDirectory.Host.Logging;

namespace NCode.Identity.OpenId.GlobalDirectory.Host;

/// <summary>
/// The <c>migrate</c> command of the directory host: it applies the directory's migrations, grants the runtime role the
/// data access the replicas need, and exits without starting the server. A one-off Kubernetes Job runs it from the
/// host's own image.
/// </summary>
/// <remarks>
/// The command is selected by an argument list that is exactly <see cref="CommandName"/>. It runs against the built
/// host's services, never starts the hosted services or binds a port, and returns its exit code. It reads the one
/// connection the replicas read, given the schema owner's credentials, and grants the runtime role that
/// <c>DirectoryMigration:RuntimeRole</c> names.
/// </remarks>
internal static class DirectoryMigrateCommand
{
    /// <summary>
    /// The argument that selects the command, <c>migrate</c>.
    /// </summary>
    public const string CommandName = "migrate";

    /// <summary>
    /// Determines whether the host's arguments select the command.
    /// </summary>
    /// <param name="args">The command-line arguments of the host.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="args"/> is exactly <see cref="CommandName"/>, compared with ordinal
    /// rules; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool IsSelected(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return args is [CommandName];
    }

    /// <summary>
    /// Gets the arguments that the host builds with: none when they select the command, because a command-line
    /// configuration provider would read the argument as configuration, and the arguments otherwise.
    /// </summary>
    /// <param name="args">The command-line arguments of the host.</param>
    /// <returns>The arguments for the host builder.</returns>
    public static string[] GetHostArguments(string[] args)
    {
        return IsSelected(args) ? [] : args;
    }

    /// <summary>
    /// Runs the command against the services of a built host, without starting the host. It migrates the schema, then
    /// grants the runtime role the data access the replicas need.
    /// </summary>
    /// <param name="services">The services of the built host.</param>
    /// <param name="configuration">
    /// The host's configuration, which names the runtime role in <c>DirectoryMigration:RuntimeRole</c>.
    /// </param>
    /// <param name="cancellationToken">
    /// The <see cref="CancellationToken"/> that may be used to cancel the command.
    /// </param>
    /// <returns>
    /// The <see cref="ValueTask{TResult}"/> that represents the command, containing its exit code: <c>0</c> when the
    /// database is migrated and <c>1</c> when the command fails, which it logs.
    /// </returns>
    public static async ValueTask<int> RunAsync(
        IServiceProvider services,
        IConfiguration configuration,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var logger = services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(DirectoryMigrateCommand));

        try
        {
            var runtimeRole = DirectoryDatabase.GetRuntimeRole(configuration);
            await DirectoryDatabase.MigrateAsync(services, runtimeRole, cancellationToken);
            return 0;
        }
        // An exception that escapes Main leaves the end of the process to the runtime's crash path, which does not end
        // a process that runs as PID 1 in its container (dotnet/runtime issue 112580), so a Job would run until its
        // deadline.
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.DirectoryMigrateCommandFailed(exception);
            return 1;
        }
    }
}
