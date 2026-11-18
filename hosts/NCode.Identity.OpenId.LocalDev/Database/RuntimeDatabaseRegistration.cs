// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;

namespace NCode.Identity.OpenId.LocalDev.Database;

/// <summary>
/// Hands an OpenID host the parts of the runtime role's database connection.
/// </summary>
internal static class RuntimeDatabaseRegistration
{
    extension(IResourceBuilder<ProjectResource> project)
    {
        /// <summary>
        /// Hands the host the parts of the dev database's connection as the runtime role; the host assembles the
        /// connection from them with <c>NpgsqlConnectionStringBuilder</c>.
        /// </summary>
        /// <param name="endpoint">The endpoint of the Postgres server.</param>
        /// <param name="password">The role's password; it is URL-safe Base64 (<c>A-Z a-z 0-9 - _</c>).</param>
        /// <returns>The same resource builder, to chain further calls.</returns>
        /// <exception cref="ArgumentException">The password is not valid.</exception>
        [ExcludeFromCodeCoverage(Justification = "Aspire resource wiring.")]
        internal IResourceBuilder<ProjectResource> WithRuntimeDatabase(
            EndpointReference endpoint,
            string password
        )
        {
            ArgumentNullException.ThrowIfNull(project);
            ArgumentNullException.ThrowIfNull(endpoint);
            ArgumentNullException.ThrowIfNull(password);

            project.WithEnvironment(
                $"{LocalDatabase.ConnectionPartsSection}__Host",
                ReferenceExpression.Create($"{endpoint.Property(EndpointProperty.Host)}")
            );
            project.WithEnvironment(
                $"{LocalDatabase.ConnectionPartsSection}__Port",
                ReferenceExpression.Create($"{endpoint.Property(EndpointProperty.Port)}")
            );

            foreach (var (name, value) in LocalDatabase.RuntimeDatabaseParts(password))
            {
                project.WithEnvironment(name, value);
            }

            return project;
        }
    }
}
