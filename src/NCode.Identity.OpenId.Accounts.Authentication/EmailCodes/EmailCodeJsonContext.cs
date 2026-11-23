// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;

namespace NCode.Identity.OpenId.Accounts.Authentication.EmailCodes;

/// <summary>
/// The source-generated JSON context for the state of an email-code flow.
/// </summary>
// See ADR-1533.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true
)]
[JsonSerializable(typeof(EmailCodeState))]
internal sealed partial class EmailCodeJsonContext : JsonSerializerContext;
