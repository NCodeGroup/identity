// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NCode.Buffers;

namespace NCode.Identity.OpenId.Authentication.Endpoints;

/// <summary>
/// Writes a text body as strict UTF-8 bytes without adding a charset parameter.
/// </summary>
internal static class Utf8TextResults
{
    public static Utf8ContentHttpResult Create(string content, string contentType)
    {
        using var lease = SecureEncoding.UTF8.RentBytes(
            content,
            isSensitive: false,
            out Span<byte> utf8
        );
        return TypedResults.Text(utf8, contentType); // copies the bytes
    }
}
