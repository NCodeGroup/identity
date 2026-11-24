// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.EntityFrameworkCore;
using NCode.Identity.OpenId.Persistence.EntityFramework.Core.Entities;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Core;

/// <summary>
/// Provides typed <see cref="DbSet{TEntity}"/> accessors for the core OpenID schema over the framework's
/// <c>DbSet</c>-less <see cref="OpenIdDbContext"/>. The accessors live with the slice that owns the entities,
/// so the framework context stays schema-agnostic while core store code keeps its readable table access.
/// </summary>
internal static class CoreDbContextExtensions
{
    extension(OpenIdDbContext context)
    {
        public DbSet<SecretEntity> Secrets => context.Set<SecretEntity>();

        public DbSet<MasterKeyEntity> MasterKeys => context.Set<MasterKeyEntity>();

        public DbSet<OwnerRootKeyEntity> OwnerRootKeys => context.Set<OwnerRootKeyEntity>();

        public DbSet<PlatformEntity> Platforms => context.Set<PlatformEntity>();

        public DbSet<InstanceSetupEntity> InstanceSetups => context.Set<InstanceSetupEntity>();

        public DbSet<DirectoryFeedCheckpointEntity> DirectoryFeedCheckpoints =>
            context.Set<DirectoryFeedCheckpointEntity>();

        public DbSet<ReplicatedDocumentEntity> ReplicatedDocuments =>
            context.Set<ReplicatedDocumentEntity>();

        public DbSet<RevokedManagementKeyEntity> RevokedManagementKeys =>
            context.Set<RevokedManagementKeyEntity>();

        public DbSet<TeamEntity> Teams => context.Set<TeamEntity>();

        public DbSet<RetiredTeamEntity> RetiredTeams => context.Set<RetiredTeamEntity>();

        public DbSet<TenantEntity> Tenants => context.Set<TenantEntity>();

        public DbSet<TenantSecretEntity> TenantSecrets => context.Set<TenantSecretEntity>();

        public DbSet<ClientEntity> Clients => context.Set<ClientEntity>();

        public DbSet<ClientSecretEntity> ClientSecrets => context.Set<ClientSecretEntity>();

        public DbSet<GrantEntity> Grants => context.Set<GrantEntity>();

        public DbSet<ConsentEntity> Consents => context.Set<ConsentEntity>();

        public DbSet<ConnectionEntity> Connections => context.Set<ConnectionEntity>();

        public DbSet<ResourceServerEntity> ResourceServers => context.Set<ResourceServerEntity>();

        public DbSet<ResourceServerSecretEntity> ResourceServerSecrets =>
            context.Set<ResourceServerSecretEntity>();

        public DbSet<ScopeEntity> Scopes => context.Set<ScopeEntity>();

        public DbSet<ClientGrantEntity> ClientGrants => context.Set<ClientGrantEntity>();

        public DbSet<RoleAssignmentEntity> RoleAssignments => context.Set<RoleAssignmentEntity>();

        public DbSet<FederatedPrincipalEntity> FederatedPrincipals =>
            context.Set<FederatedPrincipalEntity>();

        public DbSet<FederatedIdentityEntity> FederatedIdentities =>
            context.Set<FederatedIdentityEntity>();

        public DbSet<RetiredTenantEntity> RetiredTenants => context.Set<RetiredTenantEntity>();

        public DbSet<RetiredIssuerEntity> RetiredIssuers => context.Set<RetiredIssuerEntity>();

        public DbSet<TenantUsageEntity> TenantUsage => context.Set<TenantUsageEntity>();

        public DbSet<TenantUsageCheckpointEntity> TenantUsageCheckpoints =>
            context.Set<TenantUsageCheckpointEntity>();

        public DbSet<AuditRecordEntity> AuditRecords => context.Set<AuditRecordEntity>();

        public DbSet<AuditRecordResourceEntity> AuditRecordResources =>
            context.Set<AuditRecordResourceEntity>();

        public DbSet<AuditStreamEntity> AuditStreams => context.Set<AuditStreamEntity>();

        public DbSet<AuditStreamStateEntity> AuditStreamStates =>
            context.Set<AuditStreamStateEntity>();

        public DbSet<AuditStreamSigningSecretEntity> AuditStreamSigningSecrets =>
            context.Set<AuditStreamSigningSecretEntity>();
    }
}
