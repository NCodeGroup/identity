// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.Accounts.Authentication.EmailCodes;

/// <summary>
/// Names one email code flow: the purpose of its code and the handle that
/// <see cref="ILocalAccountEmailCodes.SendAsync"/> returned.
/// </summary>
/// <param name="Purpose">The purpose of the code, one of the <see cref="EmailCodePurposes"/>.</param>
/// <param name="FlowId">The handle that <see cref="ILocalAccountEmailCodes.SendAsync"/> returned.</param>
// See ADR-1533.
internal readonly record struct EmailCodeFlow(string Purpose, string FlowId);
