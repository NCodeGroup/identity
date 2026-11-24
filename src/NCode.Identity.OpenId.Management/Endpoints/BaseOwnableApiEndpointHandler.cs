// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NCode.Identity.Events.Audit;
using NCode.Identity.OpenId.Management.Auditing;
using NCode.Identity.OpenId.Management.Authorization;
using NCode.Identity.OpenId.Management.Contracts;
using NCode.Identity.OpenId.Management.ControlPlane;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Management.Endpoints;

/// <summary>
/// Provides common functionality for management API endpoint handlers whose resources hold role lists: listing a
/// role's holders with whether the caller may change them, granting and revoking any role, and the one-owner
/// invariant.
/// </summary>
internal abstract class BaseOwnableApiEndpointHandler : BaseApiEndpointHandler
{
    /// <summary>
    /// Creates the <c>409 Conflict</c> response for a grant that is refused because the principal is a client that
    /// cannot hold the role at the node.
    /// </summary>
    // See ADR-1466.
    internal static ProblemHttpResult RoleRefusedForPrincipal() =>
        TypedResults.Problem(
            detail: "A client holds a role only as a machine client, at the node that owns it or a node beneath it.",
            statusCode: StatusCodes.Status409Conflict
        );

    /// <summary>
    /// Reads the one name that a grant requests its principal by.
    /// </summary>
    /// <param name="request">The <see cref="AddRoleHolderRequest"/> that names the principal.</param>
    /// <param name="name">
    /// When this method returns <c>true</c>, the username when only the username is non-empty, or the principal
    /// identifier when only the principal identifier is non-empty; otherwise, <see langword="null"/>.
    /// </param>
    /// <param name="isUserName">
    /// <c>true</c> when the username of <paramref name="request"/> is non-empty; otherwise, <c>false</c>.
    /// </param>
    /// <returns>
    /// <c>true</c> when exactly one of the principal identifier and the username is non-empty; otherwise, <c>false</c>.
    /// </returns>
    // See ADR-1465.
    private static bool TryGetRequestedName(
        AddRoleHolderRequest request,
        [NotNullWhen(true)] out string? name,
        out bool isUserName
    )
    {
        var principalId = request.PrincipalId;
        var userName = request.UserName;
        var hasPrincipalId = !string.IsNullOrEmpty(principalId);
        isUserName = !string.IsNullOrEmpty(userName);
        if (hasPrincipalId == isUserName)
        {
            name = null;
            return false;
        }

        name = isUserName ? userName : principalId;
        return name is not null;
    }

    /// <summary>
    /// Gets the factory used to open a unit of work for role-list operations.
    /// </summary>
    protected abstract IStoreManagerFactory StoreManagerFactory { get; }

    /// <summary>
    /// Gets the service used to read and mutate resource ownership.
    /// </summary>
    protected abstract IResourceOwnershipService ResourceOwnershipService { get; }

    /// <summary>
    /// Gets the directory that names the holders of a role list and resolves a grant's username.
    /// </summary>
    protected abstract IControlPlanePrincipalDirectory ControlPlanePrincipalDirectory { get; }

    /// <summary>
    /// Gets the recorder used to write management audit events.
    /// </summary>
    protected abstract IManagementAuditRecorder ManagementAuditRecorder { get; }

    /// <summary>
    /// Records that a role assignment was granted or revoked at a resource node.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> of the request.</param>
    /// <param name="assignment">The role assignment that was granted or revoked.</param>
    /// <param name="changeType">One of the values in <see cref="ResourceChangeTypes"/>.</param>
    /// <returns>
    /// A <see cref="ValueTask"/> that represents the asynchronous operation.
    /// </returns>
    // See ADR-1465.
    [SuppressMessage(
        "Design",
        "NCID0003",
        Justification = "The work follows a committed change and runs on its own bounded token through the audit recorder, so no caller can pass a token that drops it (ADR-1351)."
    )]
    protected internal virtual ValueTask RecordRoleAssignmentChangedAsync(
        HttpContext httpContext,
        PersistedRoleAssignment assignment,
        string changeType
    )
    {
        var values = new
        {
            assignment.AssignmentId,
            assignment.PrincipalId,
            assignment.RoleName,
        };
        var resourceValues = ToResourceValues(values);
        var auditEvent = new RoleAssignmentChangedAuditEvent
        {
            EventSource = AuditEventSources.Management,
            TenantId = assignment.TenantId,
            SubjectId = assignment.PrincipalId,
            ResourceId = assignment.AssignmentId,
            NodeType = assignment.ResourceType,
            NodeId = assignment.ResourceId,
            ChangeType = changeType,
            ResourceValues = resourceValues,
            Outcome = AuditOutcome.Success,
        };
        return ManagementAuditRecorder.RecordResourceChangeAsync(httpContext, auditEvent);
    }

