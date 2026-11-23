// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.Setup;

/// <summary>
/// The validated inputs of one instance setup, which each attempt stages on a store manager of its own.
/// </summary>
internal sealed class InstanceSetupStaging
{
    /// <summary>
    /// Gets the identifier of the control-plane tenant that holds the root administrator.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Gets the issuer of the control-plane tenant, which the instance's addresses are computed from.
    /// </summary>
    public required string Issuer { get; init; }

    /// <summary>
    /// Gets the root administrator's username.
    /// </summary>
    public required string UserName { get; init; }

    /// <summary>
    /// Gets the root administrator's password.
    /// </summary>
    public required string Password { get; init; }

    /// <summary>
    /// Gets the root administrator's options.
    /// </summary>
    public required SetupRootOptions Root { get; init; }

    /// <summary>
    /// Gets the registration of the management console's client, or <see langword="null"/> when the request carries
    /// none.
    /// </summary>
    public (string ClientId, string BaseUrl, string Jwks)? ManagementConsole { get; init; }

    /// <summary>
    /// Gets the registration of the automation client, or <see langword="null"/> when the request carries none.
    /// </summary>
    public (string ClientId, string Jwks)? Automation { get; init; }
}
