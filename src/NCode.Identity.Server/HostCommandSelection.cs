// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.Server;

/// <summary>
/// The offline command that the host's command-line arguments select, and the arguments that the host builds with.
/// </summary>
/// <param name="Command">The selected command, or <see langword="null"/> when the arguments select none.</param>
/// <param name="HostArguments">The arguments for the host builder: none when a command is selected, because a
/// command-line configuration provider would read the command's own arguments as configuration, and the arguments
/// otherwise.</param>
[PublicAPI]
public sealed record HostCommandSelection(IHostCommand? Command, string[] HostArguments);
