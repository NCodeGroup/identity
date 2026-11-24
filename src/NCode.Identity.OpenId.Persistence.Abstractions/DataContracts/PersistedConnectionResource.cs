// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Persistence.DataContracts;

/// <summary>
/// Provides the base implementation for a persisted connection resource that holds a value.
/// </summary>
/// <typeparam name="TValue">The type of the persisted connection resource.</typeparam>
[PublicAPI]
[ExcludeFromCodeCoverage]
public abstract class PersistedConnectionResource<TValue> : BasePersistedConnectionResource
{
    /// <summary>
    /// Gets or sets the value of the resource.
    /// </summary>
    public required TValue Value { get; set; }
}