    /// <summary>
    /// Maps a role assignment to its <see cref="RoleHolderResource"/> representation.
    /// </summary>
    /// <param name="assignment">The role assignment to map.</param>
    /// <param name="principal">
    /// The control plane's summary of the holder, or <see langword="null"/> when the control plane does not know the
    /// principal.
    /// </param>
    /// <returns>The mapped <see cref="RoleHolderResource"/>.</returns>
    internal virtual RoleHolderResource ToRoleHolderResource(
        PersistedRoleAssignment assignment,
        PrincipalSummaryResource? principal
    ) =>
        new()
        {
            AssignmentId = assignment.AssignmentId,
            PrincipalId = assignment.PrincipalId,
            RoleName = assignment.RoleName,
            Principal = principal,
        };

    /// <summary>
    /// Describes principals through the control plane's directory.
    /// </summary>
    /// <param name="principalIds">The identifiers of the principals to describe.</param>
    /// <param name="cancellationToken">
    /// The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.
    /// </param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> whose result maps each described principal's identifier to its summary,
    /// comparing identifiers ignoring case, as the stores match them; a principal the control plane does not know has
    /// no entry.
    /// </returns>
    private async ValueTask<IReadOnlyDictionary<string, PrincipalSummaryResource>> DescribeAsync(
        List<string> principalIds,
        CancellationToken cancellationToken
    )
    {
        if (principalIds.Count == 0)
        {
            return FrozenDictionary<string, PrincipalSummaryResource>.Empty;
        }

        var query = new ControlPlanePrincipalQuery { PrincipalIds = principalIds };
        var found = await ControlPlanePrincipalDirectory.FindAsync(query, cancellationToken);
        var summaries = new Dictionary<string, PrincipalSummaryResource>(
            found.Count,
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var summary in found)
        {
            summaries.TryAdd(summary.PrincipalId, summary);
        }

        return summaries;
    }

    /// <summary>
    /// Determines whether the caller holds an operation at a resource node.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> of the request.</param>
    /// <param name="node">The resource node the operation applies to.</param>
    /// <param name="operation">The operation to authorize.</param>
    /// <param name="cancellationToken">
    /// The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.
    /// </param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> whose result is <c>true</c> when the caller holds the operation at the
    /// node; otherwise, <c>false</c>.
    /// </returns>
    // See ADR-1465.
    protected internal virtual async ValueTask<bool> HoldsAuthorityAsync(
        HttpContext httpContext,
        ResourceNode node,
        IAuthorizationRequirement operation,
        CancellationToken cancellationToken
    )
    {
        var authorizationResult = await AuthorizationService.AuthorizeAsync(
            httpContext.User,
            node,
            operation
        );
        return authorizationResult.Succeeded;
    }

    /// <summary>
    /// Processes an HTTP <c>GET</c> for the principals that hold one role at a resource node: authorizes the caller to
    /// read the node, then returns the assignments of the role, each with the control plane's summary of its holder,
    /// and whether the caller may grant and revoke the role there. Responds <c>404</c> when the resource does not
    /// exist.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> of the request.</param>
    /// <param name="roleList">The <see cref="RoleList"/> that describes the role whose assignments are listed.</param>
    /// <param name="canChangeAsync">
    /// Determines whether the caller may grant and revoke the role at the node, which is the
    /// <see cref="RoleListResource.CanChange"/> value of the response.
    /// </param>
    /// <param name="cancellationToken">
    /// The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.
    /// </param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> whose result is the <see cref="IResult"/> of the response.
    /// </returns>
    // See ADR-1465.
    protected internal virtual async ValueTask<IResult> ProcessListRoleAssignmentsAsync(
        HttpContext httpContext,
        RoleList roleList,
        Func<IStoreManager, CancellationToken, ValueTask<bool>> canChangeAsync,
        CancellationToken cancellationToken
    )
    {
        var node = roleList.Node;
        var authorizationResult = await AuthorizationService.AuthorizeAsync(
            httpContext.User,
            node,
            Operations.Read
        );
        if (!authorizationResult.Succeeded)
        {
            return AuthorizationFailed(httpContext);
        }

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        if (!await roleList.ResourceExistsAsync(storeManager, cancellationToken))
        {
            return ManagementProblems.ResourceNotFound();
        }

        var assignments = await ResourceOwnershipService.GetAssignmentsAsync(
            storeManager,
            node.ResourceType,
            node.ResourceId,
            roleList.RoleName,
            cancellationToken
        );
        var canChange = await canChangeAsync(storeManager, cancellationToken);
        var principalIds = new List<string>(assignments.Count);
        // A role list holds each principal at most once, by the unique index of role assignments, so the identifiers
        // need no dedupe.
        foreach (var assignment in assignments)
        {
            principalIds.Add(assignment.PrincipalId);
        }

        var summaries = await DescribeAsync(principalIds, cancellationToken);
        var items = new List<RoleHolderResource>(assignments.Count);
        foreach (var assignment in assignments)
        {
            var summary = summaries.GetValueOrDefault(assignment.PrincipalId);
            var item = ToRoleHolderResource(assignment, summary);
            items.Add(item);
        }

        var roleListResource = new RoleListResource { Items = items, CanChange = canChange };
        return TypedResults.Json(roleListResource);
    }

