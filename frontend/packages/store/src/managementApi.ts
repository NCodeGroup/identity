import { createApi } from '@reduxjs/toolkit/query/react';
import type { FetchArgs, FetchBaseQueryError } from '@reduxjs/toolkit/query';
import {
  diffPatch,
  PLATFORM_SCOPE,
  LIST_TAG,
  ownerApiPath,
  ownerEffectiveTagId,
  ownerLimitsTagId,
  ownerScope,
  ownerTagId,
  replacePatch,
  roleListApiPath,
  roleListTagId,
  scopeTagId,
  teamScope,
  type AddRoleHolderRequest,
  type CallerResource,
  type CellResource,
  type ClientOwner,
  type ClientResource,
  type CollectionResource,
  type ConnectionResource,
  type ConnectionSettingsResource,
  type CreatedInitialAccessTokenResource,
  type CreatedSecretResource,
  type CreateClientRequest,
  type CreateConnectionRequest,
  type CreateInitialAccessTokenRequest,
  type CreateLocalAccountRequest,
  type CreateRemoteSecretRequest,
  type CreateResourceServerRequest,
  type CreateScopeRequest,
  type CreateSecretRequest,
  type RotateSecretRequest,
  type CreateTeamRequest,
  type CreateTenantRequest,
  type EffectiveSettingsResource,
  type EncryptionKeyOwner,
  type EncryptionKeyRotationResource,
  type EncryptionKeysResource,
  type EncryptionRootKeyChangeResource,
  type EncryptionRootKeyRequest,
  type EncryptionRootKeyResource,
  type GrantResource,
  type GrantRevocationResult,
  type ImportRemoteSecretRequest,
  type LocalAccountResource,
  type MachineClientOwner,
  type ManagementCapabilitiesResource,
  type ManagementOwner,
  type RoleHolderResource,
  type PromoteTenantRequest,
  type ResetLocalAccountPasswordRequest,
  type PublicKeyResource,
  type ResourceServerResource,
  type RoleList,
  type RoleListResource,
  type ScopeResource,
  type SecretResource,
  type SecretsResource,
  type SecretOwner,
  type SettingLimitsResource,
  type SettingsResource,
  type TeamResource,
  type TeamRootKeyResource,
  type TeamRootKeysResource,
  type TeamUsageResource,
  type TenantAddressResource,
  type TenantConfigurationDocument,
  type TenantExportRequest,
  type TenantExportResource,
  type TenantImportRequest,
  type TenantImportResource,
  type TenantPromotionResource,
  type TenantResource,
  type TenantTransferKeyResource,
  type TenantUsageResource,
  type UpdateClientFields,
  type UpdateConnectionFields,
  type UpdateLocalAccountFields,
  type UpdateResourceServerFields,
  type UpdateScopeRequest,
  type UpdateTeamFields,
  type UpdateTenantFields,
} from '@ncodegroup/identity-api';
import { dynamicBaseQuery, type ApiRootState, type CellFetchArgs } from './baseQuery.js';

/** The cell a tenant-scoped call addresses; `undefined` addresses the control plane. */
type CellArg = { readonly cell: string | undefined };

/** The arguments of a tenant-scoped call: the call's own arguments and the cell it addresses. */
type WithCell<T> = T & CellArg;

/** Addresses a call to `cell`, or to the control plane when there is none. */
const inCell = (cell: string | undefined, args: string | FetchArgs): CellFetchArgs => ({
  ...(typeof args === 'string' ? { url: args } : args),
  ...(cell ? { cell } : {}),
});

/** A tenant tagged with the cell it lives in; the cell is absent when the host forwards to no named cell. */
export type CellTenantResource = TenantResource & { readonly cell?: string };

/**
 * The tenants of every cell, merged. `unavailableCells` names each cell that did not answer: one cell's failure keeps
 * the tenants of the others.
 */
export type CellTenantsResource = {
  readonly items: readonly CellTenantResource[];
  readonly unavailableCells: readonly string[];
};

/** The answer of one call of a query: its data or its error. */
type CellQueryResult = { data?: unknown; error?: FetchBaseQueryError | undefined };

/**
 * Lists the tenants at `url` in every cell the console forwards to and merges them, each tagged with its cell. A cell
 * that fails is reported as unavailable. With no named cell the control plane answers alone, and its failure fails the
 * query.
 */
async function listTenantsInEveryCell(
  cells: readonly string[],
  url: string,
  fetchWithBQ: (args: CellFetchArgs) => CellQueryResult | PromiseLike<CellQueryResult>,
): Promise<
  | { data: CellTenantsResource; error?: undefined }
  | { error: FetchBaseQueryError; data?: undefined }
> {
  if (cells.length === 0) {
    const result = await fetchWithBQ({ url });
    if (result.error) {
      return { error: result.error };
    }
    const { items } = result.data as CollectionResource<TenantResource>;
    return { data: { items, unavailableCells: [] } };
  }

  const results = await Promise.all(
    cells.map(async (cell) => ({ cell, result: await fetchWithBQ({ url, cell }) })),
  );
  const items: CellTenantResource[] = [];
  const unavailableCells: string[] = [];
  for (const { cell, result } of results) {
    if (result.error) {
      unavailableCells.push(cell);
      continue;
    }
    const collection = result.data as CollectionResource<TenantResource>;
    items.push(...collection.items.map((tenant) => ({ ...tenant, cell })));
  }
  return { data: { items, unavailableCells } };
}

/** The names of the cells the console forwards to, from the loaded runtime configuration. */
const runtimeCells = (api: { getState: () => unknown }): readonly string[] => {
  const runtime = (api.getState() as ApiRootState).config.runtime;
  return runtime?.kind === 'bff' ? (runtime.cells ?? []) : [];
};

/** A cache-tag id in the platform scope, which owns the teams and identifies every tenant. */
const platformTag = (id: string): string => scopeTagId(PLATFORM_SCOPE, id);

/** A cache-tag id in a team's scope, which owns the lists of its tenants and of its child teams. */
const teamTag = (teamId: string, id: string): string => scopeTagId(teamScope(teamId), id);

/** A cache-tag id in a tenant's scope, which owns its grants, secrets, and settings. */
const tenantTag = (tenantId: string, id: string): string =>
  scopeTagId({ kind: 'tenant', tenantId }, id);

/**
 * The owner of a local-account list: the platform (which uses the platform tenant's default connection), or a local
 * connection of a tenant in the cell that holds it.
 */
export type LocalAccountOwner =
  | { readonly kind: 'platform' }
  | {
      readonly kind: 'tenant';
      readonly tenantId: string;
      readonly connectionId: string;
      readonly cell?: string | undefined;
    };

/** The local-accounts collection of `owner`, addressed to the owner's cell when it is a tenant in one. */
const localAccountsPath = (owner: LocalAccountOwner): CellFetchArgs =>
  owner.kind === 'platform'
    ? { url: 'api/platform/local-accounts' }
    : inCell(
        owner.cell,
        `api/tenants/${encodeURIComponent(owner.tenantId)}/connections/${encodeURIComponent(
          owner.connectionId,
        )}/local-accounts`,
      );

/** The cache-tag id of the list of local accounts that `owner` holds. */
const localAccountListTag = (owner: LocalAccountOwner): string =>
  owner.kind === 'platform'
    ? platformTag(LIST_TAG)
    : tenantTag(owner.tenantId, `${owner.connectionId}:${LIST_TAG}`);

