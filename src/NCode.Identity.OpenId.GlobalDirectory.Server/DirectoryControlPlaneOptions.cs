// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

/// <summary>
/// Configures the platform's trust anchor, the control-plane cell and its root tenant, which the directory pins. Bound
/// from the <c>ControlPlane</c> section of <see cref="DirectoryServerOptions"/>.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class DirectoryControlPlaneOptions
{
    /// <summary>
    /// Builds the configuration key of a control-plane option, which every message that names a misconfigured option
    /// uses.
    /// </summary>
    /// <param name="name">The name of the control-plane option.</param>
    /// <returns>The key, which starts with the control-plane section.</returns>
    internal static string GetConfigurationKey(string name) =>
        $"{DirectoryServerOptions.SectionName}:{nameof(DirectoryServerOptions.ControlPlane)}:{name}";

    /// <summary>
    /// Gets or sets the name of the control-plane cell. Only that cell publishes the platform document, reserves,
    /// publishes, releases or retires a team, and registers or retires other cells; it also releases, retires and
    /// changes the issuer of any tenant. It is required.
    /// </summary>
    public string Cell { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the region of the control-plane cell. It is required.
    /// </summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the kind of the control-plane cell, one of the values of <see cref="CellKinds"/>. It defaults to
    /// <see cref="CellKinds.Shared"/>.
    /// </summary>
    public string Kind { get; set; } = CellKinds.Shared;

    /// <summary>
    /// Gets or sets the identifier of the control-plane cell's root tenant, the control-plane tenant. That cell creates
    /// its root tenant without asking the directory, so the directory holds the identifier and <see cref="Issuer"/> for
    /// it, placed in <see cref="Cell"/>, and no other cell reserves either. It is required.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the issuer identifier of the control-plane tenant, which the directory holds for it. It is
    /// required.
    /// </summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the public JSON Web Key Set, as JSON text, with which the control-plane cell authenticates to the
    /// directory. It is required.
    /// </summary>
    public string ClientJwks { get; set; } = string.Empty;
}
