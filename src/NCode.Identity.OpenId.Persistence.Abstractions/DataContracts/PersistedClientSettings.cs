// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using JetBrains.Annotations;
using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.Persistence.DataContracts;

/// <summary>
/// Contains the settings document of a client: a JSON object, which is empty when the client has no settings.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class PersistedClientSettings : PersistedClientResource<JsonElement>
{
    /// <inheritdoc/>
    [MaxLength(MaxLengths.ResourceType)]
    public override string ResourceType =>
        $"{ResourceTypePrefix}{ResourceTypes.Separator}{OpenIdResourceTypes.Settings}";
}
