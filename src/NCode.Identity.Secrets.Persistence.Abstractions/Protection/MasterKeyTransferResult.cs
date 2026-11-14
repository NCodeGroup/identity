// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.Secrets.Persistence.Protection;

/// <summary>
/// Reports the outcome of importing the master keys of an owner from a transfer package.
/// </summary>
/// <param name="Count">The number of master keys that were stored.</param>
// See ADR-1514.
[PublicAPI]
public sealed record MasterKeyTransferResult(int Count);
