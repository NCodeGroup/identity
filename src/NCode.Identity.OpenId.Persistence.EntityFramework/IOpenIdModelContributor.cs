// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;

namespace NCode.Identity.OpenId.Persistence.EntityFramework;

/// <summary>
/// Provides a seam for satellite packages to contribute additional entities to the shared <see cref="OpenIdDbContext"/>
/// model, so their data participates in the same unit of work and transactions as the core entities. The context
/// constructor receives the registered contributors via dependency injection.
/// </summary>
/// <remarks>
/// Implementations must be registered as <b>stateless singletons</b>: the model is built once, cached and shared by
/// every context the context factory creates, so a contributor must hold no per-instance or per-request state, must
/// configure the model deterministically, and reads the ambient tenant only through the context's query filter
/// expression.
/// </remarks>
[PublicAPI]
public interface IOpenIdModelContributor
{
    /// <summary>
    /// Configures additional entities on the model being created for the <see cref="OpenIdDbContext"/>.
    /// </summary>
    /// <param name="modelBuilder">The <see cref="ModelBuilder"/> being configured.</param>
    void Configure(ModelBuilder modelBuilder);
}
