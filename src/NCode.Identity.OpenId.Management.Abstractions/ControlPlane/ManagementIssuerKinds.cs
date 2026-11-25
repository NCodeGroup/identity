// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.ControlPlane;

/// <summary>
/// Defines the kinds of issuer whose management tokens a cell accepts, and the claim that records the kind on the
/// authenticated identity of a request.
/// </summary>
[PublicAPI]
public static class ManagementIssuerKinds
{
    /// <summary>
    /// The kind of a token that the control-plane tenant issued, which authorizes through the replicated authority.
    /// </summary>
    public const string ControlPlane = "control-plane";

    /// <summary>
    /// The kind of a token that the cell's own management tenant issued, which authorizes only through the assignments
    /// that the cell stores.
    /// </summary>
    public const string Cell = "cell";

    /// <summary>
    /// The type of the claim that carries the kind of the issuer on the authenticated identity.
    /// </summary>
    public const string ClaimType = "ncode_management_issuer";
}
