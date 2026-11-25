// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Setup;

/// <summary>
/// Creates or restores the administrator of the control-plane tenant when its sign-in is lost: the local account is
/// created or re-enabled, unlocked and given a new password, and holds <c>GlobalAdmin</c> at the platform node.
/// Register a custom implementation to replace or extend the default behavior.
/// </summary>
[PublicAPI]
public interface IAdministratorRecoveryService
{
    /// <summary>
    /// Creates or restores the administrator in one unit of work and records an audit event for each change.
    /// </summary>
    /// <param name="options">The <see cref="RecoveryAdminOptions"/> describing the administrator.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>An <see cref="AdministratorRecoveryResult"/> carrying the recovered administrator, or the error
    /// describing the first failed precondition.</returns>
    ValueTask<AdministratorRecoveryResult> RecoverAsync(
        RecoveryAdminOptions options,
        CancellationToken cancellationToken
    );
}
