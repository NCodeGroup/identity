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
/// <see cref="IConnectionStrategyCatalog"/>. The vocabulary is open, so a consumer or sibling package adds a strategy
/// by registering one rather than by editing the library.
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

    /// <summary>
    /// Validates a change of the options document of an existing connection of the strategy, so that a change that
    /// would move the connection's dependents out of reach is refused. It is called after <see cref="ValidateOptions"/>
    /// accepts the proposed options.
    /// </summary>
    /// <param name="current">The options document the connection has, a JSON object.</param>
    /// <param name="proposed">The options document the connection would have, a JSON object.</param>
    /// <returns>A description of why the change is refused, or <see langword="null"/> when it is allowed.</returns>
    string? ValidateOptionsChange(JsonElement current, JsonElement proposed);
}
