// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.Persistence;

/// <summary>
/// The name of a table or sequence in a Postgres database, with the schema that holds it.
/// </summary>
/// <param name="Schema">The schema that holds the table or sequence, or <c>null</c> for the default schema.</param>
/// <param name="Name">The name of the table or sequence.</param>
[PublicAPI]
public readonly record struct DatabaseObjectName(string? Schema, string Name);
