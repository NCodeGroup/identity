// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.Server.InstanceSetup;
using NCode.Identity.Server.Recovery;
using NCode.Identity.Server.SecretProtection;

namespace NCode.Identity.Server;

/// <summary>
/// Selects the offline command that the host's command-line arguments name: <c>secret-protection</c>, <c>setup</c>,
/// <c>revoke-management-key</c>, <c>recover-admin</c> or <c>recover-sign-in</c>.
/// </summary>
// See ADR-1408, ADR-1512 and ADR-1536.
[PublicAPI]
public static class HostCommands
{
    /// <summary>
    /// Selects the command that the first argument names.
    /// </summary>
    /// <param name="args">The command-line arguments of the host.</param>
    /// <returns>The selected command with no host arguments, or no command with the arguments unchanged when the first
    /// argument names none.</returns>
    public static HostCommandSelection Select(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        IHostCommand? command =
            SecretProtectionCommandLine.Parse(args)
            ?? InstanceSetupCommandLine.Parse(args)
            ?? RevokeManagementKeyCommandLine.Parse(args)
            ?? RecoverAdminCommandLine.Parse(args)
            ?? (IHostCommand?)RecoverSignInCommandLine.Parse(args);

        return command is null
            ? new HostCommandSelection(null, args)
            : new HostCommandSelection(command, []);
    }
}
