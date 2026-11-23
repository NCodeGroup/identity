// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Connections;

/// <summary>
/// A kind of connection, such as <c>local</c>, that a tenant's connections select through their strategy name.
/// </summary>
/// <remarks>
/// This is a strategy collection: implementations are registered in DI and resolved by <see cref="Name"/> through
/// <see cref="IConnectionStrategyCatalog"/>. The vocabulary is open, so a consumer or sibling package adds a strategy by
/// registering one rather than by editing the library.
/// </remarks>
// See ADR-1535 and ADR-1150.
[PublicAPI]
public interface IConnectionStrategy
{
    /// <summary>
    /// Gets the name of the strategy, which is the value a connection stores as its strategy. Names are compared
    /// ordinally.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the sign-in method that connections of the strategy offer. The vocabulary is open;
    /// <see cref="ConnectionSignInMethods.Password"/> means the server verifies a password, so the interactive form and
    /// the password grant serve the connection.
    /// </summary>
    string SignInMethod { get; }

    /// <summary>
    /// Validates the options document of a connection of the strategy.
    /// </summary>
    /// <param name="options">The options document, a JSON object.</param>
    /// <returns>A description of why the options are invalid, or <see langword="null"/> when they are valid.</returns>
    string? ValidateOptions(JsonElement options);
}
