// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace NCode.Identity.OpenId.Persistence.EntityFramework;

/// <summary>
/// Provides a seam for satellite packages to intercept the saves of the shared <see cref="OpenIdDbContext"/>, so a
/// change and what must commit with it share one transaction. The context constructor receives the registered
/// interceptors via dependency injection.
/// </summary>
/// <remarks>
/// Implementations must be registered as <b>stateless singletons</b>: every context the context factory creates shares
/// them.
/// </remarks>
[PublicAPI]
public interface IOpenIdSaveChangesInterceptor : ISaveChangesInterceptor;