/** The route of one tenant local account in its connection. */
const localAccountRoute = (
  {
    tenantId,
    connectionId,
    localAccountId,
  }: { tenantId: string; connectionId: string; localAccountId: string },
  suffix: string,
): string =>
  `api/tenants/${encodeURIComponent(tenantId)}/connections/${encodeURIComponent(
    connectionId,
  )}/local-accounts/${encodeURIComponent(localAccountId)}${suffix}`;

/** The arguments that name one tenant local account in its connection and cell. */
type LocalAccountArgs = WithCell<{
  tenantId: string;
  connectionId: string;
  localAccountId: string;
}>;

/** The route of one tenant local account, addressed to the cell that holds the tenant. */
const localAccountItem = (
  args: LocalAccountArgs,
  suffix: string,
  method: 'DELETE' | 'POST',
): CellFetchArgs => inCell(args.cell, { url: localAccountRoute(args, suffix), method });

/** The route of a connection, or of the connections collection, in a tenant. */
const connectionsPath = (tenantId: string, connectionId?: string, suffix = ''): string =>
  `api/tenants/${encodeURIComponent(tenantId)}/connections${
    connectionId === undefined ? '' : `/${encodeURIComponent(connectionId)}`
  }${suffix}`;

/** The cache-tag id of the settings document of a connection. */
const connectionSettingsTag = (tenantId: string, connectionId: string): string =>
  tenantTag(tenantId, `connection-settings:${connectionId}`);

/** The cache-tag id of the effective settings of a connection. */
const connectionEffectiveTag = (tenantId: string, connectionId: string): string =>
  tenantTag(tenantId, `connection-effective:${connectionId}`);

/** The cache-tag id of the setting limits of a connection. */
const connectionLimitsTag = (tenantId: string, connectionId: string): string =>
  tenantTag(tenantId, `connection-limits:${connectionId}`);

// The caller's own access spans scopes, so each mutation that can change where it holds a role invalidates it.
const callerTag = 'me';

/**
 * The RTK Query client for the Management REST API. Co-located with the store (one RTK module
 * boundary) so its middleware/reducer types reconcile with `configureStore`. Every cache-tag id is
 * derived from the scope that owns the resource ({@link scopeTagId}), so the platform, team and
 * tenant boundaries invalidate independently (see docs/decisions/1064): a mutation in one scope never
 * invalidates the caches of the platform scope, another team or another tenant.
 */
