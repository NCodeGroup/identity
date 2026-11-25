// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Http;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

/// <summary>
/// An endpoint filter that is installed once on the <c>cells</c> route group. It answers a cell write that the
/// directory refuses with <c>409 Conflict</c> and the refusal's message, so that no handler catches the exception
/// itself.
/// </summary>
internal sealed class CellDirectoryConflictEndpointFilter : IEndpointFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next
    )
    {
        try
        {
            return await next(context);
        }
        catch (CellDirectoryConflictException exception)
        {
            return DirectoryEndpoints.Conflict(exception.Message);
        }
    }
}
