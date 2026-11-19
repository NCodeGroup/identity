// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Configuration;

/// <summary>
/// Provides a value generator for surrogate keys that mints each key with <see cref="ISurrogateKeyGenerator"/>,
/// resolved from the context's service provider when the key is generated.
/// </summary>
internal sealed class SurrogateKeyValueGenerator : ValueGenerator<Guid>
{
    /// <inheritdoc />
    public override bool GeneratesTemporaryValues => false;

    /// <inheritdoc />
    public override Guid Next(EntityEntry entry) =>
        entry.Context.GetService<ISurrogateKeyGenerator>().CreateKey();
}
