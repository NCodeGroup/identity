// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.Mail;

/// <summary>
/// Contains the known values of a mail content's transfer encoding, which are the ones RFC 2045 defines. A transport
/// may support others.
/// </summary>
// See ADR-1532.
[PublicAPI]
public static class MailTransferEncodings
{
    /// <summary>
    /// The content is 7-bit text with short lines.
    /// </summary>
    public const string SevenBit = "7bit";

    /// <summary>
    /// The content is 8-bit text with short lines.
    /// </summary>
    public const string EightBit = "8bit";

    /// <summary>
    /// The content is binary data that is written as it is.
    /// </summary>
    public const string Binary = "binary";

    /// <summary>
    /// The content is encoded as quoted-printable text.
    /// </summary>
    public const string QuotedPrintable = "quoted-printable";

    /// <summary>
    /// The content is encoded as base64 text.
    /// </summary>
    public const string Base64 = "base64";
}
