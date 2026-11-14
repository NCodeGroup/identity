// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Identifies the control-plane cell to a cell that is not the control plane. Bound from the <c>Cell:ControlPlane</c>
/// configuration section.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class ControlPlaneOptions
{
    /// <summary>
    /// Gets or sets the issuer of the control-plane cell, whose access tokens a cell that is not the control plane
    /// accepts for platform and team management. It is required when <see cref="CellOptions.IsControlPlane"/> is
    /// <c>false</c>.
    /// </summary>
    public string? Issuer { get; set; }
}
