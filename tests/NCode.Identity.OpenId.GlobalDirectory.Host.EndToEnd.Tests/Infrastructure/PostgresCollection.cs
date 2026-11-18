// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Host.EndToEnd.Tests.Infrastructure;

/// <summary>
/// Shares one <see cref="PostgresFixture"/> among every end-to-end test class.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    /// <summary>
    /// The name of the collection.
    /// </summary>
    public const string Name = "Postgres";
}
