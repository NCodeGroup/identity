// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NCode.Buffers;
using NCode.Identity.OpenId.GlobalDirectory.Server;

namespace NCode.Identity.OpenId.GlobalDirectory.Host.Tests;

/// <summary>
/// Hosts the directory host in-process on a <see cref="TestServer"/>. The host's database is one open in-memory Sqlite
/// connection that holds the directory's schema, and its key ring holds one generated EC P-256 key.
/// </summary>
public sealed class DirectoryHostFactory : WebApplicationFactory<DirectoryHostMarker>
{
    private const string TestIssuer = "https://directory.example";
    private const string TestControlPlaneCell = "control";
    private const string TestControlPlaneRegion = "region-1";
    private const string TestControlPlaneKind = "primary";
    private const string TestControlPlaneTenantId = "root";
    private const string TestControlPlaneIssuer = "https://control.example";
    private const string TestKeyRing = "directory";

    private const string TestConnectionString =
        "Host=localhost;Database=directory;Username=directory;Password=unused";

    private SqliteConnection Connection { get; } = new("DataSource=:memory:");

    private string KeyDirectory { get; } =
        Path.Combine(Path.GetTempPath(), "directory-host-" + Guid.NewGuid().ToString("N"));

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Connection.Open();
        var schemaOptions = new DbContextOptionsBuilder<DirectoryDbContext>()
            .UseSqlite(Connection)
            .Options;
        using (var schemaContext = new DirectoryDbContext(schemaOptions))
        {
            schemaContext.Database.EnsureCreated();
        }

        Directory.CreateDirectory(KeyDirectory);
        var keyPath = Path.Combine(KeyDirectory, "directory.pem");
        using (var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            var pem = signingKey.ExportPkcs8PrivateKeyPem();
            File.WriteAllBytes(keyPath, SecureEncoding.UTF8.GetBytes(pem));
        }

        string clientJwks;
        using (var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            var point = clientKey.ExportParameters(includePrivateParameters: false).Q;
            var x = Base64Url.EncodeToString(point.X!);
            var y = Base64Url.EncodeToString(point.Y!);
            clientJwks =
                $$"""{"keys":[{"kty":"EC","crv":"P-256","x":"{{x}}","y":"{{y}}","kid":"control-1","use":"sig","alg":"ES256"}]}""";
        }

        // Resolve the content root to the host project so its appsettings.json is loaded.
        builder.UseSolutionRelativeContentRoot("hosts/NCode.Identity.OpenId.GlobalDirectory.Host");

        // The host reads each value while it registers its services, so each is also a host setting.
        builder.UseSetting("LocalDevelopment:Enabled", "true");
        builder.UseSetting("ConnectionStrings:directory", TestConnectionString);
        builder.UseSetting($"KeyRings:{TestKeyRing}:PemFiles:0", keyPath);
        builder.UseSetting("DirectoryServer:Issuer", TestIssuer);
        builder.UseSetting("DirectoryServer:KeyRing", TestKeyRing);
        builder.UseSetting("DirectoryServer:ControlPlane:Cell", TestControlPlaneCell);
        builder.UseSetting("DirectoryServer:ControlPlane:Region", TestControlPlaneRegion);
        builder.UseSetting("DirectoryServer:ControlPlane:Kind", TestControlPlaneKind);
        builder.UseSetting("DirectoryServer:ControlPlane:TenantId", TestControlPlaneTenantId);
        builder.UseSetting("DirectoryServer:ControlPlane:Issuer", TestControlPlaneIssuer);
        builder.UseSetting("DirectoryServer:ControlPlane:ClientJwks", clientJwks);

        builder.ConfigureTestServices(services =>
        {
            var registrations = services
                .Where(descriptor => TouchesDirectoryDbContext(descriptor.ServiceType))
                .ToList();
            foreach (var registration in registrations)
                services.Remove(registration);

            services.AddDbContextFactory<DirectoryDbContext>(options =>
                options.UseSqlite(Connection)
            );
        });
    }

    private static bool TouchesDirectoryDbContext(Type serviceType) =>
        serviceType == typeof(DirectoryDbContext)
        || serviceType == typeof(DbContextOptions)
        || (
            serviceType.IsGenericType
            && serviceType.GetGenericArguments().Contains(typeof(DirectoryDbContext))
        );

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        Connection.Dispose();
        if (Directory.Exists(KeyDirectory))
            Directory.Delete(KeyDirectory, recursive: true);
    }
}
