// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using NCode.Identity.Persistence;
using NCode.Identity.Registration;

namespace NCode.Identity.OpenId.Host.Database;

/// <summary>
/// Resolves the name of the Postgres role that the reference OP host runs as, from configuration. The migration worker
/// grants the role its data access after it migrates, and the role name is judged in one place.
/// </summary>
/// <remarks>
/// The role is configured under <c>HostDatabase:RuntimeRole</c>. It is required, and it is a valid name according to
/// <see cref="DatabaseRoleNameRules"/>, so it is safe to quote as an identifier in a statement. The <c>HostDatabase</c>
/// section binds strictly to <see cref="HostDatabaseSettings"/>, so a key that is not one of its properties stops the
/// host.
/// </remarks>
// See ADR-1018. The migration worker is the MigrationService project.
[PublicAPI]
public static class HostDatabaseRuntimeRole
{
    /// <summary>
    /// The name of the configuration section that holds the runtime role, <c>HostDatabase</c>.
    /// </summary>
    public const string SectionName = "HostDatabase";

    /// <summary>
    /// Resolves the runtime role name from <paramref name="configuration"/>.
    /// </summary>
    /// <param name="configuration">The configuration to read.</param>
    /// <returns>The trimmed role name.</returns>
    /// <exception cref="InvalidOperationException">
    /// The section holds a key that is not one of the <see cref="HostDatabaseSettings"/> properties, or the role is
    /// missing, empty or not a valid role name. The message names <c>HostDatabase:RuntimeRole</c> and never holds the
    /// configured value.
    /// </exception>
    public static string Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var settings = new HostDatabaseSettings();
        configuration.GetSection(SectionName).BindStrictly(settings);

        var key = $"{SectionName}:{nameof(HostDatabaseSettings.RuntimeRole)}";

        return DatabaseRoleNameRules.RequireRuntimeRole(
            settings.RuntimeRole,
            key,
            "the host runs as"
        );
    }
}
