// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.Json;

namespace NCode.Identity.OpenId.Persistence.EntityFramework;

/// <summary>
/// Provides the <see cref="IPersistedPayloadSerializer"/> that <c>AddPersistedPayloadSerializer()</c> registers, so the
/// store tests and the rows they seed write and read each stored payload as the serving code does.
/// </summary>
internal static class TestPayloadSerializer
{
    /// <summary>
    /// Gets the <see cref="IPersistedPayloadSerializer"/> that <c>AddPersistedPayloadSerializer()</c> registers.
    /// </summary>
    public static IPersistedPayloadSerializer Instance { get; } =
        new ServiceCollection()
            .AddPersistedPayloadSerializer()
            .BuildServiceProvider()
            .GetRequiredService<IPersistedPayloadSerializer>();
}
