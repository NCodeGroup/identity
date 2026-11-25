// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Setup;

/// <summary>
/// Repairs a sign-in that a setting blocks: the values of the sign-in-critical settings that can refuse every sign-in
/// are removed, and the default local connection, the management console's client and the management resource server
/// are enabled. Register a custom implementation to replace or extend the default behavior.
/// </summary>
[PublicAPI]
public interface ISignInRecoveryService
{
    /// <summary>
    /// Repairs the anchor tenant in one unit of work and records an audit event for each resource it changes. The
    /// control-plane cell also resets the platform's values of the sign-in-critical settings. A healthy tenant has no
    /// change.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="SignInRepairResult"/> that lists each change.</returns>
    ValueTask<SignInRepairResult> RepairAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Defines the scopes that a <see cref="SignInRepairChange"/> names.
/// </summary>
[PublicAPI]
public static class SignInRepairScopes
{
    /// <summary>
    /// A sign-in-critical setting of the platform.
    /// </summary>
    public const string Platform = "platform";

    /// <summary>
    /// A sign-in-critical setting of the anchor tenant.
    /// </summary>
    public const string Tenant = "tenant";

    /// <summary>
    /// A connection of the anchor tenant that was disabled.
    /// </summary>
    public const string Connection = "connection";

    /// <summary>
    /// A client of the anchor tenant that was disabled.
    /// </summary>
    public const string Client = "client";

    /// <summary>
    /// A resource server of the anchor tenant that was disabled.
    /// </summary>
    public const string ResourceServer = "resource-server";
}

/// <summary>
/// Represents one change that <see cref="ISignInRecoveryService.RepairAsync"/> made.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed record SignInRepairChange
{
    /// <summary>
    /// Gets the scope of the change, one of the <see cref="SignInRepairScopes"/> values.
    /// </summary>
    public required string Scope { get; init; }

    /// <summary>
    /// Gets the name of the setting that was removed, or the name or identifier of the resource that was enabled.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the value before the change as JSON text, or <c>null</c> when the change enabled a resource.
    /// </summary>
    public string? Before { get; init; }
}

/// <summary>
/// Represents the outcome of <see cref="ISignInRecoveryService.RepairAsync"/>.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class SignInRepairResult
{
    /// <summary>
    /// Gets the changes that the repair made, which is empty when nothing blocked a sign-in.
    /// </summary>
    public IReadOnlyList<SignInRepairChange> Changes { get; init; } = [];
}
