// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.Authentication.Interactive;

/// <summary>
/// The JSON payload the email page fetches to show the address of the signed-in end user and whether it is verified.
/// </summary>
/// <param name="Email">The email address of the end user.</param>
/// <param name="Verified">Whether the end user has proven they control the address.</param>
// See ADR-1533.
internal sealed record AccountEmailResponse(string Email, bool Verified);
