// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.SharedState.Options;

/// <summary>
/// Describes the deployment whose state the shared-state adapters hold. A composition root configures it; the identity
/// server names its cell. It is not bound from configuration.
/// </summary>
[PublicAPI]
public sealed class SharedStateOptions
{
    /// <summary>
    /// Gets or sets the name of the deployment whose state the adapters hold. An adapter whose server several
    /// deployments share puts it in every key and channel it writes, so the deployments never read or write one
    /// another's state. When it is empty, the keys carry no deployment.
    /// </summary>
    public string Deployment { get; set; } = string.Empty;
}