    /// <summary>
    /// Processes an HTTP <c>POST</c> that grants a principal one role at a resource node: authorizes the caller to
    /// perform the operation at the node, then adds the assignment and records the grant. The request names the
    /// principal by identifier or by the username of a control-plane local account. Responds <c>403</c> when the caller
    /// lacks the operation, <c>400</c> when the request names no principal, both, an identifier no principal of the
    /// control plane has, or a username no principal has, <c>404</c> when the resource does not exist, <c>409</c> when
    /// the principal is a client that cannot hold the role at the node, <c>200</c> when the principal already holds the
    /// role, and <c>201</c> when the role is granted.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> of the request.</param>
    /// <param name="roleList">The <see cref="RoleList"/> that describes the role to grant.</param>
    /// <param name="request">
    /// The <see cref="AddRoleHolderRequest"/> that names the principal to grant the role to by exactly one of its
    /// identifier and its username.
    /// </param>
    /// <param name="cancellationToken">
    /// The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.
    /// </param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> whose result is the <see cref="IResult"/> of the response.
    /// </returns>
    // See ADR-1465.
    protected internal virtual async ValueTask<IResult> ProcessAddRoleAssignmentAsync(
        HttpContext httpContext,
        RoleList roleList,
        AddRoleHolderRequest request,
        CancellationToken cancellationToken
    )
    {
        var node = roleList.Node;
        var authorizationResult = await AuthorizationService.AuthorizeAsync(
            httpContext.User,
            node,
            roleList.Operation
        );
        if (!authorizationResult.Succeeded)
        {
            return AuthorizationFailed(httpContext);
        }

        if (!TryGetRequestedName(request, out var requestedName, out var isUserName))
        {
            return TypedResults.Problem(
                detail: "Name exactly one of 'principalId' and 'userName'.",
                statusCode: StatusCodes.Status400BadRequest
            );
        }

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        if (!await roleList.ResourceExistsAsync(storeManager, cancellationToken))
        {
            return ManagementProblems.ResourceNotFound();
        }

        PrincipalSummaryResource summary;
        if (isUserName)
        {
            var userNameQuery = new ControlPlanePrincipalQuery { UserNames = [requestedName] };
            var matches = await ControlPlanePrincipalDirectory.FindAsync(
                userNameQuery,
                cancellationToken
            );
            PrincipalSummaryResource? userNameMatch = null;
            foreach (var match in matches)
            {
                if (
                    string.Equals(match.UserName, requestedName, StringComparison.OrdinalIgnoreCase)
                )
                {
                    userNameMatch = match;
                    break;
                }
            }

            if (userNameMatch is null)
            {
                return TypedResults.Problem(
                    detail: "No principal of the control plane has the user name.",
                    statusCode: StatusCodes.Status400BadRequest
                );
            }

            summary = userNameMatch;
        }
        else
        {
            var described = await DescribeAsync([requestedName], cancellationToken);
            var describedSummary = described.GetValueOrDefault(requestedName);
            if (describedSummary is null)
            {
                return TypedResults.Problem(
                    detail: "No principal of the control plane has the identifier.",
                    statusCode: StatusCodes.Status400BadRequest
                );
            }

            summary = describedSummary;
        }

        var principalId = summary.PrincipalId;
        var grant = await ResourceOwnershipService.AddAssignmentAsync(
            storeManager,
            node.TenantId,
            node.ResourceType,
            node.ResourceId,
            principalId,
            roleList.RoleName,
            cancellationToken
        );
        if (grant.IsRefused)
        {
            return RoleRefusedForPrincipal();
        }

        await storeManager.SaveChangesAsync(cancellationToken);

        var roleHolder = ToRoleHolderResource(grant.Assignment, summary);
        if (!grant.IsAdded)
        {
            return TypedResults.Ok(roleHolder);
        }

        await RecordRoleAssignmentChangedAsync(
            httpContext,
            grant.Assignment,
            ResourceChangeTypes.Created
        );

        return TypedResults.Created($"{roleList.Path}/{principalId}", roleHolder);
    }

