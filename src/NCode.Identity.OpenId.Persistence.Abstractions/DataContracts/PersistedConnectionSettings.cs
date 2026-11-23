// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using JetBrains.Annotations;
using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.Persistence.DataContracts;

/// <summary>
/// Contains the settings document of a connection: a JSON object, which is empty when the connection has no settings.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class PersistedConnectionSettings : PersistedConnectionResource<JsonElement>
{
    /// <inheritdoc/>
    [MaxLength(MaxLengths.ResourceType)]
    public override string ResourceType =>
        $"{ResourceTypePrefix}{ResourceTypes.Separator}{OpenIdResourceTypes.Settings}";
}
