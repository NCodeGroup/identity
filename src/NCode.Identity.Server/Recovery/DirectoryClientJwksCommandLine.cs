// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using NCode.Identity.Jose;
using NCode.Identity.Jwk;
using NCode.Identity.Jwt;
using NCode.Identity.OpenId.Client;
using NCode.Identity.OpenId.Client.KeyRings;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.Registration;
using NCode.Identity.Secrets;
using NCode.Identity.Secrets.KeyTypes;
using NCode.Identity.Secrets.Persistence;
using NCode.Identity.Server.Logging;

namespace NCode.Identity.Server.Recovery;

/// <summary>
/// The headless <c>directory-client-jwks</c> command of the host: it writes the public keys of a key ring as a JSON Web
/// Key Set to standard output and exits without starting the server. An operator runs it to register a cell's keys with
/// the directory, which needs no database. Its logs go to standard error, so standard output holds only the key set.
/// </summary>
/// <remarks>
/// The exit code is <c>0</c> when the key set is written, <c>1</c> when the command fails, which it logs, and <c>2</c>
/// for a usage error or when no key ring is named or the named key ring is unknown.
/// </remarks>
// See ADR-1536.
[PublicAPI]
public sealed class DirectoryClientJwksCommandLine
{
    /// <summary>
    /// The first argument that selects this command.
    /// </summary>
    public const string CommandName = "directory-client-jwks";

    /// <summary>
    /// The option that names the key ring.
    /// </summary>
    public const string KeyRingOption = "--key-ring";

    private DirectoryClientJwksCommandLine() { }

    /// <summary>
    /// Gets the usage text of the command.
    /// </summary>
    public static string Usage { get; } =
        $"Usage: {CommandName} [{KeyRingOption} <name>] (the key ring defaults to the Directory:KeyRing setting)";

