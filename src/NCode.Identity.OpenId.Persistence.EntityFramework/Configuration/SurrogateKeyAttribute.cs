// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Configuration;

/// <summary>
/// Marks an entity's surrogate key, a <see cref="Guid"/> holding a UUIDv7 that the context mints through
/// <see cref="NCode.Identity.Persistence.ISurrogateKeyGenerator"/> when an entity is added without one and that never
/// leaves the persistence layer.
/// </summary>
[PublicAPI]
[AttributeUsage(AttributeTargets.Property)]
public sealed class SurrogateKeyAttribute : Attribute
{
    // nothing
}