export const managementApi = createApi({
  reducerPath: 'managementApi',
  baseQuery: dynamicBaseQuery,
  tagTypes: [
    'Tenant',
    'Team',
    'Client',
    'LocalAccount',
    'Connection',
    'ResourceServer',
    'Grant',
    'Secret',
    'Settings',
    'SettingLimits',
    'RoleAssignment',
    'EncryptionKey',
    'TeamRootKey',
    'Cell',
    'Caller',
  ],
  endpoints: (build) => ({
    getCapabilities: build.query<ManagementCapabilitiesResource, void>({
      query: () => 'api/capabilities',
    }),
    getCaller: build.query<CallerResource, void>({
      query: () => 'api/me',
      providesTags: [{ type: 'Caller', id: callerTag }],
    }),
    listCallerTeams: build.query<CollectionResource<TeamResource>, void>({
      query: () => 'api/me/teams',
      providesTags: [{ type: 'Caller', id: callerTag }],
    }),
    // The caller's tenants in every cell, merged (see listTenantsInEveryCell).
    listCallerTenants: build.query<CellTenantsResource, void>({
      queryFn: (_arg, api, _extraOptions, fetchWithBQ) =>
        listTenantsInEveryCell(runtimeCells(api), 'api/me/tenants', fetchWithBQ),
      providesTags: [{ type: 'Caller', id: callerTag }],
    }),
    listCells: build.query<CellResource[], void>({
      query: () => 'api/cells',
      providesTags: [{ type: 'Cell', id: LIST_TAG }],
    }),
    listTeams: build.query<CollectionResource<TeamResource>, void>({
      query: () => 'api/teams',
      providesTags: (result) => [
        { type: 'Team' as const, id: platformTag(LIST_TAG) },
        ...(result?.items ?? []).map((t) => ({
          type: 'Team' as const,
          id: platformTag(t.teamId),
        })),
      ],
    }),
    getTeam: build.query<TeamResource, string>({
      query: (teamId) => `api/teams/${encodeURIComponent(teamId)}`,
      providesTags: (_result, _error, teamId) => [{ type: 'Team', id: platformTag(teamId) }],
    }),
    createTeam: build.mutation<TeamResource, CreateTeamRequest>({
      query: (body) => ({ url: 'api/teams', method: 'POST', body }),
      invalidatesTags: [
        { type: 'Team', id: platformTag(LIST_TAG) },
        { type: 'Caller', id: callerTag },
      ],
    }),
    // The child teams of one team; each child's own tag lives in the platform scope, where its id is unique.
    listChildTeams: build.query<CollectionResource<TeamResource>, { teamId: string }>({
      query: ({ teamId }) => `api/teams/${encodeURIComponent(teamId)}/teams`,
      providesTags: (result, _error, { teamId }) => [
        { type: 'Team' as const, id: teamTag(teamId, LIST_TAG) },
        ...(result?.items ?? []).map((t) => ({
          type: 'Team' as const,
          id: platformTag(t.teamId),
        })),
      ],
    }),
    createChildTeam: build.mutation<TeamResource, { teamId: string; body: CreateTeamRequest }>({
      query: ({ teamId, body }) => ({
        url: `api/teams/${encodeURIComponent(teamId)}/teams`,
        method: 'POST',
        body,
      }),
      invalidatesTags: (_result, _error, { teamId }) => [
        { type: 'Team', id: teamTag(teamId, LIST_TAG) },
        { type: 'Team', id: platformTag(LIST_TAG) },
        { type: 'Caller', id: callerTag },
      ],
    }),
    updateTeam: build.mutation<
      void,
      { teamId: string; concurrencyToken: string; fields: UpdateTeamFields }
    >({
      query: ({ teamId, concurrencyToken, fields }) => ({
        url: `api/teams/${encodeURIComponent(teamId)}`,
        method: 'PATCH',
        body: replacePatch({ ...fields }),
        headers: { 'If-Match': concurrencyToken },
      }),
      invalidatesTags: (_result, _error, { teamId }) => [
        { type: 'Team', id: platformTag(LIST_TAG) },
        { type: 'Team', id: platformTag(teamId) },
        { type: 'Caller', id: callerTag },
      ],
    }),
    deleteTeam: build.mutation<void, { teamId: string }>({
      query: ({ teamId }) => ({
        url: `api/teams/${encodeURIComponent(teamId)}`,
        method: 'DELETE',
      }),
      invalidatesTags: (_result, _error, { teamId }) => [
        { type: 'Team', id: platformTag(LIST_TAG) },
        { type: 'Team', id: platformTag(teamId) },
        { type: 'Caller', id: callerTag },
      ],
    }),
    // The usage ledger of a team's tenants grows with every flush of the server (ADR-1199), so nothing is cached once
    // unused: every mount reads the current ledger.
    getTeamUsage: build.query<
      TeamUsageResource,
      { teamId: string; from?: string | undefined; to?: string | undefined }
    >({
      query: ({ teamId, from, to }) => {
        const params = new URLSearchParams();
        if (from) params.set('from', from);
        if (to) params.set('to', to);
        const search = params.toString();
        return `api/teams/${encodeURIComponent(teamId)}/usage${search ? `?${search}` : ''}`;
      },
      keepUnusedDataFor: 0,
    }),
    // The tenants of one team in every cell, merged (see listTenantsInEveryCell); each tenant's own tag lives in the
    // platform scope, where its id is unique.
    listTenants: build.query<CellTenantsResource, { teamId: string }>({
      queryFn: ({ teamId }, api, _extraOptions, fetchWithBQ) =>
        listTenantsInEveryCell(
          runtimeCells(api),
          `api/teams/${encodeURIComponent(teamId)}/tenants`,
          fetchWithBQ,
        ),
      providesTags: (result, _error, { teamId }) => [
        { type: 'Tenant' as const, id: teamTag(teamId, LIST_TAG) },
        ...(result?.items ?? []).map((t) => ({
          type: 'Tenant' as const,
          id: platformTag(t.tenantId),
        })),
      ],
    }),
    getTenant: build.query<TenantResource, WithCell<{ tenantId: string }>>({
      query: ({ tenantId, cell }) => inCell(cell, `api/tenants/${encodeURIComponent(tenantId)}`),
      providesTags: (_result, _error, { tenantId }) => [
        { type: 'Tenant', id: platformTag(tenantId) },
      ],
    }),
    // The configuration a tenant exports for promotion (settings, clients, client grants and resource servers, never
    // secrets), read from the tenant's own cell. It is never cached beyond its subscriber, so a promotion always
    // carries the configuration as it is when asked.
    getTenantConfiguration: build.query<
      TenantConfigurationDocument,
      WithCell<{ tenantId: string }>
    >({
      query: ({ tenantId, cell }) =>
        inCell(cell, `api/tenants/${encodeURIComponent(tenantId)}/configuration`),
      keepUnusedDataFor: 0,
    }),
    // A preview of the address an existing tenant has, or would have with a proposed issuer or alias. It is never
    // cached beyond its subscriber, so it always reflects the tenants that exist when it is asked.
    getTenantAddress: build.query<
      TenantAddressResource,
      WithCell<{ tenantId: string; issuer?: string; mtlsEndpointAliasAuthority?: string }>
    >({
      query: ({ tenantId, issuer, mtlsEndpointAliasAuthority, cell }) => {
        const params = {
          ...(issuer ? { issuer } : {}),
          ...(mtlsEndpointAliasAuthority ? { mtlsEndpointAliasAuthority } : {}),
        };
        return inCell(cell, {
          url: `api/tenants/${encodeURIComponent(tenantId)}/address`,
          ...(Object.keys(params).length > 0 ? { params } : {}),
        });
      },
      keepUnusedDataFor: 0,
    }),
    // A preview of the address a tenant that does not exist yet would have in a team. It is never cached beyond its
    // subscriber, so it always reflects the tenants that exist when it is asked.
    getNewTenantAddress: build.query<
      TenantAddressResource,
      WithCell<{
        teamId: string;
        tenantId: string;
        issuer?: string;
        mtlsEndpointAliasAuthority?: string;
      }>
    >({
      query: ({ teamId, tenantId, issuer, mtlsEndpointAliasAuthority, cell }) => {
        const params = {
          ...(issuer ? { issuer } : {}),
          ...(mtlsEndpointAliasAuthority ? { mtlsEndpointAliasAuthority } : {}),
        };
        return inCell(cell, {
          url: `api/teams/${encodeURIComponent(teamId)}/tenants/${encodeURIComponent(tenantId)}/address`,
          ...(Object.keys(params).length > 0 ? { params } : {}),
        });
      },
      keepUnusedDataFor: 0,
    }),
    // `resourceServerId` lists only the clients that act for that resource server.
    listClients: build.query<
      CollectionResource<ClientResource>,
      WithCell<{ tenantId: string; resourceServerId?: string }>
    >({
      query: ({ tenantId, resourceServerId, cell }) =>
        inCell(cell, {
          url: `api/tenants/${encodeURIComponent(tenantId)}/clients`,
          ...(resourceServerId ? { params: { resourceServerId } } : {}),
        }),
      providesTags: (result, _error, { tenantId }) => [
        { type: 'Client' as const, id: tenantTag(tenantId, LIST_TAG) },
        ...(result?.items ?? []).map((c) => ({
          type: 'Client' as const,
          id: tenantTag(tenantId, c.clientId),
        })),
      ],
    }),
    createTenant: build.mutation<
      TenantResource,
      WithCell<{ teamId: string; body: CreateTenantRequest }>
    >({
      query: ({ teamId, body, cell }) =>
        inCell(cell, {
          url: `api/teams/${encodeURIComponent(teamId)}/tenants`,
          method: 'POST',
          body,
        }),
      invalidatesTags: (_result, _error, { teamId }) => [
        { type: 'Tenant', id: teamTag(teamId, LIST_TAG) },
        { type: 'Caller', id: callerTag },
      ],
    }),
    updateTenant: build.mutation<
      void,
      WithCell<{
        teamId: string;
        tenantId: string;
        concurrencyToken: string;
        fields: UpdateTenantFields;
      }>
    >({
      query: ({ tenantId, concurrencyToken, fields, cell }) =>
        inCell(cell, {
          url: `api/tenants/${encodeURIComponent(tenantId)}`,
          method: 'PATCH',
          body: replacePatch({ ...fields }),
          headers: { 'If-Match': concurrencyToken },
        }),
      invalidatesTags: (_result, _error, { teamId, tenantId }) => [
        { type: 'Tenant', id: teamTag(teamId, LIST_TAG) },
        { type: 'Tenant', id: platformTag(tenantId) },
        { type: 'Caller', id: callerTag },
      ],
    }),
    deleteTenant: build.mutation<void, WithCell<{ teamId: string; tenantId: string }>>({
      query: ({ tenantId, cell }) =>
        inCell(cell, {
          url: `api/tenants/${encodeURIComponent(tenantId)}`,
          method: 'DELETE',
        }),
      invalidatesTags: (_result, _error, { teamId, tenantId }) => [
        { type: 'Tenant', id: teamTag(teamId, LIST_TAG) },
        { type: 'Tenant', id: platformTag(tenantId) },
        { type: 'Caller', id: callerTag },
      ],
    }),
    // A promotion runs in the target tenant's cell; the body names its source, a tenant of that cell or an exported
    // configuration.
    previewTenantPromotion: build.mutation<
      TenantPromotionResource,
      WithCell<{ tenantId: string; body: PromoteTenantRequest }>
    >({
      query: ({ tenantId, body, cell }) =>
        inCell(cell, {
          url: `api/tenants/${encodeURIComponent(tenantId)}/promotions/preview`,
          method: 'POST',
          body,
        }),
    }),
    // A promotion writes the target tenant's settings, clients and resource servers, so it refreshes the target's own
    // tags and, for every client and resource server it reports, the item and its settings.
    promoteTenant: build.mutation<
      TenantPromotionResource,
      WithCell<{ tenantId: string; body: PromoteTenantRequest }>
    >({
      query: ({ tenantId, body, cell }) =>
        inCell(cell, {
          url: `api/tenants/${encodeURIComponent(tenantId)}/promotions`,
          method: 'POST',
          body,
        }),
      invalidatesTags: (result, _error, { tenantId }) => {
        const tags: { type: 'Tenant' | 'Client' | 'ResourceServer' | 'Settings'; id: string }[] = [
          { type: 'Tenant', id: platformTag(tenantId) },
          { type: 'Settings', id: ownerTagId({ kind: 'tenant', tenantId }) },
          { type: 'Client', id: tenantTag(tenantId, LIST_TAG) },
          { type: 'ResourceServer', id: tenantTag(tenantId, LIST_TAG) },
        ];
        for (const change of result?.changes ?? []) {
          if (change.resourceType === 'client') {
            tags.push(
              { type: 'Client', id: tenantTag(tenantId, change.resourceId) },
              {
                type: 'Settings',
                id: ownerTagId({ kind: 'client', tenantId, clientId: change.resourceId }),
              },
            );
          } else if (change.resourceType === 'resource_server') {
            tags.push(
              { type: 'ResourceServer', id: tenantTag(tenantId, change.resourceId) },
              {
                type: 'Settings',
                id: ownerTagId({
                  kind: 'resource-server',
                  tenantId,
                  resourceServerId: change.resourceId,
                }),
              },
            );
          }
        }
        return tags;
      },
    }),
    // A move runs in four calls, each addressed to the cell it concerns: the receiving cell issues a one-time transfer
    // key, the tenant's own cell exports to it, the receiving cell imports the export and the tenant's own cell
    // releases what it still holds. The import and the release change where the tenant lives, so each refreshes the
    // tenant's tags, which every list that holds it provides, and the caller's access.
    createTransferKey: build.mutation<TenantTransferKeyResource, { cell: string }>({
      query: ({ cell }) =>
        inCell(cell, { url: 'api/tenant-imports/transfer-keys', method: 'POST' }),
    }),
    exportTenant: build.mutation<
      TenantExportResource,
      {
        cell: string;
        tenantId: string;
        targetCell: TenantExportRequest['targetCell'];
        jwk: TenantExportRequest['jwk'];
      }
    >({
      query: ({ cell, tenantId, targetCell, jwk }) =>
        inCell(cell, {
          url: `api/tenants/${encodeURIComponent(tenantId)}/export`,
          method: 'POST',
          body: { jwk, targetCell } satisfies TenantExportRequest,
        }),
    }),
    importTenant: build.mutation<
      TenantImportResource,
      { cell: string; kid: string; package: string }
    >({
      query: ({ cell, kid, package: sealedPackage }) =>
        inCell(cell, {
          url: 'api/tenant-imports',
          method: 'POST',
          body: { kid, package: sealedPackage } satisfies TenantImportRequest,
        }),
      invalidatesTags: (result) =>
        result
          ? [
              { type: 'Tenant', id: platformTag(result.tenantId) },
              { type: 'Caller', id: callerTag },
            ]
          : [],
    }),
    releaseMovedTenant: build.mutation<void, { cell: string; tenantId: string }>({
      query: ({ cell, tenantId }) =>
        inCell(cell, {
          url: `api/tenants/${encodeURIComponent(tenantId)}/release-moved`,
          method: 'POST',
        }),
      invalidatesTags: (_result, _error, { tenantId }) => [
        { type: 'Tenant', id: platformTag(tenantId) },
        { type: 'Caller', id: callerTag },
      ],
    }),
    createClient: build.mutation<
      ClientResource,
      WithCell<{ tenantId: string; body: CreateClientRequest }>
    >({
      query: ({ tenantId, body, cell }) =>
        inCell(cell, {
          url: `api/tenants/${encodeURIComponent(tenantId)}/clients`,
          method: 'POST',
          body,
        }),
      invalidatesTags: (_result, _error, { tenantId }) => [
        { type: 'Client', id: tenantTag(tenantId, LIST_TAG) },
      ],
    }),
    updateClient: build.mutation<
      void,
      WithCell<{
        tenantId: string;
        clientId: string;
        concurrencyToken: string;
        fields: UpdateClientFields;
      }>
    >({
      query: ({ tenantId, clientId, concurrencyToken, fields, cell }) =>
        inCell(cell, {
          url: `api/tenants/${encodeURIComponent(tenantId)}/clients/${encodeURIComponent(clientId)}`,
          method: 'PATCH',
          body: replacePatch({ ...fields }),
          headers: { 'If-Match': concurrencyToken },
        }),
      invalidatesTags: (_result, _error, { tenantId, clientId }) => [
        { type: 'Client', id: tenantTag(tenantId, LIST_TAG) },
        { type: 'Client', id: tenantTag(tenantId, clientId) },
      ],
    }),
    deleteClient: build.mutation<void, WithCell<{ tenantId: string; clientId: string }>>({
      query: ({ tenantId, clientId, cell }) =>
        inCell(cell, {
          url: `api/tenants/${encodeURIComponent(tenantId)}/clients/${encodeURIComponent(clientId)}`,
          method: 'DELETE',
        }),
      invalidatesTags: (_result, _error, { tenantId, clientId }) => [
        { type: 'Client', id: tenantTag(tenantId, LIST_TAG) },
        { type: 'Client', id: tenantTag(tenantId, clientId) },
      ],
    }),
    // The machine clients the platform or a team owns (ADR-1466). Each lives in the control-plane tenant, so its item
    // tag is that tenant's and a change through the tenant's client routes refetches the owner's list.
    listMachineClients: build.query<CollectionResource<ClientResource>, MachineClientOwner>({
      query: (owner) => `${ownerApiPath(owner)}/clients`,
      providesTags: (result, _error, owner) => [
        { type: 'Client' as const, id: scopeTagId(ownerScope(owner), LIST_TAG) },
        ...(result?.items ?? []).map((c) => ({
          type: 'Client' as const,
          id: tenantTag(c.tenantId, c.clientId),
        })),
      ],
    }),
    createMachineClient: build.mutation<
      ClientResource,
      { owner: MachineClientOwner; body: CreateClientRequest }
    >({
      query: ({ owner, body }) => ({
        url: `${ownerApiPath(owner)}/clients`,
        method: 'POST',
        body,
      }),
      invalidatesTags: (result, _error, { owner }) => [
        { type: 'Client' as const, id: scopeTagId(ownerScope(owner), LIST_TAG) },
        ...(result ? [{ type: 'Client' as const, id: tenantTag(result.tenantId, LIST_TAG) }] : []),
      ],
    }),
    listLocalAccounts: build.query<CollectionResource<LocalAccountResource>, LocalAccountOwner>({
      query: (owner) => localAccountsPath(owner),
      providesTags: (result, _error, owner) => [
        { type: 'LocalAccount' as const, id: localAccountListTag(owner) },
        ...(result?.items ?? []).map((a) => ({
          type: 'LocalAccount' as const,
          id: tenantTag(a.tenantId, a.localAccountId),
        })),
      ],
    }),
    createLocalAccount: build.mutation<
      LocalAccountResource,
      { owner: LocalAccountOwner; body: CreateLocalAccountRequest }
    >({
      query: ({ owner, body }) => ({ ...localAccountsPath(owner), method: 'POST', body }),
      invalidatesTags: (_result, _error, { owner }) => [
        { type: 'LocalAccount', id: localAccountListTag(owner) },
      ],
    }),
    updateLocalAccount: build.mutation<
      void,
      LocalAccountArgs & {
        concurrencyToken: string;
        fields: UpdateLocalAccountFields;
      }
    >({
      query: (args) =>
        inCell(args.cell, {
          url: localAccountRoute(args, ''),
          method: 'PATCH',
          body: replacePatch({ ...args.fields }),
          headers: { 'If-Match': args.concurrencyToken },
        }),
      invalidatesTags: (_result, _error, { tenantId, localAccountId }) => [
        { type: 'LocalAccount', id: tenantTag(tenantId, localAccountId) },
      ],
    }),
    deleteLocalAccount: build.mutation<void, LocalAccountArgs>({
      query: (args) => localAccountItem(args, '', 'DELETE'),
      invalidatesTags: (_result, _error, { tenantId, localAccountId }) => [
        { type: 'LocalAccount', id: tenantTag(tenantId, localAccountId) },
      ],
    }),
    enableLocalAccount: build.mutation<void, LocalAccountArgs>({
      query: (args) => localAccountItem(args, '/enable', 'POST'),
      invalidatesTags: (_result, _error, { tenantId, localAccountId }) => [
        { type: 'LocalAccount', id: tenantTag(tenantId, localAccountId) },
      ],
    }),
    disableLocalAccount: build.mutation<void, LocalAccountArgs>({
      query: (args) => localAccountItem(args, '/disable', 'POST'),
      invalidatesTags: (_result, _error, { tenantId, localAccountId }) => [
        { type: 'LocalAccount', id: tenantTag(tenantId, localAccountId) },
      ],
    }),
    resetLocalAccountPassword: build.mutation<
      void,
      LocalAccountArgs & { body: ResetLocalAccountPasswordRequest }
    >({
      query: (args) =>
        inCell(args.cell, {
          url: localAccountRoute(args, '/password'),
          method: 'POST',
          body: args.body,
        }),
      invalidatesTags: (_result, _error, { tenantId, localAccountId }) => [
        { type: 'LocalAccount', id: tenantTag(tenantId, localAccountId) },
      ],
    }),
    listConnections: build.query<
      CollectionResource<ConnectionResource>,
      WithCell<{ tenantId: string }>
    >({
      query: ({ tenantId, cell }) => inCell(cell, connectionsPath(tenantId)),
      providesTags: (result, _error, { tenantId }) => [
        { type: 'Connection' as const, id: tenantTag(tenantId, LIST_TAG) },
        ...(result?.items ?? []).map((c) => ({
          type: 'Connection' as const,
          id: tenantTag(tenantId, c.connectionId),
        })),
      ],
    }),
    getConnection: build.query<
      ConnectionResource,
      WithCell<{ tenantId: string; connectionId: string }>
    >({
      query: ({ tenantId, connectionId, cell }) =>
        inCell(cell, connectionsPath(tenantId, connectionId)),
      providesTags: (_result, _error, { tenantId, connectionId }) => [
        { type: 'Connection' as const, id: tenantTag(tenantId, connectionId) },
      ],
    }),
    createConnection: build.mutation<
      ConnectionResource,
      WithCell<{ tenantId: string; body: CreateConnectionRequest }>
    >({
      query: ({ tenantId, body, cell }) =>
        inCell(cell, { url: connectionsPath(tenantId), method: 'POST', body }),
      invalidatesTags: (_result, _error, { tenantId }) => [
        { type: 'Connection', id: tenantTag(tenantId, LIST_TAG) },
      ],
    }),
    updateConnection: build.mutation<
      void,
      WithCell<{
        tenantId: string;
        connectionId: string;
        concurrencyToken: string;
        fields: UpdateConnectionFields;
      }>
    >({
      query: ({ tenantId, connectionId, concurrencyToken, fields, cell }) =>
        inCell(cell, {
          url: connectionsPath(tenantId, connectionId),
          method: 'PATCH',
          body: replacePatch({ ...fields }),
          headers: { 'If-Match': concurrencyToken },
        }),
      invalidatesTags: (_result, _error, { tenantId, connectionId }) => [
        { type: 'Connection', id: tenantTag(tenantId, LIST_TAG) },
        { type: 'Connection', id: tenantTag(tenantId, connectionId) },
        { type: 'Settings', id: connectionEffectiveTag(tenantId, connectionId) },
        { type: 'SettingLimits', id: connectionLimitsTag(tenantId, connectionId) },
      ],
    }),
    deleteConnection: build.mutation<void, WithCell<{ tenantId: string; connectionId: string }>>({
      query: ({ tenantId, connectionId, cell }) =>
        inCell(cell, { url: connectionsPath(tenantId, connectionId), method: 'DELETE' }),
      invalidatesTags: (_result, _error, { tenantId, connectionId }) => [
        { type: 'Connection', id: tenantTag(tenantId, LIST_TAG) },
        { type: 'Connection', id: tenantTag(tenantId, connectionId) },
      ],
    }),
    getConnectionSettings: build.query<
      ConnectionSettingsResource,
      WithCell<{ tenantId: string; connectionId: string }>
    >({
      query: ({ tenantId, connectionId, cell }) =>
        inCell(cell, connectionsPath(tenantId, connectionId, '/settings')),
      providesTags: (_result, _error, { tenantId, connectionId }) => [
        { type: 'Settings' as const, id: connectionSettingsTag(tenantId, connectionId) },
      ],
    }),
    getConnectionEffectiveSettings: build.query<
      EffectiveSettingsResource,
      WithCell<{ tenantId: string; connectionId: string }>
    >({
      query: ({ tenantId, connectionId, cell }) =>
        inCell(cell, connectionsPath(tenantId, connectionId, '/effective-settings')),
      providesTags: (_result, _error, { tenantId, connectionId }) => [
        { type: 'Settings' as const, id: connectionEffectiveTag(tenantId, connectionId) },
      ],
    }),
    getConnectionSettingLimits: build.query<
      SettingLimitsResource,
      WithCell<{ tenantId: string; connectionId: string }>
    >({
      query: ({ tenantId, connectionId, cell }) =>
        inCell(cell, connectionsPath(tenantId, connectionId, '/settings/limits')),
      providesTags: (_result, _error, { tenantId, connectionId }) => [
        { type: 'SettingLimits' as const, id: connectionLimitsTag(tenantId, connectionId) },
      ],
    }),
    // Connection settings are an opaque object, so updates are a top-level add/remove/replace diff.
    updateConnectionSettings: build.mutation<
      void,
      WithCell<{
        tenantId: string;
        connectionId: string;
        concurrencyToken: string;
        original: Readonly<Record<string, unknown>>;
        desired: Readonly<Record<string, unknown>>;
      }>
    >({
      query: ({ tenantId, connectionId, concurrencyToken, original, desired, cell }) =>
        inCell(cell, {
          url: connectionsPath(tenantId, connectionId, '/settings'),
          method: 'PATCH',
          body: diffPatch(original, desired),
          headers: { 'If-Match': concurrencyToken },
        }),
      invalidatesTags: (_result, _error, { tenantId, connectionId }) => [
        { type: 'Settings', id: connectionSettingsTag(tenantId, connectionId) },
        { type: 'Settings', id: connectionEffectiveTag(tenantId, connectionId) },
        { type: 'SettingLimits', id: connectionLimitsTag(tenantId, connectionId) },
      ],
    }),
    listResourceServers: build.query<
      CollectionResource<ResourceServerResource>,
      WithCell<{ tenantId: string }>
    >({
      query: ({ tenantId, cell }) =>
        inCell(cell, `api/tenants/${encodeURIComponent(tenantId)}/resource-servers`),
      providesTags: (result, _error, { tenantId }) => [
        { type: 'ResourceServer' as const, id: tenantTag(tenantId, LIST_TAG) },
        ...(result?.items ?? []).map((r) => ({
          type: 'ResourceServer' as const,
          id: tenantTag(tenantId, r.resourceServerId),
        })),
      ],
    }),
    createResourceServer: build.mutation<
      ResourceServerResource,
      WithCell<{ tenantId: string; body: CreateResourceServerRequest }>
    >({
      query: ({ tenantId, body, cell }) =>
        inCell(cell, {
          url: `api/tenants/${encodeURIComponent(tenantId)}/resource-servers`,
          method: 'POST',
          body,
        }),
      invalidatesTags: (_result, _error, { tenantId }) => [
        { type: 'ResourceServer', id: tenantTag(tenantId, LIST_TAG) },
      ],
    }),
    updateResourceServer: build.mutation<
      void,
      WithCell<{
        tenantId: string;
        resourceServerId: string;
        concurrencyToken: string;
        fields: UpdateResourceServerFields;
      }>
    >({
      query: ({ tenantId, resourceServerId, concurrencyToken, fields, cell }) =>
        inCell(cell, {
          url: `api/tenants/${encodeURIComponent(tenantId)}/resource-servers/${encodeURIComponent(
            resourceServerId,
          )}`,
          method: 'PATCH',
          body: replacePatch({ ...fields }),
          headers: { 'If-Match': concurrencyToken },
        }),
      invalidatesTags: (_result, _error, { tenantId, resourceServerId }) => [
        { type: 'ResourceServer', id: tenantTag(tenantId, LIST_TAG) },
        { type: 'ResourceServer', id: tenantTag(tenantId, resourceServerId) },
      ],
    }),
    deleteResourceServer: build.mutation<
      void,
      WithCell<{ tenantId: string; resourceServerId: string }>
    >({
      query: ({ tenantId, resourceServerId, cell }) =>
        inCell(cell, {
          url: `api/tenants/${encodeURIComponent(tenantId)}/resource-servers/${encodeURIComponent(
            resourceServerId,
          )}`,
          method: 'DELETE',
        }),
      invalidatesTags: (_result, _error, { tenantId, resourceServerId }) => [
        { type: 'ResourceServer', id: tenantTag(tenantId, LIST_TAG) },
        { type: 'ResourceServer', id: tenantTag(tenantId, resourceServerId) },
      ],
    }),
    createScope: build.mutation<
      ScopeResource,
      WithCell<{ tenantId: string; resourceServerId: string; body: CreateScopeRequest }>
    >({
      query: ({ tenantId, resourceServerId, body, cell }) =>
        inCell(cell, {
          url: `api/tenants/${encodeURIComponent(tenantId)}/resource-servers/${encodeURIComponent(
            resourceServerId,
          )}/scopes`,
          method: 'POST',
          body,
        }),
      invalidatesTags: (_result, _error, { tenantId, resourceServerId }) => [
        { type: 'ResourceServer', id: tenantTag(tenantId, LIST_TAG) },
        { type: 'ResourceServer', id: tenantTag(tenantId, resourceServerId) },
      ],
    }),
    updateScope: build.mutation<
      void,
      WithCell<{
        tenantId: string;
        resourceServerId: string;
        scopeValue: string;
        body: UpdateScopeRequest;
      }>
    >({
      query: ({ tenantId, resourceServerId, scopeValue, body, cell }) =>
        inCell(cell, {
          url: `api/tenants/${encodeURIComponent(tenantId)}/resource-servers/${encodeURIComponent(
            resourceServerId,
          )}/scopes/${encodeURIComponent(scopeValue)}`,
          method: 'PUT',
          body,
        }),
      invalidatesTags: (_result, _error, { tenantId, resourceServerId }) => [
        { type: 'ResourceServer', id: tenantTag(tenantId, LIST_TAG) },
        { type: 'ResourceServer', id: tenantTag(tenantId, resourceServerId) },
      ],
    }),
    deleteScope: build.mutation<
      void,
      WithCell<{ tenantId: string; resourceServerId: string; scopeValue: string }>
    >({
      query: ({ tenantId, resourceServerId, scopeValue, cell }) =>
        inCell(cell, {
          url: `api/tenants/${encodeURIComponent(tenantId)}/resource-servers/${encodeURIComponent(
            resourceServerId,
          )}/scopes/${encodeURIComponent(scopeValue)}`,
          method: 'DELETE',
        }),
      invalidatesTags: (_result, _error, { tenantId, resourceServerId }) => [
        { type: 'ResourceServer', id: tenantTag(tenantId, LIST_TAG) },
        { type: 'ResourceServer', id: tenantTag(tenantId, resourceServerId) },
      ],
    }),
    listGrants: build.query<
      CollectionResource<GrantResource>,
      WithCell<{ tenantId: string; subjectId?: string; clientId?: string }>
    >({
      query: ({ tenantId, subjectId, clientId, cell }) =>
        inCell(cell, {
          url: `api/tenants/${encodeURIComponent(tenantId)}/grants`,
          params: {
            ...(subjectId ? { subjectId } : {}),
            ...(clientId ? { clientId } : {}),
          },
        }),
      providesTags: (result, _error, { tenantId }) => [
        { type: 'Grant' as const, id: tenantTag(tenantId, LIST_TAG) },
        ...(result?.items ?? []).map((g) => ({
          type: 'Grant' as const,
          id: tenantTag(tenantId, g.grantId),
        })),
      ],
    }),
    revokeGrant: build.mutation<void, WithCell<{ tenantId: string; grantId: string }>>({
      query: ({ tenantId, grantId, cell }) =>
        inCell(cell, {
          url: `api/tenants/${encodeURIComponent(tenantId)}/grants/${encodeURIComponent(grantId)}`,
          method: 'DELETE',
        }),
      invalidatesTags: (_result, _error, { tenantId, grantId }) => [
        { type: 'Grant', id: tenantTag(tenantId, LIST_TAG) },
        { type: 'Grant', id: tenantTag(tenantId, grantId) },
      ],
    }),
    // Bulk revoke requires at least one filter server-side; the caller must supply a subject and/or client.
    revokeGrants: build.mutation<
      GrantRevocationResult,
      WithCell<{ tenantId: string; subjectId?: string; clientId?: string }>
    >({
      query: ({ tenantId, subjectId, clientId, cell }) =>
        inCell(cell, {
          url: `api/tenants/${encodeURIComponent(tenantId)}/grants`,
          method: 'DELETE',
          params: {
            ...(subjectId ? { subjectId } : {}),
            ...(clientId ? { clientId } : {}),
          },
        }),
      invalidatesTags: (_result, _error, { tenantId }) => [
        { type: 'Grant', id: tenantTag(tenantId, LIST_TAG) },
      ],
    }),
    // Mints an RFC 7591 initial access token; the plaintext value is revealed once and the backing grant is listed.
    createInitialAccessToken: build.mutation<
      CreatedInitialAccessTokenResource,
      WithCell<{ tenantId: string; body: CreateInitialAccessTokenRequest }>
    >({
      query: ({ tenantId, body, cell }) =>
        inCell(cell, {
          url: `api/tenants/${encodeURIComponent(tenantId)}/grants/initial-access-tokens`,
          method: 'POST',
          body,
        }),
      invalidatesTags: (_result, _error, { tenantId }) => [
        { type: 'Grant', id: tenantTag(tenantId, LIST_TAG) },
      ],
    }),
    // The usage ledger grows with every flush of the server (ADR-1199), so nothing is cached once unused: every mount
    // reads the current ledger.
    getTenantUsage: build.query<
      TenantUsageResource,
      WithCell<{ tenantId: string; from?: string | undefined; to?: string | undefined }>
    >({
      query: ({ tenantId, from, to, cell }) => {
        const params = new URLSearchParams();
        if (from) params.set('from', from);
        if (to) params.set('to', to);
        const search = params.toString();
        return inCell(
          cell,
          `api/tenants/${encodeURIComponent(tenantId)}/usage${search ? `?${search}` : ''}`,
        );
      },
      keepUnusedDataFor: 0,
    }),
    // The secrets of a tenant, or of a client or resource server in a tenant, and the settings of any owner: the
    // platform, a team, a tenant, or a client or resource server (docs/decisions/1257). Every tag is scoped by the
    // owner's scope, so one owner's caches never invalidate another's.
    listSecrets: build.query<SecretsResource, WithCell<SecretOwner>>({
      query: (owner) => inCell(owner.cell, `${ownerApiPath(owner)}/secrets`),
      providesTags: (result, _error, owner) => [
        { type: 'Secret' as const, id: ownerTagId(owner) },
        ...(result?.secrets ?? []).map((s) => ({
          type: 'Secret' as const,
          id: scopeTagId(ownerScope(owner), s.secretId),
        })),
      ],
    }),
    createSecret: build.mutation<
      CreatedSecretResource,
      WithCell<{ owner: SecretOwner; body: CreateSecretRequest }>
    >({
      query: ({ owner, body, cell }) =>
        inCell(cell, {
          url: `${ownerApiPath(owner)}/secrets`,
          method: 'POST',
          body,
        }),
      invalidatesTags: (_result, _error, { owner }) => [{ type: 'Secret', id: ownerTagId(owner) }],
    }),
    deleteSecret: build.mutation<void, WithCell<{ owner: SecretOwner; secretId: string }>>({
      query: ({ owner, secretId, cell }) =>
        inCell(cell, {
          url: `${ownerApiPath(owner)}/secrets/${encodeURIComponent(secretId)}`,
          method: 'DELETE',
        }),
      invalidatesTags: (_result, _error, { owner, secretId }) => [
        { type: 'Secret', id: ownerTagId(owner) },
        { type: 'Secret', id: scopeTagId(ownerScope(owner), secretId) },
      ],
    }),
    // Only a client's secrets rotate in one call, so the owner is a client by type.
    rotateSecret: build.mutation<
      CreatedSecretResource,
      WithCell<{ owner: ClientOwner; secretId: string; body: RotateSecretRequest }>
    >({
      query: ({ owner, secretId, body, cell }) =>
        inCell(cell, {
          url: `${ownerApiPath(owner)}/secrets/${encodeURIComponent(secretId)}/rotate`,
          method: 'POST',
          body,
        }),
      invalidatesTags: (_result, _error, { owner, secretId }) => [
        { type: 'Secret', id: ownerTagId(owner) },
        { type: 'Secret', id: scopeTagId(ownerScope(owner), secretId) },
      ],
    }),
    // Only a tenant's keys are held in a KMS, so the owner is a tenant by type (ADR-1476).
    createRemoteSecret: build.mutation<
      SecretResource,
      WithCell<{ tenantId: string; body: CreateRemoteSecretRequest }>
    >({
      query: ({ tenantId, body, cell }) =>
        inCell(cell, {
          url: `${ownerApiPath({ kind: 'tenant', tenantId })}/secrets/remote`,
          method: 'POST',
          body,
        }),
      invalidatesTags: (_result, _error, { tenantId }) => [
        { type: 'Secret', id: ownerTagId({ kind: 'tenant', tenantId }) },
      ],
    }),
    importRemoteSecret: build.mutation<
      SecretResource,
      WithCell<{ tenantId: string; body: ImportRemoteSecretRequest }>
    >({
      query: ({ tenantId, body, cell }) =>
        inCell(cell, {
          url: `${ownerApiPath({ kind: 'tenant', tenantId })}/secrets/remote/import`,
          method: 'POST',
          body,
        }),
      invalidatesTags: (_result, _error, { tenantId }) => [
        { type: 'Secret', id: ownerTagId({ kind: 'tenant', tenantId }) },
      ],
    }),
    // The master keys of a tenant or of the platform (ADR-1407). A rekey invalidates only its owner's keys.
    listEncryptionKeys: build.query<EncryptionKeysResource, WithCell<EncryptionKeyOwner>>({
      query: (owner) => inCell(owner.cell, `${ownerApiPath(owner)}/encryption-keys`),
      providesTags: (_result, _error, owner) => [
        { type: 'EncryptionKey' as const, id: ownerTagId(owner) },
      ],
    }),
    rekeyEncryptionKeys: build.mutation<
      EncryptionKeyRotationResource,
      WithCell<EncryptionKeyOwner>
    >({
      query: (owner) =>
        inCell(owner.cell, {
          url: `${ownerApiPath(owner)}/encryption-keys/rekey`,
          method: 'POST',
        }),
      invalidatesTags: (_result, _error, owner) => [
        { type: 'EncryptionKey', id: ownerTagId(owner) },
      ],
    }),
    // The root key a tenant brings (ADR-1414). Setting, validating or removing it changes only that tenant's keys.
    setTenantRootKey: build.mutation<
      EncryptionRootKeyChangeResource,
      WithCell<{ tenantId: string; body: EncryptionRootKeyRequest }>
    >({
      query: ({ tenantId, body, cell }) =>
        inCell(cell, {
          url: `${ownerApiPath({ kind: 'tenant', tenantId })}/encryption-keys/root-key`,
          method: 'PUT',
          body,
        }),
      invalidatesTags: (_result, _error, { tenantId }) => [
        { type: 'EncryptionKey', id: ownerTagId({ kind: 'tenant', tenantId }) },
      ],
    }),
    // Answers 204 once every master key is back under the deployment's root key, or 202 with the root key while keys
    // remain under the tenant's.
    removeTenantRootKey: build.mutation<
      EncryptionRootKeyResource | null,
      WithCell<{ tenantId: string }>
    >({
      query: ({ tenantId, cell }) =>
        inCell(cell, {
          url: `${ownerApiPath({ kind: 'tenant', tenantId })}/encryption-keys/root-key`,
          method: 'DELETE',
        }),
      invalidatesTags: (_result, _error, { tenantId }) => [
        { type: 'EncryptionKey', id: ownerTagId({ kind: 'tenant', tenantId }) },
      ],
    }),
    validateTenantRootKey: build.mutation<
      EncryptionRootKeyResource,
      WithCell<{ tenantId: string }>
    >({
      query: ({ tenantId, cell }) =>
        inCell(cell, {
          url: `${ownerApiPath({ kind: 'tenant', tenantId })}/encryption-keys/root-key/validate`,
          method: 'POST',
        }),
      invalidatesTags: (_result, _error, { tenantId }) => [
        { type: 'EncryptionKey', id: ownerTagId({ kind: 'tenant', tenantId }) },
      ],
    }),
    // The root key a team brings per region. Changing one region's key changes only that team's keys.
    listTeamRootKeys: build.query<TeamRootKeysResource, { teamId: string }>({
      query: ({ teamId }) => `${ownerApiPath({ kind: 'team', teamId })}/encryption-keys/root-keys`,
      providesTags: (_result, _error, { teamId }) => [
        { type: 'TeamRootKey' as const, id: ownerTagId({ kind: 'team', teamId }) },
      ],
    }),
    setTeamRootKey: build.mutation<
      TeamRootKeyResource,
      { teamId: string; region: string; body: EncryptionRootKeyRequest }
    >({
      query: ({ teamId, region, body }) => ({
        url: `${ownerApiPath({ kind: 'team', teamId })}/encryption-keys/root-keys/${encodeURIComponent(region)}`,
        method: 'PUT',
        body,
      }),
      invalidatesTags: (_result, _error, { teamId }) => [
        { type: 'TeamRootKey', id: ownerTagId({ kind: 'team', teamId }) },
      ],
    }),
    // Answers 204 once the region's keys are back under the deployment's root key, or 202 with the root key while keys
    // remain under the team's.
    removeTeamRootKey: build.mutation<
      EncryptionRootKeyResource | null,
      { teamId: string; region: string }
    >({
      query: ({ teamId, region }) => ({
        url: `${ownerApiPath({ kind: 'team', teamId })}/encryption-keys/root-keys/${encodeURIComponent(region)}`,
        method: 'DELETE',
      }),
      invalidatesTags: (_result, _error, { teamId }) => [
        { type: 'TeamRootKey', id: ownerTagId({ kind: 'team', teamId }) },
      ],
    }),
    validateTeamRootKey: build.mutation<
      EncryptionRootKeyResource,
      { teamId: string; region: string }
    >({
      query: ({ teamId, region }) => ({
        url: `${ownerApiPath({ kind: 'team', teamId })}/encryption-keys/root-keys/${encodeURIComponent(region)}/validate`,
        method: 'POST',
      }),
      invalidatesTags: (_result, _error, { teamId }) => [
        { type: 'TeamRootKey', id: ownerTagId({ kind: 'team', teamId }) },
      ],
    }),
    getSecretPublicKey: build.query<
      PublicKeyResource,
      WithCell<{ owner: SecretOwner; secretId: string }>
    >({
      query: ({ owner, secretId, cell }) =>
        inCell(cell, `${ownerApiPath(owner)}/secrets/${encodeURIComponent(secretId)}/public-key`),
    }),
    getSettings: build.query<SettingsResource, WithCell<ManagementOwner>>({
      query: (owner) => inCell(owner.cell, `${ownerApiPath(owner)}/settings`),
      providesTags: (_result, _error, owner) => [{ type: 'Settings', id: ownerTagId(owner) }],
    }),
    getEffectiveSettings: build.query<EffectiveSettingsResource, WithCell<ManagementOwner>>({
      query: (owner) => inCell(owner.cell, `${ownerApiPath(owner)}/effective-settings`),
      providesTags: (_result, _error, owner) => [
        { type: 'Settings', id: ownerEffectiveTagId(owner) },
      ],
    }),
    getSettingLimits: build.query<SettingLimitsResource, WithCell<ManagementOwner>>({
      query: (owner) => inCell(owner.cell, `${ownerApiPath(owner)}/settings/limits`),
      providesTags: (_result, _error, owner) => [
        { type: 'SettingLimits', id: ownerLimitsTagId(owner) },
      ],
    }),
    // Settings are an opaque object, so updates are a top-level add/remove/replace diff (not replace-only). A client's
    // settings change can issue the secret its client-authentication method needs, which the response carries once
    // (docs/decisions/1391); every other change answers 204 with no body, which resolves to null.
    updateSettings: build.mutation<
      CreatedSecretResource | null,
      WithCell<{
        owner: ManagementOwner;
        concurrencyToken: string;
        original: Readonly<Record<string, unknown>>;
        desired: Readonly<Record<string, unknown>>;
      }>
    >({
      query: ({ owner, concurrencyToken, original, desired, cell }) =>
        inCell(cell, {
          url: `${ownerApiPath(owner)}/settings`,
          method: 'PATCH',
          body: diffPatch(original, desired),
          headers: { 'If-Match': concurrencyToken },
        }),
      invalidatesTags: (_result, _error, { owner }) => [
        { type: 'Settings', id: ownerTagId(owner) },
        { type: 'Settings', id: ownerEffectiveTagId(owner) },
        { type: 'Secret', id: ownerTagId(owner) },
      ],
    }),
    // The owners of a team, tenant, client or resource server and the administrators of a team or tenant, addressed by
    // a role list (ADR-1257).
    listRoleAssignments: build.query<RoleListResource, WithCell<RoleList>>({
      query: (list) => inCell(list.cell, roleListApiPath(list)),
      providesTags: (_result, _error, list) => [
        { type: 'RoleAssignment', id: roleListTagId(list) },
      ],
    }),
    addRoleAssignment: build.mutation<
      RoleHolderResource,
      WithCell<{ list: RoleList; principalId: string }>
    >({
      query: ({ list, principalId, cell }) =>
        inCell(cell, {
          url: roleListApiPath(list),
          method: 'POST',
          body: { principalId } satisfies AddRoleHolderRequest,
        }),
      // A role change can change the caller's own access, its authority over every role list and its authority over any
      // scope's limits.
      invalidatesTags: ['RoleAssignment', { type: 'Caller', id: callerTag }, 'SettingLimits'],
    }),
    removeRoleAssignment: build.mutation<void, WithCell<{ list: RoleList; principalId: string }>>({
      query: ({ list, principalId, cell }) =>
        inCell(cell, {
          url: `${roleListApiPath(list)}/${encodeURIComponent(principalId)}`,
          method: 'DELETE',
        }),
      // A role change can change the caller's own access, its authority over every role list and its authority over any
      // scope's limits.
      invalidatesTags: ['RoleAssignment', { type: 'Caller', id: callerTag }, 'SettingLimits'],
    }),
  }),
});

