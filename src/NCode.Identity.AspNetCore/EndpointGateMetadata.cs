// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.AspNetCore;

/// <summary>
/// Endpoint metadata that names one <see cref="IEndpointGate"/>. An endpoint runs its gates in the order its metadata
/// lists them.
/// </summary>
/// <param name="create">The factory that creates the gate from the services of a request.</param>
// See ADR-1520.
internal sealed class EndpointGateMetadata(Func<IServiceProvider, IEndpointGate> create)
{
    /// <summary>
    /// Gets the factory that creates the gate from the services of a request.
    /// </summary>
    public Func<IServiceProvider, IEndpointGate> Create { get; } = create;
}
