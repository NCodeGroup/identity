// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NCode.Identity.Cryptography;
using NCode.Identity.Events.Audit;
using NCode.Identity.OpenId.Management.Auditing;
using NCode.Identity.OpenId.Management.Authorization;
using NCode.Identity.OpenId.Management.Contracts;
using NCode.Identity.OpenId.Management.ControlPlane;
using NCode.Identity.OpenId.Management.Principals;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Management.Endpoints;

public class BaseOwnableApiEndpointHandlerTests : IDisposable
{
    private const string TenantId = "tenant-1";
    private const string ResourceId = "client-1";
    private const string RoleName = "custom-role";
    private const string RoleListPath = "/clients/client-1/holders";

    private static HttpContext CreateHttpContext() =>
        new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity("test")) };

    private static PersistedRoleAssignment Assignment(
        string principalId,
        string roleName,
        string assignmentId = "a1"
    ) =>
        new()
        {
            TenantId = TenantId,
            AssignmentId = assignmentId,
            PrincipalId = principalId,
            RoleName = roleName,
            ResourceType = ResourceNodeTypes.Client,
            ResourceId = ResourceId,
            ConcurrencyToken = string.Empty,
        };

    private static PersistedRoleAssignment Owner(string principalId, string assignmentId = "a1") =>
        Assignment(principalId, BuiltInRoles.Owner, assignmentId);

    private static ValueTask<bool> Exists(IStoreManager _, CancellationToken __) =>
        ValueTask.FromResult(true);

    private static ValueTask<bool> Missing(IStoreManager _, CancellationToken __) =>
        ValueTask.FromResult(false);

    private static RoleList CreateRoleList(
        ResourceNode node,
        string roleName,
        IAuthorizationRequirement operation,
        Func<IStoreManager, CancellationToken, ValueTask<bool>> resourceExistsAsync
    ) =>
        new()
        {
            Node = node,
            RoleName = roleName,
            Operation = operation,
            Path = RoleListPath,
            ResourceExistsAsync = resourceExistsAsync,
        };

    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);

    public void Dispose() => MockRepository.Verify();

    private Mock<IControlPlanePrincipalDirectory> MockPrincipalDirectory { get; }

    public BaseOwnableApiEndpointHandlerTests() =>
        MockPrincipalDirectory = MockRepository.Create<IControlPlanePrincipalDirectory>();

    private sealed class TestHandler(
        IAuthorizationService authorizationService,
        IStoreManagerFactory storeManagerFactory,
        IResourceOwnershipService ownershipService,
        IManagementAuditRecorder managementAuditRecorder,
        ICryptoService cryptoService,
        IControlPlanePrincipalDirectory principalDirectory
    ) : BaseOwnableApiEndpointHandler
    {
        protected override IAuthorizationService AuthorizationService { get; } =
            authorizationService;
        protected override ICryptoService CryptoService { get; } = cryptoService;
        protected override IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
        protected override IResourceOwnershipService ResourceOwnershipService { get; } =
            ownershipService;
        protected override IManagementAuditRecorder ManagementAuditRecorder { get; } =
            managementAuditRecorder;
        protected override IControlPlanePrincipalDirectory ControlPlanePrincipalDirectory { get; } =
            principalDirectory;
    }

    private TestHandler CreateHandler(
        IAuthorizationService authorizationService,
        IStoreManagerFactory storeManagerFactory,
        IResourceOwnershipService ownershipService,
        IManagementAuditRecorder? recorder = null,
        IControlPlanePrincipalDirectory? directory = null
    )
    {
        var effectiveRecorder =
            recorder ?? MockRepository.Create<IManagementAuditRecorder>().Object;
        var cryptoService = MockRepository.Create<ICryptoService>().Object;
        var effectiveDirectory = directory ?? MockPrincipalDirectory.Object;
        return new TestHandler(
            authorizationService,
            storeManagerFactory,
            ownershipService,
            effectiveRecorder,
            cryptoService,
            effectiveDirectory
        );
    }

    private Mock<IAuthorizationService> Authz(AuthorizationResult result)
    {
        var mock = MockRepository.Create<IAuthorizationService>();
        mock.Setup(x =>
                x.AuthorizeAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<object?>(),
                    It.IsAny<IEnumerable<IAuthorizationRequirement>>()
                )
            )
            .ReturnsAsync(result)
            .Verifiable();
        return mock;
    }

    private (Mock<IStoreManagerFactory>, Mock<IStoreManager>) StoreManager()
    {
        var factory = MockRepository.Create<IStoreManagerFactory>();
        var manager = MockRepository.Create<IStoreManager>();
        factory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(manager.Object)
            .Verifiable();
        manager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();
        return (factory, manager);
    }

    private Mock<IManagementAuditRecorder> Recorder(List<RoleAssignmentChangedAuditEvent> captured)
    {
        var recorder = MockRepository.Create<IManagementAuditRecorder>();
        recorder
            .Setup(x =>
                x.RecordResourceChangeAsync(
                    It.IsAny<HttpContext>(),
                    It.IsAny<RoleAssignmentChangedAuditEvent>()
                )
            )
            .Callback<HttpContext, ResourceChangeAuditEvent>(
                (_, auditEvent) =>
                    captured.Add(Assert.IsType<RoleAssignmentChangedAuditEvent>(auditEvent))
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        return recorder;
    }

    #region ProcessRemoveOwnerAsync Tests

    [Fact]
    public async Task ProcessRemoveOwnerAsync_WhenLastOwner_ReturnsConflict()
    {
        var authz = Authz(AuthorizationResult.Success());
        var (factory, manager) = StoreManager();
        var ownership = MockRepository.Create<IResourceOwnershipService>();
        ownership
            .Setup(x =>
                x.RemoveOwnerAsync(
                    manager.Object,
                    ResourceNodeTypes.Client,
                    ResourceId,
                    "p1",
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                new OwnerRemoval
                {
                    Result = OwnerRemovalResult.LastOwnerForbidden,
                    RemovedAssignments = [],
                }
            )
            .Verifiable();

        // A strict recorder with no setup fails the test when an event is recorded.
        var recorder = MockRepository.Create<IManagementAuditRecorder>();

        var handler = CreateHandler(
            authz.Object,
            factory.Object,
            ownership.Object,
            recorder.Object
        );
        var node = ResourceNode.For(TenantId, ResourceNodeTypes.Client, ResourceId);
        var roleList = CreateRoleList(node, BuiltInRoles.Owner, Operations.Update, Exists);
        var result = await handler.ProcessRemoveOwnerAsync(
            CreateHttpContext(),
            roleList,
            "p1",
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    [Fact]
    public async Task ProcessRemoveOwnerAsync_WhenNotFound_ReturnsNotFoundAndRecordsNothing()
    {
        var authz = Authz(AuthorizationResult.Success());
        var (factory, manager) = StoreManager();
        var ownership = MockRepository.Create<IResourceOwnershipService>();
        ownership
            .Setup(x =>
                x.RemoveOwnerAsync(
                    manager.Object,
                    ResourceNodeTypes.Client,
                    ResourceId,
                    "p1",
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                new OwnerRemoval { Result = OwnerRemovalResult.NotFound, RemovedAssignments = [] }
            )
            .Verifiable();
        // A strict recorder with no setup fails the test when an event is recorded.
        var recorder = MockRepository.Create<IManagementAuditRecorder>();

        var handler = CreateHandler(
            authz.Object,
            factory.Object,
            ownership.Object,
            recorder.Object
        );
        var node = ResourceNode.For(TenantId, ResourceNodeTypes.Client, ResourceId);
        var roleList = CreateRoleList(node, BuiltInRoles.Owner, Operations.Update, Exists);
        var result = await handler.ProcessRemoveOwnerAsync(
            CreateHttpContext(),
            roleList,
            "p1",
            CancellationToken.None
        );

        var notFound = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, notFound.StatusCode);
    }

    [Fact]
    public async Task ProcessRemoveOwnerAsync_WhenRemoved_ReturnsNoContentAndRecordsDeleted()
    {
        var authz = Authz(AuthorizationResult.Success());
        var (factory, manager) = StoreManager();
        manager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        var ownership = MockRepository.Create<IResourceOwnershipService>();
        ownership
            .Setup(x =>
                x.RemoveOwnerAsync(
                    manager.Object,
                    ResourceNodeTypes.Client,
                    ResourceId,
                    "p1",
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                new OwnerRemoval
                {
                    Result = OwnerRemovalResult.Removed,
                    RemovedAssignments = [Owner("p1")],
                }
            )
            .Verifiable();
        var captured = new List<RoleAssignmentChangedAuditEvent>();
        var recorder = Recorder(captured);

        var handler = CreateHandler(
            authz.Object,
            factory.Object,
            ownership.Object,
            recorder.Object
        );
        var node = ResourceNode.For(TenantId, ResourceNodeTypes.Client, ResourceId);
        var roleList = CreateRoleList(node, BuiltInRoles.Owner, Operations.Update, Exists);
        var result = await handler.ProcessRemoveOwnerAsync(
            CreateHttpContext(),
            roleList,
            "p1",
            CancellationToken.None
        );

        Assert.IsType<NoContent>(result);
        var auditEvent = Assert.Single(captured);
        Assert.Equal(ResourceChangeTypes.Deleted, auditEvent.ChangeType);
        Assert.Equal("a1", auditEvent.ResourceId);
        Assert.Equal("p1", auditEvent.SubjectId);
    }

    #endregion

    #region RecordRoleAssignmentChangedAsync Tests

    [Fact]
    public async Task RecordRoleAssignmentChangedAsync_RecordsTheAssignmentAtItsNode()
    {
        var authz = MockRepository.Create<IAuthorizationService>();
        var factory = MockRepository.Create<IStoreManagerFactory>();
        var ownership = MockRepository.Create<IResourceOwnershipService>();
        var recorder = MockRepository.Create<IManagementAuditRecorder>();
        ResourceChangeAuditEvent? captured = null;
        var httpContext = CreateHttpContext();
        recorder
            .Setup(x =>
                x.RecordResourceChangeAsync(httpContext, It.IsAny<ResourceChangeAuditEvent>())
            )
            .Callback<HttpContext, ResourceChangeAuditEvent>(
                (_, auditEvent) => captured = auditEvent
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var handler = CreateHandler(
            authz.Object,
            factory.Object,
            ownership.Object,
            recorder.Object
        );
        await handler.RecordRoleAssignmentChangedAsync(
            httpContext,
            Owner("p2", "a2"),
            ResourceChangeTypes.Created
        );

        var auditEvent = Assert.IsType<RoleAssignmentChangedAuditEvent>(captured);
        Assert.Equal(AuditEventSources.Management, auditEvent.EventSource);
        Assert.Equal(TenantId, auditEvent.TenantId);
        Assert.Equal("p2", auditEvent.SubjectId);
        Assert.Equal(ManagementResourceTypes.RoleAssignment, auditEvent.ResourceType);
        Assert.Equal("a2", auditEvent.ResourceId);
        Assert.Equal(ResourceNodeTypes.Client, auditEvent.NodeType);
        Assert.Equal(ResourceId, auditEvent.NodeId);
        Assert.Equal(ResourceChangeTypes.Created, auditEvent.ChangeType);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        var values = Assert.IsType<JsonElement>(auditEvent.ResourceValues);
        Assert.Equal("a2", values.GetProperty("assignmentId").GetString());
        Assert.Equal("p2", values.GetProperty("principalId").GetString());
        Assert.Equal(BuiltInRoles.Owner, values.GetProperty("roleName").GetString());
    }

    #endregion

    #region ProcessListRoleAssignmentsAsync Tests

    [Fact]
    public async Task ProcessListRoleAssignmentsAsync_WhenTheCallerMayChange_ReturnsTheAssignmentsAndCanChange()
    {
        PrincipalDirectoryMocks.SetupDescribingEveryPrincipal(MockPrincipalDirectory);
        var authz = Authz(AuthorizationResult.Success());
        var (factory, manager) = StoreManager();
        var ownership = MockRepository.Create<IResourceOwnershipService>();
        ownership
            .Setup(x =>
                x.GetAssignmentsAsync(
                    manager.Object,
                    ResourceNodeTypes.Client,
                    ResourceId,
                    BuiltInRoles.Owner,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync([Owner("p1")])
            .Verifiable();
        var node = ResourceNode.For(TenantId, ResourceNodeTypes.Client, ResourceId);

        var handler = CreateHandler(authz.Object, factory.Object, ownership.Object);
        var roleList = CreateRoleList(node, BuiltInRoles.Owner, Operations.Update, Exists);
        var result = await handler.ProcessListRoleAssignmentsAsync(
            CreateHttpContext(),
            roleList,
            (_, _) => ValueTask.FromResult(true),
            CancellationToken.None
        );

        var json = Assert.IsType<JsonHttpResult<RoleListResource>>(result);
        var roleListResource = Assert.IsType<RoleListResource>(json.Value);
        var item = Assert.Single(roleListResource.Items);
        Assert.Equal("p1", item.PrincipalId);
        Assert.True(roleListResource.CanChange);
    }

    [Fact]
    public async Task ProcessListRoleAssignmentsAsync_WhenTheCallerMayNotChange_ReturnsTheAssignmentsWithoutCanChange()
    {
        PrincipalDirectoryMocks.SetupDescribingEveryPrincipal(MockPrincipalDirectory);
        var authz = Authz(AuthorizationResult.Success());
        var (factory, manager) = StoreManager();
        var ownership = MockRepository.Create<IResourceOwnershipService>();
        ownership
            .Setup(x =>
                x.GetAssignmentsAsync(
                    manager.Object,
                    ResourceNodeTypes.Client,
                    ResourceId,
                    BuiltInRoles.Owner,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync([Owner("p1")])
            .Verifiable();
        var node = ResourceNode.For(TenantId, ResourceNodeTypes.Client, ResourceId);

        var handler = CreateHandler(authz.Object, factory.Object, ownership.Object);
        var roleList = CreateRoleList(node, BuiltInRoles.Owner, Operations.Update, Exists);
        var result = await handler.ProcessListRoleAssignmentsAsync(
            CreateHttpContext(),
            roleList,
            (_, _) => ValueTask.FromResult(false),
            CancellationToken.None
        );

        var json = Assert.IsType<JsonHttpResult<RoleListResource>>(result);
        var roleListResource = Assert.IsType<RoleListResource>(json.Value);
        var item = Assert.Single(roleListResource.Items);
        Assert.Equal("p1", item.PrincipalId);
        Assert.False(roleListResource.CanChange);
    }

    [Fact]
    public async Task ProcessListRoleAssignmentsAsync_WhenSomeHoldersAreKnown_NamesThemAndLeavesTheUnknownOneNull()
    {
        var authorized = AuthorizationResult.Success();
        var authz = Authz(authorized);
        var (factory, manager) = StoreManager();
        var holder1 = Owner("p1", "a1");
        var holder2 = Owner("p2", "a2");
        var holder3 = Owner("p1", "a3");
        var ownership = MockRepository.Create<IResourceOwnershipService>();
        ownership
            .Setup(x =>
                x.GetAssignmentsAsync(
                    manager.Object,
                    ResourceNodeTypes.Client,
                    ResourceId,
                    BuiltInRoles.Owner,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync([holder1, holder2, holder3])
            .Verifiable();
        var aliceSummary = PrincipalDirectoryMocks.CreateSummary(
            "p1",
            PrincipalTypes.LocalAccount,
            "alice",
            "alice"
        );
        var directory = MockRepository.Create<IControlPlanePrincipalDirectory>();
        directory
            .Setup(x =>
                x.FindAsync(
                    It.Is<ControlPlanePrincipalQuery>(query =>
                        query.PrincipalIds.SequenceEqual(new[] { "p1", "p2", "p1" })
                        && query.UserNames.Count == 0
                    ),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync([aliceSummary])
            .Verifiable();
        var node = ResourceNode.For(TenantId, ResourceNodeTypes.Client, ResourceId);

        var handler = CreateHandler(
            authz.Object,
            factory.Object,
            ownership.Object,
            directory: directory.Object
        );
        var httpContext = CreateHttpContext();
        var roleList = CreateRoleList(node, BuiltInRoles.Owner, Operations.Update, Exists);
        var result = await handler.ProcessListRoleAssignmentsAsync(
            httpContext,
            roleList,
            (_, _) => ValueTask.FromResult(true),
            CancellationToken.None
        );

        var json = Assert.IsType<JsonHttpResult<RoleListResource>>(result);
        var roleListResource = Assert.IsType<RoleListResource>(json.Value);
        Assert.Equal(3, roleListResource.Items.Count);
        Assert.Equal("alice", roleListResource.Items[0].Principal?.UserName);
        Assert.Null(roleListResource.Items[1].Principal);
        Assert.Equal("alice", roleListResource.Items[2].Principal?.UserName);
    }

    [Fact]
    public async Task ProcessListRoleAssignmentsAsync_WhenNoOneHoldsTheRole_NamesNobodyWithoutAskingTheDirectory()
    {
        var authorized = AuthorizationResult.Success();
        var authz = Authz(authorized);
        var (factory, manager) = StoreManager();
        var ownership = MockRepository.Create<IResourceOwnershipService>();
        ownership
            .Setup(x =>
                x.GetAssignmentsAsync(
                    manager.Object,
                    ResourceNodeTypes.Client,
                    ResourceId,
                    BuiltInRoles.Owner,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync([])
            .Verifiable();
        // A strict directory with no setup fails the test when it is asked.
        var directory = MockRepository.Create<IControlPlanePrincipalDirectory>();
        var node = ResourceNode.For(TenantId, ResourceNodeTypes.Client, ResourceId);

        var handler = CreateHandler(
            authz.Object,
            factory.Object,
            ownership.Object,
            directory: directory.Object
        );
        var httpContext = CreateHttpContext();
        var roleList = CreateRoleList(node, BuiltInRoles.Owner, Operations.Update, Exists);
        var result = await handler.ProcessListRoleAssignmentsAsync(
            httpContext,
            roleList,
            (_, _) => ValueTask.FromResult(true),
            CancellationToken.None
        );

        var json = Assert.IsType<JsonHttpResult<RoleListResource>>(result);
        var roleListResource = Assert.IsType<RoleListResource>(json.Value);
        Assert.Empty(roleListResource.Items);
    }

    [Fact]
    public async Task ProcessListRoleAssignmentsAsync_WhenUnauthorized_ForbidsWithoutOpeningTheStore()
    {
        var authz = Authz(AuthorizationResult.Failed());
        // A forbidden request never opens the store.
        var factory = MockRepository.Create<IStoreManagerFactory>();
        var ownership = MockRepository.Create<IResourceOwnershipService>();
        var node = ResourceNode.For(TenantId, ResourceNodeTypes.Client, ResourceId);

        var handler = CreateHandler(authz.Object, factory.Object, ownership.Object);
        var roleList = CreateRoleList(node, BuiltInRoles.Owner, Operations.Update, Exists);
        var result = await handler.ProcessListRoleAssignmentsAsync(
            CreateHttpContext(),
            roleList,
            (_, _) => ValueTask.FromResult(true),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);

        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task ProcessListRoleAssignmentsAsync_WhenTheResourceIsMissing_ReturnsNotFound()
    {
        var authz = Authz(AuthorizationResult.Success());
        var (factory, _) = StoreManager();
        var ownership = MockRepository.Create<IResourceOwnershipService>();
        var node = ResourceNode.For(TenantId, ResourceNodeTypes.Client, ResourceId);

        var handler = CreateHandler(authz.Object, factory.Object, ownership.Object);
        var roleList = CreateRoleList(node, BuiltInRoles.Owner, Operations.Update, Missing);
        var result = await handler.ProcessListRoleAssignmentsAsync(
            CreateHttpContext(),
            roleList,
            (_, _) => throw new InvalidOperationException("canChangeAsync must not run."),
            CancellationToken.None
        );

        var notFound = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, notFound.StatusCode);
    }

    #endregion

    #region ProcessAddRoleAssignmentAsync Tests

    [Fact]
    public async Task ProcessAddRoleAssignmentAsync_WhenAdded_ReturnsCreatedBeneathTheRoleListAndRecordsCreated()
    {
        PrincipalDirectoryMocks.SetupDescribingEveryPrincipal(MockPrincipalDirectory);
        var authz = Authz(AuthorizationResult.Success());
        var (factory, manager) = StoreManager();
        manager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        var ownership = MockRepository.Create<IResourceOwnershipService>();
        ownership
            .Setup(x =>
                x.AddAssignmentAsync(
                    manager.Object,
                    TenantId,
                    ResourceNodeTypes.Client,
                    ResourceId,
                    "p2",
                    RoleName,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                new RoleAssignmentGrant
                {
                    Assignment = Assignment("p2", RoleName, "a2"),
                    IsAdded = true,
                }
            )
            .Verifiable();
        var captured = new List<RoleAssignmentChangedAuditEvent>();
        var recorder = Recorder(captured);
        var node = ResourceNode.For(TenantId, ResourceNodeTypes.Client, ResourceId);

        var handler = CreateHandler(
            authz.Object,
            factory.Object,
            ownership.Object,
            recorder.Object
        );
        var roleList = CreateRoleList(node, RoleName, Operations.Create, Exists);
        var result = await handler.ProcessAddRoleAssignmentAsync(
            CreateHttpContext(),
            roleList,
            new AddRoleHolderRequest { PrincipalId = "p2" },
            CancellationToken.None
        );

        var created = Assert.IsType<Created<RoleHolderResource>>(result);
        Assert.Equal("/clients/client-1/holders/p2", created.Location);
        var roleHolder = Assert.IsType<RoleHolderResource>(created.Value);
        Assert.Equal(RoleName, roleHolder.RoleName);
        var auditEvent = Assert.Single(captured);
        Assert.Equal(ResourceChangeTypes.Created, auditEvent.ChangeType);
        Assert.Equal("a2", auditEvent.ResourceId);
        Assert.Equal("p2", auditEvent.SubjectId);
    }

    [Fact]
    public async Task ProcessAddRoleAssignmentAsync_WhenAUserNameIsNamed_ResolvesThePrincipalAndGrants()
    {
        var authorized = AuthorizationResult.Success();
        var authz = Authz(authorized);
        var (factory, manager) = StoreManager();
        manager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        var bobSummary = PrincipalDirectoryMocks.CreateSummary(
            "p9",
            PrincipalTypes.LocalAccount,
            "bob",
            "bob"
        );
        var aliceSummary = PrincipalDirectoryMocks.CreateSummary(
            "p2",
            PrincipalTypes.LocalAccount,
            "alice",
            "alice"
        );
        var directory = MockRepository.Create<IControlPlanePrincipalDirectory>();
        directory
            .Setup(x =>
                x.FindAsync(
                    It.Is<ControlPlanePrincipalQuery>(query =>
                        query.UserNames.SequenceEqual(new[] { "alice" })
                        && query.PrincipalIds.Count == 0
                    ),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync([bobSummary, aliceSummary])
            .Verifiable();
        var assignment = Assignment("p2", RoleName, "a2");
        var grant = new RoleAssignmentGrant { Assignment = assignment, IsAdded = true };
        var ownership = MockRepository.Create<IResourceOwnershipService>();
        ownership
            .Setup(x =>
                x.AddAssignmentAsync(
                    manager.Object,
                    TenantId,
                    ResourceNodeTypes.Client,
                    ResourceId,
                    "p2",
                    RoleName,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(grant)
            .Verifiable();
        var captured = new List<RoleAssignmentChangedAuditEvent>();
        var recorder = Recorder(captured);
        var node = ResourceNode.For(TenantId, ResourceNodeTypes.Client, ResourceId);

        var handler = CreateHandler(
            authz.Object,
            factory.Object,
            ownership.Object,
            recorder.Object,
            directory.Object
        );
        var httpContext = CreateHttpContext();
        var roleList = CreateRoleList(node, RoleName, Operations.Create, Exists);
        var request = new AddRoleHolderRequest { UserName = "alice" };
        var result = await handler.ProcessAddRoleAssignmentAsync(
            httpContext,
            roleList,
            request,
            CancellationToken.None
        );

        var created = Assert.IsType<Created<RoleHolderResource>>(result);
        Assert.Equal("/clients/client-1/holders/p2", created.Location);
        var roleHolder = Assert.IsType<RoleHolderResource>(created.Value);
        Assert.Equal("p2", roleHolder.PrincipalId);
        Assert.Equal("alice", roleHolder.Principal?.UserName);
        Assert.Single(captured);
    }

    [Fact]
    public async Task ProcessAddRoleAssignmentAsync_WhenAUserNameDiffersInCase_GrantsTheAccount()
    {
        var authorized = AuthorizationResult.Success();
        var authz = Authz(authorized);
        var (factory, manager) = StoreManager();
        manager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        var aliceSummary = PrincipalDirectoryMocks.CreateSummary(
            "p2",
            PrincipalTypes.LocalAccount,
            "alice",
            "alice"
        );
        var directory = MockRepository.Create<IControlPlanePrincipalDirectory>();
        directory
            .Setup(x =>
                x.FindAsync(
                    It.Is<ControlPlanePrincipalQuery>(query =>
                        query.UserNames.SequenceEqual(new[] { "Alice" })
                        && query.PrincipalIds.Count == 0
                    ),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync([aliceSummary])
            .Verifiable();
        var assignment = Assignment("p2", RoleName, "a2");
        var grant = new RoleAssignmentGrant { Assignment = assignment, IsAdded = true };
        var ownership = MockRepository.Create<IResourceOwnershipService>();
        ownership
            .Setup(x =>
                x.AddAssignmentAsync(
                    manager.Object,
                    TenantId,
                    ResourceNodeTypes.Client,
                    ResourceId,
                    "p2",
                    RoleName,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(grant)
            .Verifiable();
        var captured = new List<RoleAssignmentChangedAuditEvent>();
        var recorder = Recorder(captured);
        var node = ResourceNode.For(TenantId, ResourceNodeTypes.Client, ResourceId);

        var handler = CreateHandler(
            authz.Object,
            factory.Object,
            ownership.Object,
            recorder.Object,
            directory.Object
        );
        var httpContext = CreateHttpContext();
        var roleList = CreateRoleList(node, RoleName, Operations.Create, Exists);
        var request = new AddRoleHolderRequest { UserName = "Alice" };
        var result = await handler.ProcessAddRoleAssignmentAsync(
            httpContext,
            roleList,
            request,
            CancellationToken.None
        );

        var created = Assert.IsType<Created<RoleHolderResource>>(result);
        var roleHolder = Assert.IsType<RoleHolderResource>(created.Value);
        Assert.Equal("p2", roleHolder.PrincipalId);
        Assert.Single(captured);
    }

    [Fact]
    public async Task ProcessAddRoleAssignmentAsync_WhenThePrincipalIdDiffersInCase_GrantsTheStoredPrincipal()
    {
        var authorized = AuthorizationResult.Success();
        var authz = Authz(authorized);
        var (factory, manager) = StoreManager();
        manager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        var storedSummary = PrincipalDirectoryMocks.CreateSummary(
            "p2",
            PrincipalTypes.Client,
            "p2"
        );
        var directory = MockRepository.Create<IControlPlanePrincipalDirectory>();
        directory
            .Setup(x =>
                x.FindAsync(
                    It.Is<ControlPlanePrincipalQuery>(query =>
                        query.PrincipalIds.SequenceEqual(new[] { "P2" })
                        && query.UserNames.Count == 0
                    ),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync([storedSummary])
            .Verifiable();
        var assignment = Assignment("p2", RoleName, "a2");
        var grant = new RoleAssignmentGrant { Assignment = assignment, IsAdded = true };
        var ownership = MockRepository.Create<IResourceOwnershipService>();
        ownership
            .Setup(x =>
                x.AddAssignmentAsync(
                    manager.Object,
                    TenantId,
                    ResourceNodeTypes.Client,
                    ResourceId,
                    "p2",
                    RoleName,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(grant)
            .Verifiable();
        var captured = new List<RoleAssignmentChangedAuditEvent>();
        var recorder = Recorder(captured);
        var node = ResourceNode.For(TenantId, ResourceNodeTypes.Client, ResourceId);

        var handler = CreateHandler(
            authz.Object,
            factory.Object,
            ownership.Object,
            recorder.Object,
            directory.Object
        );
        var httpContext = CreateHttpContext();
        var roleList = CreateRoleList(node, RoleName, Operations.Create, Exists);
        var request = new AddRoleHolderRequest { PrincipalId = "P2" };
        var result = await handler.ProcessAddRoleAssignmentAsync(
            httpContext,
            roleList,
            request,
            CancellationToken.None
        );

        var created = Assert.IsType<Created<RoleHolderResource>>(result);
        Assert.Equal($"{RoleListPath}/p2", created.Location);
        var roleHolder = Assert.IsType<RoleHolderResource>(created.Value);
        Assert.Equal("p2", roleHolder.PrincipalId);
        Assert.Single(captured);
    }

    [Fact]
    public async Task ProcessAddRoleAssignmentAsync_WhenNoPrincipalHasTheUserName_ReturnsBadRequestWithoutGranting()
    {
        var authorized = AuthorizationResult.Success();
        var authz = Authz(authorized);
        var (factory, _) = StoreManager();
        var bobSummary = PrincipalDirectoryMocks.CreateSummary(
            "p9",
            PrincipalTypes.LocalAccount,
            "bob",
            "bob"
        );
        var directory = MockRepository.Create<IControlPlanePrincipalDirectory>();
        directory
            .Setup(x =>
                x.FindAsync(It.IsAny<ControlPlanePrincipalQuery>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync([bobSummary])
            .Verifiable();
        // A strict ownership service with no setup fails the test when it grants.
        var ownership = MockRepository.Create<IResourceOwnershipService>();
        var node = ResourceNode.For(TenantId, ResourceNodeTypes.Client, ResourceId);

        var handler = CreateHandler(
            authz.Object,
            factory.Object,
            ownership.Object,
            directory: directory.Object
        );
        var httpContext = CreateHttpContext();
        var roleList = CreateRoleList(node, RoleName, Operations.Create, Exists);
        var request = new AddRoleHolderRequest { UserName = "alice" };
        var result = await handler.ProcessAddRoleAssignmentAsync(
            httpContext,
            roleList,
            request,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(
            "No principal of the control plane has the user name.",
            problem.ProblemDetails.Detail
        );
        Assert.DoesNotContain("alice", problem.ProblemDetails.Detail);
    }

    [Fact]
    public async Task ProcessAddRoleAssignmentAsync_WhenThePrincipalIsUnknown_ReturnsBadRequestWithoutGrantingOrAuditing()
    {
        var authorized = AuthorizationResult.Success();
        var authz = Authz(authorized);
        var (factory, _) = StoreManager();
        var directory = MockRepository.Create<IControlPlanePrincipalDirectory>();
        directory
            .Setup(x =>
                x.FindAsync(
                    It.Is<ControlPlanePrincipalQuery>(query =>
                        query.PrincipalIds.SequenceEqual(new[] { "ghost-principal" })
                    ),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync([])
            .Verifiable();
        // A strict ownership service and recorder with no setup fail the test when they grant or audit.
        var ownership = MockRepository.Create<IResourceOwnershipService>();
        var recorder = MockRepository.Create<IManagementAuditRecorder>();
        var node = ResourceNode.For(TenantId, ResourceNodeTypes.Client, ResourceId);

        var handler = CreateHandler(
            authz.Object,
            factory.Object,
            ownership.Object,
            recorder.Object,
            directory.Object
        );
        var httpContext = CreateHttpContext();
        var roleList = CreateRoleList(node, RoleName, Operations.Create, Exists);
        var request = new AddRoleHolderRequest { PrincipalId = "ghost-principal" };
        var result = await handler.ProcessAddRoleAssignmentAsync(
            httpContext,
            roleList,
            request,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(
            "No principal of the control plane has the identifier.",
            problem.ProblemDetails.Detail
        );
        Assert.DoesNotContain("ghost-principal", problem.ProblemDetails.Detail);
    }

    [Fact]
    public async Task ProcessAddRoleAssignmentAsync_WhenBothAreNamed_ReturnsBadRequestWithoutOpeningTheStore()
    {
        var authorized = AuthorizationResult.Success();
        var authz = Authz(authorized);
        // A request that names both or neither never opens the store.
        var factory = MockRepository.Create<IStoreManagerFactory>();
        var ownership = MockRepository.Create<IResourceOwnershipService>();
        var node = ResourceNode.For(TenantId, ResourceNodeTypes.Client, ResourceId);

        var handler = CreateHandler(authz.Object, factory.Object, ownership.Object);
        var httpContext = CreateHttpContext();
        var roleList = CreateRoleList(node, RoleName, Operations.Create, Exists);
        var request = new AddRoleHolderRequest { PrincipalId = "p2", UserName = "alice" };
        var result = await handler.ProcessAddRoleAssignmentAsync(
            httpContext,
            roleList,
            request,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(
            "Name exactly one of 'principalId' and 'userName'.",
            problem.ProblemDetails.Detail
        );
    }

    [Fact]
    public async Task ProcessAddRoleAssignmentAsync_WhenNeitherIsNamed_ReturnsBadRequestWithoutOpeningTheStore()
    {
        var authorized = AuthorizationResult.Success();
        var authz = Authz(authorized);
        // A request that names both or neither never opens the store.
        var factory = MockRepository.Create<IStoreManagerFactory>();
        var ownership = MockRepository.Create<IResourceOwnershipService>();
        var node = ResourceNode.For(TenantId, ResourceNodeTypes.Client, ResourceId);

        var handler = CreateHandler(authz.Object, factory.Object, ownership.Object);
        var httpContext = CreateHttpContext();
        var roleList = CreateRoleList(node, RoleName, Operations.Create, Exists);
        var request = new AddRoleHolderRequest { PrincipalId = null, UserName = null };
        var result = await handler.ProcessAddRoleAssignmentAsync(
            httpContext,
            roleList,
            request,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(
            "Name exactly one of 'principalId' and 'userName'.",
            problem.ProblemDetails.Detail
        );
    }

    [Fact]
    public async Task ProcessAddRoleAssignmentAsync_WhenAlreadyHeld_ReturnsOkAndRecordsNothing()
    {
        PrincipalDirectoryMocks.SetupDescribingEveryPrincipal(MockPrincipalDirectory);
        var authz = Authz(AuthorizationResult.Success());
        var (factory, manager) = StoreManager();
        manager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        var ownership = MockRepository.Create<IResourceOwnershipService>();
        ownership
            .Setup(x =>
                x.AddAssignmentAsync(
                    manager.Object,
                    TenantId,
                    ResourceNodeTypes.Client,
                    ResourceId,
                    "p2",
                    RoleName,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                new RoleAssignmentGrant
                {
                    Assignment = Assignment("p2", RoleName, "a2"),
                    IsAdded = false,
                }
            )
            .Verifiable();
        // A strict recorder with no setup fails the test when an event is recorded.
        var recorder = MockRepository.Create<IManagementAuditRecorder>();
        var node = ResourceNode.For(TenantId, ResourceNodeTypes.Client, ResourceId);

        var handler = CreateHandler(
            authz.Object,
            factory.Object,
            ownership.Object,
            recorder.Object
        );
        var roleList = CreateRoleList(node, RoleName, Operations.Create, Exists);
        var result = await handler.ProcessAddRoleAssignmentAsync(
            CreateHttpContext(),
            roleList,
            new AddRoleHolderRequest { PrincipalId = "p2" },
            CancellationToken.None
        );

        var ok = Assert.IsType<Ok<RoleHolderResource>>(result);
        var roleHolder = Assert.IsType<RoleHolderResource>(ok.Value);
        Assert.Equal("a2", roleHolder.AssignmentId);
    }

    [Fact]
    public async Task ProcessAddRoleAssignmentAsync_WhenThePrincipalCannotHoldTheRole_ReturnsConflictWithoutSavingOrRecording()
    {
        PrincipalDirectoryMocks.SetupDescribingEveryPrincipal(MockPrincipalDirectory);
        var authz = Authz(AuthorizationResult.Success());
        // A strict store manager with no save setup fails the test when changes are saved.
        var (factory, manager) = StoreManager();
        var ownership = MockRepository.Create<IResourceOwnershipService>();
        ownership
            .Setup(x =>
                x.AddAssignmentAsync(
                    manager.Object,
                    TenantId,
                    ResourceNodeTypes.Client,
                    ResourceId,
                    "p2",
                    RoleName,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new RoleAssignmentGrant { Assignment = null, IsAdded = false })
            .Verifiable();
        // A strict recorder with no setup fails the test when an event is recorded.
        var recorder = MockRepository.Create<IManagementAuditRecorder>();
        var node = ResourceNode.For(TenantId, ResourceNodeTypes.Client, ResourceId);

        var handler = CreateHandler(
            authz.Object,
            factory.Object,
            ownership.Object,
            recorder.Object
        );
        var roleList = CreateRoleList(node, RoleName, Operations.Create, Exists);
        var result = await handler.ProcessAddRoleAssignmentAsync(
            CreateHttpContext(),
            roleList,
            new AddRoleHolderRequest { PrincipalId = "p2" },
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    [Fact]
    public async Task ProcessAddRoleAssignmentAsync_WhenTheResourceIsMissing_ReturnsNotFound()
    {
        var authz = Authz(AuthorizationResult.Success());
        var (factory, _) = StoreManager();
        var ownership = MockRepository.Create<IResourceOwnershipService>();
        var node = ResourceNode.For(TenantId, ResourceNodeTypes.Client, ResourceId);

        var handler = CreateHandler(authz.Object, factory.Object, ownership.Object);
        var roleList = CreateRoleList(node, RoleName, Operations.Create, Missing);
        var result = await handler.ProcessAddRoleAssignmentAsync(
            CreateHttpContext(),
            roleList,
            new AddRoleHolderRequest { PrincipalId = "p2" },
            CancellationToken.None
        );

        var notFound = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, notFound.StatusCode);
    }

    [Fact]
    public async Task ProcessAddRoleAssignmentAsync_WhenThePrincipalIsMissing_ReturnsBadRequestWithoutOpeningTheStore()
    {
        var authz = Authz(AuthorizationResult.Success());
        // A request without a principal never opens the store.
        var factory = MockRepository.Create<IStoreManagerFactory>();
        var ownership = MockRepository.Create<IResourceOwnershipService>();
        var node = ResourceNode.For(TenantId, ResourceNodeTypes.Client, ResourceId);

        var handler = CreateHandler(authz.Object, factory.Object, ownership.Object);
        var roleList = CreateRoleList(node, RoleName, Operations.Create, Exists);
        var result = await handler.ProcessAddRoleAssignmentAsync(
            CreateHttpContext(),
            roleList,
            new AddRoleHolderRequest { PrincipalId = "" },
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    [Fact]
    public async Task ProcessAddRoleAssignmentAsync_WhenForbidden_ReturnsForbiddenWithoutOpeningTheStore()
    {
        var authz = Authz(AuthorizationResult.Failed());
        // A forbidden request never opens the store.
        var factory = MockRepository.Create<IStoreManagerFactory>();
        var ownership = MockRepository.Create<IResourceOwnershipService>();
        var node = ResourceNode.For(TenantId, ResourceNodeTypes.Client, ResourceId);

        var handler = CreateHandler(authz.Object, factory.Object, ownership.Object);
        var roleList = CreateRoleList(node, RoleName, Operations.Create, Exists);
        var result = await handler.ProcessAddRoleAssignmentAsync(
            CreateHttpContext(),
            roleList,
            new AddRoleHolderRequest { PrincipalId = "p2" },
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task ProcessAddRoleAssignmentAsync_AuthorizesTheOperationAtTheNode()
    {
        object? authorizedResource = null;
        IEnumerable<IAuthorizationRequirement>? authorizedRequirements = null;
        var authz = MockRepository.Create<IAuthorizationService>();
        authz
            .Setup(x =>
                x.AuthorizeAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<object?>(),
                    It.IsAny<IEnumerable<IAuthorizationRequirement>>()
                )
            )
            .Callback<ClaimsPrincipal, object?, IEnumerable<IAuthorizationRequirement>>(
                (_, resource, requirements) =>
                {
                    authorizedResource = resource;
                    authorizedRequirements = requirements;
                }
            )
            .ReturnsAsync(AuthorizationResult.Failed())
            .Verifiable();
        var factory = MockRepository.Create<IStoreManagerFactory>();
        var ownership = MockRepository.Create<IResourceOwnershipService>();
        var node = ResourceNode.For(TenantId, ResourceNodeTypes.Client, ResourceId);

        var handler = CreateHandler(authz.Object, factory.Object, ownership.Object);
        var roleList = CreateRoleList(node, RoleName, Operations.Create, Exists);
        await handler.ProcessAddRoleAssignmentAsync(
            CreateHttpContext(),
            roleList,
            new AddRoleHolderRequest { PrincipalId = "p2" },
            CancellationToken.None
        );

        Assert.Equal(node, authorizedResource);
        Assert.NotNull(authorizedRequirements);
        var requirement = Assert.Single(authorizedRequirements);
        Assert.Same(Operations.Create, requirement);
    }

    #endregion

    #region ProcessRemoveRoleAssignmentAsync Tests

    [Fact]
    public async Task ProcessRemoveRoleAssignmentAsync_WhenRemoved_ReturnsNoContentAndRecordsDeletedForEachRemoved()
    {
        var authz = Authz(AuthorizationResult.Success());
        var (factory, manager) = StoreManager();
        manager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        var ownership = MockRepository.Create<IResourceOwnershipService>();
        ownership
            .Setup(x =>
                x.RemoveAssignmentAsync(
                    manager.Object,
                    ResourceNodeTypes.Client,
                    ResourceId,
                    "p1",
                    RoleName,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync([Assignment("p1", RoleName, "a1"), Assignment("p1", RoleName, "a3")])
            .Verifiable();
        var captured = new List<RoleAssignmentChangedAuditEvent>();
        var recorder = Recorder(captured);
        var node = ResourceNode.For(TenantId, ResourceNodeTypes.Client, ResourceId);

        var handler = CreateHandler(
            authz.Object,
            factory.Object,
            ownership.Object,
            recorder.Object
        );
        var roleList = CreateRoleList(node, RoleName, Operations.Create, Exists);
        var result = await handler.ProcessRemoveRoleAssignmentAsync(
            CreateHttpContext(),
            roleList,
            "p1",
            CancellationToken.None
        );

        Assert.IsType<NoContent>(result);
        Assert.Equal(2, captured.Count);
        Assert.All(captured, x => Assert.Equal(ResourceChangeTypes.Deleted, x.ChangeType));
        var removedIds = captured.Select(x => x.ResourceId).ToList();
        Assert.Equal(["a1", "a3"], removedIds);
    }

    [Fact]
    public async Task ProcessRemoveRoleAssignmentAsync_WhenNotHeld_ReturnsNotFoundAndRecordsNothing()
    {
        var authz = Authz(AuthorizationResult.Success());
        // A strict store manager with no save setup fails the test when changes are saved.
        var (factory, manager) = StoreManager();
        var ownership = MockRepository.Create<IResourceOwnershipService>();
        ownership
            .Setup(x =>
                x.RemoveAssignmentAsync(
                    manager.Object,
                    ResourceNodeTypes.Client,
                    ResourceId,
                    "p1",
                    RoleName,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync([])
            .Verifiable();
        // A strict recorder with no setup fails the test when an event is recorded.
        var recorder = MockRepository.Create<IManagementAuditRecorder>();
        var node = ResourceNode.For(TenantId, ResourceNodeTypes.Client, ResourceId);

        var handler = CreateHandler(
            authz.Object,
            factory.Object,
            ownership.Object,
            recorder.Object
        );
        var roleList = CreateRoleList(node, RoleName, Operations.Create, Exists);
        var result = await handler.ProcessRemoveRoleAssignmentAsync(
            CreateHttpContext(),
            roleList,
            "p1",
            CancellationToken.None
        );

        var notFound = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, notFound.StatusCode);
    }

    [Fact]
    public async Task ProcessRemoveRoleAssignmentAsync_WhenForbidden_ReturnsForbiddenWithoutOpeningTheStore()
    {
        var authz = Authz(AuthorizationResult.Failed());
        // A forbidden request never opens the store.
        var factory = MockRepository.Create<IStoreManagerFactory>();
        var ownership = MockRepository.Create<IResourceOwnershipService>();
        var node = ResourceNode.For(TenantId, ResourceNodeTypes.Client, ResourceId);

        var handler = CreateHandler(authz.Object, factory.Object, ownership.Object);
        var roleList = CreateRoleList(node, RoleName, Operations.Create, Exists);
        var result = await handler.ProcessRemoveRoleAssignmentAsync(
            CreateHttpContext(),
            roleList,
            "p1",
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task ProcessRemoveRoleAssignmentAsync_AuthorizesTheOperationAtTheNode()
    {
        object? authorizedResource = null;
        IEnumerable<IAuthorizationRequirement>? authorizedRequirements = null;
        var authz = MockRepository.Create<IAuthorizationService>();
        authz
            .Setup(x =>
                x.AuthorizeAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<object?>(),
                    It.IsAny<IEnumerable<IAuthorizationRequirement>>()
                )
            )
            .Callback<ClaimsPrincipal, object?, IEnumerable<IAuthorizationRequirement>>(
                (_, resource, requirements) =>
                {
                    authorizedResource = resource;
                    authorizedRequirements = requirements;
                }
            )
            .ReturnsAsync(AuthorizationResult.Failed())
            .Verifiable();
        var factory = MockRepository.Create<IStoreManagerFactory>();
        var ownership = MockRepository.Create<IResourceOwnershipService>();
        var node = ResourceNode.For(TenantId, ResourceNodeTypes.Client, ResourceId);

        var handler = CreateHandler(authz.Object, factory.Object, ownership.Object);
        var roleList = CreateRoleList(node, RoleName, Operations.Create, Exists);
        await handler.ProcessRemoveRoleAssignmentAsync(
            CreateHttpContext(),
            roleList,
            "p1",
            CancellationToken.None
        );

        Assert.Equal(node, authorizedResource);
        Assert.NotNull(authorizedRequirements);
        var requirement = Assert.Single(authorizedRequirements);
        Assert.Same(Operations.Create, requirement);
    }

    #endregion

    #region HoldsAuthorityAsync Tests

    [Fact]
    public async Task HoldsAuthorityAsync_WhenAuthorized_ReturnsTrue()
    {
        var authz = Authz(AuthorizationResult.Success());
        var factory = MockRepository.Create<IStoreManagerFactory>();
        var ownership = MockRepository.Create<IResourceOwnershipService>();
        var node = ResourceNode.For(TenantId, ResourceNodeTypes.Client, ResourceId);

        var handler = CreateHandler(authz.Object, factory.Object, ownership.Object);
        var holds = await handler.HoldsAuthorityAsync(
            CreateHttpContext(),
            node,
            Operations.Update,
            CancellationToken.None
        );

        Assert.True(holds);
    }

    [Fact]
    public async Task HoldsAuthorityAsync_WhenForbidden_ReturnsFalse()
    {
        var authz = Authz(AuthorizationResult.Failed());
        var factory = MockRepository.Create<IStoreManagerFactory>();
        var ownership = MockRepository.Create<IResourceOwnershipService>();
        var node = ResourceNode.For(TenantId, ResourceNodeTypes.Client, ResourceId);

        var handler = CreateHandler(authz.Object, factory.Object, ownership.Object);
        var holds = await handler.HoldsAuthorityAsync(
            CreateHttpContext(),
            node,
            Operations.Update,
            CancellationToken.None
        );

        Assert.False(holds);
    }

    #endregion
}