export const {
  useGetCapabilitiesQuery,
  useGetCallerQuery,
  useListCallerTeamsQuery,
  useListCallerTenantsQuery,
  useListTenantsQuery,
  useGetTenantQuery,
  useGetTenantAddressQuery,
  useLazyGetTenantConfigurationQuery,
  useGetNewTenantAddressQuery,
  useListCellsQuery,
  useListTeamsQuery,
  useGetTeamQuery,
  useGetTeamUsageQuery,
  useListClientsQuery,
  useListResourceServersQuery,
  useCreateTeamMutation,
  useUpdateTeamMutation,
  useDeleteTeamMutation,
  useCreateTenantMutation,
  useUpdateTenantMutation,
  useDeleteTenantMutation,
  usePreviewTenantPromotionMutation,
  usePromoteTenantMutation,
  useCreateTransferKeyMutation,
  useExportTenantMutation,
  useImportTenantMutation,
  useReleaseMovedTenantMutation,
  useCreateClientMutation,
  useUpdateClientMutation,
  useDeleteClientMutation,
  useListMachineClientsQuery,
  useCreateMachineClientMutation,
  useListLocalAccountsQuery,
  useCreateLocalAccountMutation,
  useUpdateLocalAccountMutation,
  useDeleteLocalAccountMutation,
  useEnableLocalAccountMutation,
  useDisableLocalAccountMutation,
  useResetLocalAccountPasswordMutation,
  useListConnectionsQuery,
  useGetConnectionQuery,
  useCreateConnectionMutation,
  useUpdateConnectionMutation,
  useDeleteConnectionMutation,
  useGetConnectionSettingsQuery,
  useGetConnectionEffectiveSettingsQuery,
  useGetConnectionSettingLimitsQuery,
  useUpdateConnectionSettingsMutation,
  useCreateResourceServerMutation,
  useUpdateResourceServerMutation,
  useDeleteResourceServerMutation,
  useCreateScopeMutation,
  useUpdateScopeMutation,
  useDeleteScopeMutation,
  useListGrantsQuery,
  useRevokeGrantMutation,
  useRevokeGrantsMutation,
  useCreateInitialAccessTokenMutation,
  useGetTenantUsageQuery,
  useListSecretsQuery,
  useCreateSecretMutation,
  useDeleteSecretMutation,
  useRotateSecretMutation,
  useCreateRemoteSecretMutation,
  useImportRemoteSecretMutation,
  useLazyGetSecretPublicKeyQuery,
  useListEncryptionKeysQuery,
  useRekeyEncryptionKeysMutation,
  useSetTenantRootKeyMutation,
  useRemoveTenantRootKeyMutation,
  useValidateTenantRootKeyMutation,
  useListTeamRootKeysQuery,
  useSetTeamRootKeyMutation,
  useRemoveTeamRootKeyMutation,
  useValidateTeamRootKeyMutation,
  useGetSettingsQuery,
  useGetEffectiveSettingsQuery,
  useGetSettingLimitsQuery,
  useUpdateSettingsMutation,
  useListRoleAssignmentsQuery,
  useAddRoleAssignmentMutation,
  useRemoveRoleAssignmentMutation,
  useListChildTeamsQuery,
  useCreateChildTeamMutation,
} = managementApi;
