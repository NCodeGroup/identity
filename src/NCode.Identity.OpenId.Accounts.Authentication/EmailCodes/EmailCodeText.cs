// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;

namespace NCode.Identity.OpenId.Accounts.Authentication.EmailCodes;

/// <summary>
/// Formats a one-time code as six digits with leading zeros, in the invariant culture, straight into the span a
/// composite format writes to, so the code is never a string.
/// </summary>
/// <param name="Value">The code.</param>
// See ADR-1533.
internal readonly record struct EmailCodeText(int Value) : ISpanFormattable
{
    private const string CodeFormat = "D6";

    /// <inheritdoc />
    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider
    ) => Value.TryFormat(destination, out charsWritten, CodeFormat, CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public string ToString(string? format, IFormatProvider? formatProvider) =>
        Value.ToString(CodeFormat, CultureInfo.InvariantCulture);
}
