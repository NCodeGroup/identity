// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using NCode.Identity.Secrets.Keys;
using NCode.Identity.Secrets.Persistence;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

/// <summary>
/// Provides a complete <c>DirectoryServer</c> configuration for the tests that host the directory server.
/// </summary>
internal static class DirectoryServerTestOptions
{
    public const string DirectoryIssuer = "https://directory.example";

    public const string ClientJwks = """{"keys":[]}""";

    /// <summary>
    /// Creates the configuration of a valid <c>DirectoryServer</c> section, plus <c>AllowedHosts</c>.
    /// </summary>
    public static Dictionary<string, string?> Create(
        string controlPlaneCell,
        string controlPlaneTenantId,
        string controlPlaneIssuer
    ) =>
        new()
        {
            ["DirectoryServer:Issuer"] = DirectoryIssuer,
            ["DirectoryServer:KeyRing"] = "directory",
            ["DirectoryServer:ControlPlane:Cell"] = controlPlaneCell,
            ["DirectoryServer:ControlPlane:Region"] = "region-1",
            ["DirectoryServer:ControlPlane:TenantId"] = controlPlaneTenantId,
            ["DirectoryServer:ControlPlane:Issuer"] = controlPlaneIssuer,
            ["DirectoryServer:ControlPlane:ClientJwks"] = ClientJwks,
            ["AllowedHosts"] = "localhost",
        };

    /// <summary>
    /// Reads every key set successfully, with no keys.
    /// </summary>
    public sealed class AcceptingPublicJwksReader : IPublicJwksReader
    {
        public bool TryRead(
            JsonElement jwks,
            [NotNullWhen(true)] out IReadOnlyList<SecretKey>? keys,
            [NotNullWhen(false)] out string? error
        )
        {
            keys = [];
            error = null;
            return true;
        }
    }
}
