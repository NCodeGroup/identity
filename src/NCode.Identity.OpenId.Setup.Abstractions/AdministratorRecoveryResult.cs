// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using NCode.Identity.OpenId.Management;

namespace NCode.Identity.OpenId.Setup;

/// <summary>
/// Represents the outcome of recovering an administrator through <see cref="IAdministratorRecoveryService"/>.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class AdministratorRecoveryResult
{
    /// <summary>
    /// Gets the identifier of the tenant that holds the administrator, or <c>null</c> when <see cref="Error"/> is set.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// Gets the identifier of the administrator's principal, or <c>null</c> when <see cref="Error"/> is set.
    /// </summary>
    public string? PrincipalId { get; init; }

    /// <summary>
    /// Gets a value indicating whether recovery created the administrator's account, rather than restoring an existing
    /// one.
    /// </summary>
    public bool Created { get; init; }

    /// <summary>
    /// Gets the <see cref="ManagementError"/> describing the first failed precondition, or <c>null</c> when the
    /// administrator is recovered.
    /// </summary>
    public ManagementError? Error { get; init; }

    /// <summary>
    /// Gets a value indicating whether the administrator is recovered, which is when <see cref="Error"/> is
    /// <c>null</c>.
    /// </summary>
    [MemberNotNullWhen(false, nameof(Error))]
    [MemberNotNullWhen(true, nameof(TenantId))]
    [MemberNotNullWhen(true, nameof(PrincipalId))]
    public bool Succeeded => Error is null;
}
