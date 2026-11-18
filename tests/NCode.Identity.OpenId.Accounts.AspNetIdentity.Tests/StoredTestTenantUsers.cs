// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace NCode.Identity.OpenId.Accounts.AspNetIdentity;

// Writes and reads users in ASP.NET Core Identity's Entity Framework tables without the user manager, as a host's own
// code or a database script can, so a test can store a user that the user manager never stores.
public static class StoredTestTenantUsers
{
    public static async ValueTask AddAsync(
        IServiceProvider serviceProvider,
        TestTenantUser user,
        CancellationToken cancellationToken
    )
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<
            IdentityDbContext<TestTenantUser>
        >();
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public static async ValueTask<TestTenantUser> GetAsync(
        IServiceProvider serviceProvider,
        string id,
        CancellationToken cancellationToken
    )
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<
            IdentityDbContext<TestTenantUser>
        >();
        return await dbContext
            .Users.AsNoTracking()
            .SingleAsync(user => user.Id == id, cancellationToken);
    }
}
