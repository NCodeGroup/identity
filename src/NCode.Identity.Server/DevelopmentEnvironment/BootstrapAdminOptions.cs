// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.Server.DevelopmentEnvironment;

/// <summary>
/// Contains the options for the bootstrap administrator credential: a cold-start client, supplied out of band through
/// configuration, that holds a persisted <c>GlobalAdmin</c> role assignment at the platform node of the control-plane
/// tenant so an operator can make the first authorized management call and provision durable administrators. The
/// feature is opt-in: when <see cref="ClientId"/> and <see cref="ClientSecret"/> are absent, nothing is seeded and no
/// role is assigned.
/// </summary>
[PublicAPI]
public class BootstrapAdminOptions
{
    /// <summary>
    /// Gets the default configuration section name that holds the bootstrap administrator options.
    /// </summary>
    public const string DefaultSectionName = "BootstrapAdmin";

    /// <summary>
    /// Gets or sets the client identifier of the bootstrap administrator client.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Gets or sets the Base64Url-encoded client secret of the bootstrap administrator client. It is supplied encoded
    /// (rather than as plaintext) so a raw process-memory snapshot does not expose it directly; the handler decodes it
    /// into a pinned, zeroed buffer, protects it before it is persisted, and never logs it. The client registers
    /// <c>client_secret_basic</c>, so it presents the decoded secret with HTTP Basic authentication.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Gets the identifier of the tenant the bootstrap administrator client is seeded into and holds its platform
    /// <c>GlobalAdmin</c> assignment in. It is required when the bootstrap administrator is configured, and it must
    /// name the control-plane tenant: the static tenant of a single-tenant host, or the root tenant of a multi-tenant
    /// one. A default of every tenant would give each tenant created at runtime a GlobalAdmin credential.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Gets a value indicating whether the bootstrap administrator is configured (both a client id and secret are
    /// present), which both enables seeding and assigns the <c>GlobalAdmin</c> role to the client.
    /// </summary>
    public bool IsEnabled => !string.IsNullOrEmpty(ClientId) && !string.IsNullOrEmpty(ClientSecret);
}
