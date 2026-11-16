// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;

namespace NCode.Identity.OpenId.GlobalDirectory.Host.Tests;

/// <summary>
/// Hosts the directory host in-process on a <see cref="TestServer"/>. The host's database connection names a server
/// that nothing listens on, which is enough because the host opens no connection until a readiness check or a store
/// call needs one.
/// </summary>
public sealed class DirectoryHostFactory : WebApplicationFactory<DirectoryHostMarker>
{
    private const string TestIssuer = "https://control.example";
    private const string TestControlPlaneCell = "control";

    private const string TestConnectionString =
        "Host=localhost;Database=directory;Username=directory;Password=unused";

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Resolve the content root to the host project so its appsettings.json is loaded.
        builder.UseSolutionRelativeContentRoot("hosts/NCode.Identity.OpenId.GlobalDirectory.Host");

        // The host reads each value while it registers its services, so each is also a host setting.
        builder.UseSetting("LocalDevelopment:Enabled", "true");
        builder.UseSetting("ConnectionStrings:directory", TestConnectionString);
        builder.UseSetting("DirectoryServer:Issuer", TestIssuer);
        builder.UseSetting("DirectoryServer:ControlPlaneCell", TestControlPlaneCell);
    }
}
