// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace NCode.Identity.Frontend.EndToEnd.Tests.Infrastructure;

/// <summary>
/// Shares one <see cref="FrontendEnvironment"/> among every pages test class.
/// </summary>
[CollectionDefinition(Name)]
public sealed class FrontendCollection : ICollectionFixture<FrontendEnvironment>
{
    /// <summary>
    /// The name of the collection.
    /// </summary>
    public const string Name = "Frontend end-to-end";
}
