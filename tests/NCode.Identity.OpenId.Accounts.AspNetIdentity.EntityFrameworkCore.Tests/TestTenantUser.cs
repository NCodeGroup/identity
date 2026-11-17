// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Identity;

namespace NCode.Identity.OpenId.Accounts.AspNetIdentity.EntityFrameworkCore;

public sealed class TestTenantUser : IdentityUser, ITenantBoundUser
{
    public string TenantId { get; set; } = string.Empty;
}