    /// <summary>
    /// Processes an HTTP <c>DELETE</c> that revokes a principal's role at a resource node: authorizes the caller to
    /// perform the operation at the node, then removes the assignment and records each removed assignment. Responds
    /// <c>403</c> when the caller lacks the operation, <c>404</c> when the principal does not hold the role, and
    /// <c>204</c> when the role is revoked.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> of the request.</param>
    /// <param name="roleList">The <see cref="RoleList"/> that describes the role to revoke.</param>
    /// <param name="principalId">The identifier of the principal to revoke the role from.</param>
    /// <param name="cancellationToken">
    /// The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.
    /// </param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> whose result is the <see cref="IResult"/> of the response.
    /// </returns>
    // See ADR-1465.
    protected internal virtual async ValueTask<IResult> ProcessRemoveRoleAssignmentAsync(
        HttpContext httpContext,
        RoleList roleList,
        string principalId,
        CancellationToken cancellationToken
    )
    {
        var node = roleList.Node;
        var authorizationResult = await AuthorizationService.AuthorizeAsync(
            httpContext.User,
            node,
            roleList.Operation
        );
        if (!authorizationResult.Succeeded)
        {
            return AuthorizationFailed(httpContext);
        }

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var removed = await ResourceOwnershipService.RemoveAssignmentAsync(
            storeManager,
            node.ResourceType,
            node.ResourceId,
            principalId,
            roleList.RoleName,
            cancellationToken
        );
        if (removed.Count == 0)
        {
            return ManagementProblems.ResourceNotFound();
        }

        await storeManager.SaveChangesAsync(cancellationToken);
        foreach (var assignment in removed)
        {
            await RecordRoleAssignmentChangedAsync(
                httpContext,
                assignment,
                ResourceChangeTypes.Deleted
            );
        }

        return TypedResults.NoContent();
    }

    /// <summary>
    /// Processes an HTTP <c>DELETE</c> that revokes a principal's ownership of a resource: authorizes the caller
    /// to update the resource node, then removes the owner and records each removed assignment. Responds <c>403</c>
    /// when the caller lacks the operation, <c>409</c> when the removal would orphan the resource, <c>404</c> when the
    /// principal is not an owner, and <c>204</c> when the owner is removed.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> of the request.</param>
    /// <param name="roleList">The <see cref="RoleList"/> that describes the owners of the resource.</param>
    /// <param name="principalId">The identifier of the principal to remove as an owner.</param>
    /// <param name="cancellationToken">
    /// The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.
    /// </param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> whose result is the <see cref="IResult"/> of the response.
    /// </returns>
    // See ADR-1465.
    protected internal virtual async ValueTask<IResult> ProcessRemoveOwnerAsync(
        HttpContext httpContext,
        RoleList roleList,
        string principalId,
        CancellationToken cancellationToken
    )
    {
        var node = roleList.Node;
        var authorizationResult = await AuthorizationService.AuthorizeAsync(
            httpContext.User,
            node,
            roleList.Operation
        );
        if (!authorizationResult.Succeeded)
        {
            return AuthorizationFailed(httpContext);
        }

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var removal = await ResourceOwnershipService.RemoveOwnerAsync(
            storeManager,
            node.ResourceType,
            node.ResourceId,
            principalId,
            cancellationToken
        );

        switch (removal.Result)
        {
            case OwnerRemovalResult.NotFound:
                return ManagementProblems.ResourceNotFound();

            case OwnerRemovalResult.LastOwnerForbidden:
                return TypedResults.Problem(
                    detail: "The resource must retain at least one owner.",
                    statusCode: StatusCodes.Status409Conflict
                );

            default:
                await storeManager.SaveChangesAsync(cancellationToken);
                foreach (var removed in removal.RemovedAssignments)
                {
                    await RecordRoleAssignmentChangedAsync(
                        httpContext,
                        removed,
                        ResourceChangeTypes.Deleted
                    );
                }

                return TypedResults.NoContent();
        }
    }
}
