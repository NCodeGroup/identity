// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Identity;

namespace NCode.Identity.OpenId.Accounts.AspNetIdentity;

public sealed class TestTenantUser : IdentityUser, ITenantBoundUser
{
    public TestTenantUser() { }

    public TestTenantUser(string userName)
        : base(userName) { }

    public string TenantId { get; set; } = string.Empty;
}
