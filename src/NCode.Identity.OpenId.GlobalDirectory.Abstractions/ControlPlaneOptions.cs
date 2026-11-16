// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Identifies the control-plane cell to a cell that is not the control plane or that sets <c>Directory:Url</c>. Bound
/// from the <c>Cell:ControlPlane</c> configuration section.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class ControlPlaneOptions
{
    /// <summary>
    /// Gets or sets the issuer of the control-plane cell. A cell that sets <c>Directory:Url</c>, the control-plane
    /// cell included, asks this issuer for the access tokens it presents to the directory; such a cell that is not the
    /// control plane also validates management tokens against this issuer and asks the control-plane cell for the
    /// roles of a caller. It is required when <see cref="CellOptions.IsControlPlane"/> is <c>false</c> or
    /// <c>Directory:Url</c> is set.
    /// </summary>
    public string? Issuer { get; set; }
}
