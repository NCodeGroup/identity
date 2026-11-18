// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.EntityFrameworkCore.Diagnostics;

namespace NCode.Identity.OpenId.Accounts.AspNetIdentity;

/// <summary>
/// An Entity Framework Core interceptor for tests that counts how many times a context saves its changes.
/// </summary>
public sealed class SaveChangesCounter : SaveChangesInterceptor
{
    public int Count { get; private set; }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result
    )
    {
        Count++;
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        Count++;
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
