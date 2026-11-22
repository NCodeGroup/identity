// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NCode.Identity.OpenId.Accounts.Stores;

namespace NCode.Identity.OpenId.Accounts.AspNetIdentity.EntityFrameworkCore;

/// <summary>
/// Provides extension methods that configure the Entity Framework Core model of an ASP.NET Core Identity user for the
/// ASP.NET Core Identity <see cref="ILocalAccountStore"/> adapter.
/// </summary>
[PublicAPI]
public static class TenantBoundUserModel
{
    /// <param name="builder">The <see cref="EntityTypeBuilder{TEntity}"/> of the user entity.</param>
    /// <typeparam name="TUser">The ASP.NET Core Identity user type.</typeparam>
    extension<TUser>(EntityTypeBuilder<TUser> builder)
        where TUser : IdentityUser, ITenantBoundUser
    {
        /// <summary>
        /// Sets the maximum length of <c>UserName</c> and <c>Email</c> to <see cref="AccountMaxLengths.UserName"/> and
        /// <see cref="AccountMaxLengths.Email"/>, and of <c>NormalizedUserName</c> and <c>NormalizedEmail</c> to
        /// <see cref="AccountMaxLengths.NormalizedUserName"/> and <see cref="AccountMaxLengths.NormalizedEmail"/>. The
        /// default Entity Framework Core model of ASP.NET Core Identity sizes all four at 256, which a normalized value
        /// that the adapter prefixes with the tenant identifier outgrows. It also sets the maximum length of
        /// <c>DisplayName</c> to <see cref="AccountMaxLengths.DisplayName"/> and requires it. A host's
        /// <c>IdentityDbContext</c> calls this method in <c>OnModelCreating</c> after <c>base.OnModelCreating</c>.
        /// </summary>
        /// <returns>The same <see cref="EntityTypeBuilder{TEntity}"/> so that calls can be chained.</returns>
        public EntityTypeBuilder<TUser> HasTenantLookupLengths()
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.Property(user => user.UserName).HasMaxLength(AccountMaxLengths.UserName);
            builder
                .Property(user => user.NormalizedUserName)
                .HasMaxLength(AccountMaxLengths.NormalizedUserName);
            builder.Property(user => user.Email).HasMaxLength(AccountMaxLengths.Email);
            builder
                .Property(user => user.NormalizedEmail)
                .HasMaxLength(AccountMaxLengths.NormalizedEmail);
            builder
                .Property(user => user.DisplayName)
                .HasMaxLength(AccountMaxLengths.DisplayName)
                .IsRequired();

            return builder;
        }
    }
}
