// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.Secrets.Options;

/// <summary>
/// Describes where a key management service may hold the deployment's keys. A composition root configures it; the
/// identity server names the cloud regions of its cell. It is not bound from configuration.
/// </summary>
[PublicAPI]
public sealed class KeyResidencyOptions
{
    /// <summary>
    /// Gets or sets the cloud regions, such as <c>us-west-2</c>, in which a key management service may hold the
    /// deployment's keys. An adapter whose keys name a region or location replaces its own allowed list with them, so
    /// it refuses a key held anywhere else. When it is empty, each adapter keeps the list it is configured with.
    /// </summary>
    public List<string> CloudRegions { get; set; } = [];
}
