// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Describes the cell that a host runs: a host, its database, its Valkey and its KMS keys, in one region. Bound from
/// the <c>Cell</c> configuration section.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class CellOptions
{
    /// <summary>
    /// The name of the configuration section the options bind from.
    /// </summary>
    public const string SectionName = "Cell";

    /// <summary>
    /// Gets or sets the name of the cell, which is unique across the platform and has the form
    /// <see cref="CellLabelRules"/> describes.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the region the cell runs in. Tenants of one region can move between its cells. It has the form
    /// <see cref="CellLabelRules"/> describes.
    /// </summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the kind of the cell. <see cref="CellKinds"/> names the known kinds.
    /// </summary>
    public string Kind { get; set; } = CellKinds.Shared;

    /// <summary>
    /// Gets or sets the cloud regions that the cell's infrastructure spans.
    /// </summary>
    public string[] CloudRegions { get; set; } = [];

    /// <summary>
    /// Gets or sets the absolute URL at which the cell's management API is reachable.
    /// </summary>
    public string ManagementUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the cell is the control-plane cell, which serves platform and team
    /// management and setup.
    /// </summary>
    public bool IsControlPlane { get; set; }

    /// <summary>
    /// Gets or sets the settings, bound from the <c>Cell:ControlPlane</c> section, that identify the control-plane cell
    /// to a cell that is not the control plane or that sets <c>Directory:Url</c>.
    /// </summary>
    public ControlPlaneOptions ControlPlane { get; set; } = new();
}
