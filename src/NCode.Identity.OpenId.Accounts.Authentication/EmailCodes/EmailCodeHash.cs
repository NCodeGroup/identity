// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace NCode.Identity.OpenId.Accounts.Authentication.EmailCodes;

/// <summary>
/// Represents the SHA-256 hash of an email-code flow's handle and code as a fixed 32-byte value, so no array holds it.
/// </summary>
// See ADR-1533.
[InlineArray(Length)]
[JsonConverter(typeof(EmailCodeHashJsonConverter))]
internal struct EmailCodeHash
{
    /// <summary>
    /// The number of bytes in a hash.
    /// </summary>
    internal const int Length = 32;

    private byte _element0;
}
