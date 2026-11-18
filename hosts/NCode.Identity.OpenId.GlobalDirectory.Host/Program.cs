// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.AspNetCore;
using NCode.Identity.AspNetCore.OpenTelemetry;
using NCode.Identity.Jose;
using NCode.Identity.Jwt;
using NCode.Identity.OpenId.Client;
using NCode.Identity.OpenId.GlobalDirectory.Host;
using NCode.Identity.OpenId.GlobalDirectory.Server;
using NCode.Identity.Secrets;
using NCode.Identity.SharedState;

// COMPOSITION-ONLY HOST: this file is a flat list of turnkey Add*/Use*/Map* calls. Reusable startup logic does NOT
// belong here — put it in the owning package as an extension. See csharp-production.instructions.md §4. The 'migrate'
// command's dispatch below is the one exception: only the entry point can return an exit code.

// OFFLINE COMMAND: 'migrate' applies the directory's migrations and exits, for a one-off Job that runs this image. See
// DirectoryMigrateCommand.
var migrate = DirectoryMigrateCommand.IsSelected(args);
var hostArguments = DirectoryMigrateCommand.GetHostArguments(args);
var builder = WebApplication.CreateBuilder(hostArguments);

var configuration = builder.Configuration;
var serviceCollection = builder.Services;

// Logs go to standard output on every platform: the console provider writes them, and the EventSource provider hands
// them to a diagnostics tool that attaches (dotnet-monitor, dotnet-trace). See project-template ADR-0115.
builder.Logging.ClearProviders().AddConsole().AddEventSourceLogger();

// Metrics: the family's meters plus ASP.NET Core, Kestrel, outbound HTTP, and the runtime, exported over OTLP when
// OTEL_EXPORTER_OTLP_ENDPOINT is configured. ADR-1197.
serviceCollection.AddOpenTelemetryMetrics(configuration);

// HTTP logging writes the request and response properties, the allow-listed headers and the duration, never a query
// string or a credential header value; the directory's routes keep their bodies confidential (ADR-1503).
serviceCollection.AddGuardedHttpLogging();

// Trust a reverse proxy's X-Forwarded-* headers, but only the proxies named in the 'ForwardedHeaders' configuration
// section.
serviceCollection.AddTrustedForwardedHeaders(configuration);

// Send Strict-Transport-Security as configured by the 'StrictTransportSecurity' configuration section. See ADR-1447.
serviceCollection.AddStrictTransportSecurity(configuration);

// Send no Server response header, in every environment. See ADR-1511.
serviceCollection.AddServerHeaderSuppression();

// Refuse to start unless Kestrel serves HTTPS only with a certificate that loads and passes the transport checks
// (ADR-1504). Inert when 'LocalDevelopment:Enabled' is set.
serviceCollection.AddTransportSecurity(configuration);

// The pods can require the reverse proxy's client certificate on the proxy-to-pod hop, as configured by the
// 'ProxyClientCertificate' section (ADR-1506). Inert unless 'ProxyClientCertificate:Required' is true.
serviceCollection.AddProxyClientCertificate(configuration);

var proxyClientCertificate = configuration.GetProxyClientCertificateOptions();
builder.WebHost.ConfigureKestrel(kestrel =>
    kestrel.ConfigureHttpsDefaults(https =>
        https.RequestProxyClientCertificate(proxyClientCertificate)
    )
);

// The directory validates a cell's bearer token against the control-plane issuer's discovery document and signing
// keys, which the OpenID client library resolves. The in-process key set is correct for one replica. The directory
// server registers its store, its endpoints' services and the 'DirectoryServer' options, which must name the issuer,
// on the client library's builder.
serviceCollection.AddSharedStateServices().AddInProcessExpiringKeySet();
serviceCollection
    .AddSecretsLibrary()
    .AddJoseLibrary()
    .AddJwtLibrary()
    .AddOpenIdClientLibrary()
    .AddOpenIdDirectoryServer(configuration);

// The directory's own Postgres database, and the readiness check that holds the replica out of rotation while it cannot
// reach the database it serves every request from. Each workload is given its own role's credentials for the one
// connection: the replicas the runtime role's, and the 'migrate' command the schema owner's.
var directoryConnectionString = DirectoryDatabase.GetConnectionString(configuration);
serviceCollection.AddDirectoryDatabase(directoryConnectionString);

// RunAsync disposes the app when the host stops or fails to start, and each command disposes it when the command ends.
// An exception before either leaves the app to the ending process, so a test host that runs this entry point reports
// that exception instead of a disposed app.
var app = builder.Build();

// The command runs against the built services, never starts the hosted services or binds a port, disposes the services
// when it ends, and returns its exit code.
if (migrate)
{
    await using (app)
        return await DirectoryMigrateCommand.RunAsync(
            app.Services,
            configuration,
            CancellationToken.None
        );
}

// MUST be the first middleware: it refuses a request whose connection does not carry the reverse proxy's client
// certificate (except the liveness and readiness probes) before anything else reads it. A no-op unless
// 'ProxyClientCertificate:Required' is true (ADR-1506).
app.UseProxyClientCertificate();

// Right after the proxy check: everything downstream that reads the scheme, host or client address observes the
// original request rather than the proxy-to-application hop.
app.UseTrustedForwardedHeaders();
app.UseStrictTransportSecurity();
app.UseLocalDevelopmentHttpsRedirection();

app.UseRouting();

// After routing, so HTTP logging sees the selected endpoint and writes a body only for one that opts in to it.
app.UseHttpLogging();

app.MapDirectory();

// Liveness runs no checks, so a dependency outage never restarts a replica.
// Readiness runs the checks tagged ready.
app.MapHealthProbes();

await app.RunAsync();

return 0;