    /// <summary>
    /// Gets the name of the key ring that the arguments name, or <see langword="null"/> when they name none and the
    /// <c>Directory:KeyRing</c> setting applies.
    /// </summary>
    public string? KeyRing { get; private init; }

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
    public static DirectoryClientJwksCommandLine? Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Count == 0 || !string.Equals(args[0], CommandName, StringComparison.Ordinal))
            return null;

        if (args.Count == 1)
            return new DirectoryClientJwksCommandLine();

        if (!string.Equals(args[1], KeyRingOption, StringComparison.Ordinal))
        {
            return new DirectoryClientJwksCommandLine
            {
                Error = $"The {CommandName} command does not recognize '{args[1]}'.",
            };
        }

        if (args.Count == 2 || string.IsNullOrWhiteSpace(args[2]))
        {
            return new DirectoryClientJwksCommandLine
            {
                Error = $"The {KeyRingOption} option needs the name of a key ring.",
            };
        }

        return args.Count > 3
            ? new DirectoryClientJwksCommandLine
            {
                Error = $"The {CommandName} command does not recognize '{args[3]}'.",
            }
            : new DirectoryClientJwksCommandLine { KeyRing = args[2] };
    }

    /// <summary>
    /// Runs the command in a host of its own, which composes only what a key ring needs: the secrets library with its
    /// key formats, the JOSE and JWT libraries and the protocol client's key rings from the <c>KeyRings</c> section.
    /// The command needs no database, root key provider or allowed hosts, so it runs before any deployment exists. Its
    /// logs go to standard error, so standard output holds only the key set.
    /// </summary>
    /// <param name="args">The command-line arguments of the host, which select this command.</param>
    /// <param name="output">The writer of standard output that receives the key set.</param>
    /// <param name="configureSecrets">An optional action that registers more of the secrets library, such as the remote
    /// key providers of the key management services that can hold a key ring's keys, given the host's
    /// configuration.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the command.</param>
    /// <returns>The <see cref="ValueTask{TResult}"/> that represents the command, containing its exit code.</returns>
    public static async ValueTask<int> RunStandaloneAsync(
        string[] args,
        TextWriter output,
        Action<IServiceBuilder<SecretsLibrary>, IConfiguration>? configureSecrets,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);

        var command = Parse(args);
        if (command is null)
            throw new ArgumentException(
                $"The arguments do not select the {CommandName} command.",
                nameof(args)
            );

        // A command-line configuration provider would read the command's arguments as configuration.
        var hostArguments = GetHostArguments(args);
        var builder = Host.CreateApplicationBuilder(hostArguments);
        builder.Logging.ClearProviders().AddConsole();
        builder.Logging.Services.Configure<ConsoleLoggerOptions>(options =>
            options.LogToStandardErrorThreshold = LogLevel.Trace
        );

        var secretsLibrary = builder.Services.AddSecretsLibrary();
        secretsLibrary.AddSecretKeyFormats();
        configureSecrets?.Invoke(secretsLibrary, builder.Configuration);
        var clientLibrary = secretsLibrary
            .AddJoseLibrary()
            .AddJwtLibrary()
            .AddOpenIdClientLibrary();
        var keyRingsSection = builder.Configuration.GetSection(
            OpenIdClientDefaults.KeyRingsSectionName
        );
        clientLibrary.AddOpenIdClientKeyRings(keyRingsSection);

        // Starting the host loads the key rings, which the protocol client's hosted service reads from their sources.
        using var host = builder.Build();
        await host.StartAsync(cancellationToken);
        try
        {
            return await command.RunAsync(host.Services, output, cancellationToken);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    private static string[] GetHostArguments(string[] args)
    {
        return HostCommandLine.GetHostArguments(args, CommandName);
    }

    /// <summary>
    /// Runs the command against the services of a built host, without starting the host.
    /// </summary>
    /// <param name="services">The services of the built host.</param>
    /// <param name="output">The writer of standard output that receives the key set.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the command.</param>
    /// <returns>The <see cref="ValueTask{TResult}"/> that represents the command, containing its exit code.</returns>
    public async ValueTask<int> RunAsync(
        IServiceProvider services,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(output);

        var logger = services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger<DirectoryClientJwksCommandLine>();

        try
        {
            return await WriteAsync(services, logger, output, cancellationToken);
        }
        // Only a cancellation the caller requested propagates; any other failure is logged and ends the command with 1,
        // because an exception that escapes Main does not end a process that runs as PID 1 in its container.
        catch (Exception exception)
            when (exception is not OperationCanceledException
                || !cancellationToken.IsCancellationRequested
            )
        {
            logger.DirectoryClientJwksCommandFailed(exception);
            return 1;
        }
    }

    private async ValueTask<int> WriteAsync(
        IServiceProvider services,
        ILogger logger,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        if (Error is { } error)
        {
            logger.DirectoryClientJwksCommandUsageInvalid(error, Usage);
            return 2;
        }

        var keyRingName = KeyRing;
        if (string.IsNullOrWhiteSpace(keyRingName))
        {
            var options = services.GetRequiredService<IOptions<DirectoryClientOptions>>();
            keyRingName = options.Value.KeyRing;
        }

        if (string.IsNullOrWhiteSpace(keyRingName))
        {
            logger.DirectoryClientJwksCommandNoKeyRing(Usage);
            return 2;
        }

        var keyRingProvider = services.GetRequiredService<IClientKeyRingProvider>();
        if (!keyRingProvider.HasKeyRing(keyRingName))
        {
            logger.DirectoryClientJwksCommandUnknownKeyRing(keyRingName);
            return 2;
        }

        // Each key's descriptor exports its public JWK; a key with no public part, such as a shared secret, is omitted.
        var catalog = services.GetRequiredService<ISecretKeyTypeCatalog>();
        var keys = new List<JsonWebKey>();
        foreach (var secretKey in keyRingProvider.GetKeyRing(keyRingName))
        {
            if (
                catalog.TryGet(secretKey.KeyType, out var descriptor)
                && descriptor.ExportPublicKey(secretKey) is { } exported
            )
                keys.Add(exported.Jwk);
        }

        var json = new JsonWebKeySet(keys).ToJsonString();
        var memory = json.AsMemory();
        await output.WriteLineAsync(memory, cancellationToken);
        await output.FlushAsync(cancellationToken);

        logger.DirectoryClientJwksCommandWritten(keyRingName, keys.Count);
        return 0;
    }
}
