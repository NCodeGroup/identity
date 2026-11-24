// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.Persistence.DataContracts;

/// <summary>
/// Common interface for all persisted connection resources.
/// </summary>
[PublicAPI]
public interface ISupportPersistedConnectionResource
    : ISupportResource,
        ISupportConnectionId,
        ISupportConcurrencyToken,
        ISupportTenantId
{
    // nothing
}
