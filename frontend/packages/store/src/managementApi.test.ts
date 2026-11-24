import { afterEach, describe, expect, it, vi } from 'vitest';
import type {
  CreateSecretRequest,
  CreatedSecretResource,
  EncryptionKeyOwner,
  ManagementOwner,
  RoleList,
  SecretOwner,
} from '@ncodegroup/identity-api';
import type { BffRuntimeConfig, OpenIdRuntimeConfig } from '@ncodegroup/identity-config';
import { createAppStore, type AppStore } from './store.js';
import { managementApi, pagedItems, pagedTenants } from './managementApi.js';

const runtime: BffRuntimeConfig = {
  kind: 'bff',
  apiBaseUrl: 'https://api.test',
  bff: { basePath: '/bff', antiforgeryHeader: { name: 'X-CSRF', value: '1' } },
  features: {},
};

function createStore() {
  return createAppStore({ config: { runtime } });
}

type CapturedRequest = {
  readonly url: string;
  readonly method: string;
  readonly antiforgery: string | null;
  readonly ifMatch: string | null;
  readonly body: string;
};

function mockFetch(response: Response) {
  const captured: CapturedRequest[] = [];
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    const request = input as Request;
    captured.push({
      url: request.url,
      method: request.method,
      antiforgery: request.headers.get('x-csrf'),
      ifMatch: request.headers.get('if-match'),
      body: await request.clone().text(),
    });
    return response.clone();
  });
  vi.stubGlobal('fetch', fetchMock);
  return captured;
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  });
}

/** Stubs `fetch` with a per-request responder and records every call as `METHOD /path?query`. */
function routeFetch(respond: (method: string, path: string) => Response) {
  const calls: string[] = [];
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    const request = input as Request;
    const { pathname, search } = new URL(request.url);
    const path = `${pathname}${search}`;
    calls.push(`${request.method} ${path}`);
    return respond(request.method, path);
  });
  vi.stubGlobal('fetch', fetchMock);
  return {
    calls,
    count: (method: string, path: string) =>
      calls.filter((call) => call === `${method} ${path}`).length,
  };
}

/** Waits for every in-flight query, including refetches triggered by tag invalidation. */
async function settle(store: AppStore): Promise<void> {
  await Promise.all(store.dispatch(managementApi.util.getRunningQueriesThunk()));
}

type ManagementTag = {
  readonly type: 'Tenant' | 'Team' | 'Client' | 'ResourceServer' | 'Grant' | 'Secret' | 'Settings';
  readonly id: string;
};

/** The endpoint names of the cached queries that the given tags would invalidate. */
function invalidatedBy(store: AppStore, tags: readonly ManagementTag[]): string[] {
  return managementApi.util
    .selectInvalidatedBy(store.getState(), tags)
    .map((entry) => entry.endpointName);
}

const secretRequest: CreateSecretRequest = {
  secretType: 'symmetric',
  keySizeBits: 256,
  use: null,
  algorithm: null,
  expiresWhen: '2027-01-01T00:00:00Z',
};

describe('managementApi tenant mutations', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('createTenant POSTs the request body with the anti-forgery header', async () => {
    const captured = mockFetch(
      new Response(
        JSON.stringify({
          tenantId: 'new',
          concurrencyToken: 'c1',
          issuer: 'https://new.auth.test',
          mtlsEndpointAliasAuthority: 'https://new.mtls.test',
          isDisabled: false,
          displayName: 'New',
        }),
        { status: 201, headers: { 'content-type': 'application/json' } },
      ),
    );
    const store = createStore();

    const result = await store.dispatch(
      managementApi.endpoints.createTenant.initiate({
        teamId: 'acme',
        cell: undefined,
        body: {
          tenantId: 'new',
          issuer: 'https://new.auth.test',
          mtlsEndpointAliasAuthority: 'https://new.mtls.test',
          displayName: 'New',
          isDisabled: false,
          settings: {},
        },
      }),
    );

    expect('data' in result && result.data?.tenantId).toBe('new');
    expect(captured).toHaveLength(1);
    expect(captured[0]?.url).toBe('https://api.test/api/teams/acme/tenants');
    expect(captured[0]?.method).toBe('POST');
    expect(captured[0]?.antiforgery).toBe('1');
    expect(JSON.parse(captured[0]?.body ?? '{}')).toEqual({
      tenantId: 'new',
      issuer: 'https://new.auth.test',
      mtlsEndpointAliasAuthority: 'https://new.mtls.test',
      displayName: 'New',
      isDisabled: false,
      settings: {},
    });
  });

  it('updateTenant PATCHes a replace JSON Patch with the If-Match concurrency token', async () => {
    const captured = mockFetch(new Response(null, { status: 204 }));
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.updateTenant.initiate({
        teamId: 'acme',
        tenantId: 'acme/1',
        cell: undefined,
        concurrencyToken: 'etag-7',
        fields: {
          issuer: 'https://acme.auth.test',
          mtlsEndpointAliasAuthority: 'https://acme.mtls.test',
          displayName: 'Acme',
          isDisabled: true,
        },
      }),
    );

    expect(captured).toHaveLength(1);
    expect(captured[0]?.url).toBe('https://api.test/api/tenants/acme%2F1');
    expect(captured[0]?.method).toBe('PATCH');
    expect(captured[0]?.ifMatch).toBe('etag-7');
    expect(JSON.parse(captured[0]?.body ?? '[]')).toEqual([
      { op: 'replace', path: '/issuer', value: 'https://acme.auth.test' },
      { op: 'replace', path: '/mtlsEndpointAliasAuthority', value: 'https://acme.mtls.test' },
      { op: 'replace', path: '/displayName', value: 'Acme' },
      { op: 'replace', path: '/isDisabled', value: true },
    ]);
  });

  it('deleteTenant DELETEs the tenant by id', async () => {
    const captured = mockFetch(new Response(null, { status: 204 }));
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.deleteTenant.initiate({
        teamId: 'acme',
        tenantId: 'acme',
        cell: undefined,
      }),
    );

    expect(captured).toHaveLength(1);
    expect(captured[0]?.url).toBe('https://api.test/api/tenants/acme');
    expect(captured[0]?.method).toBe('DELETE');
    expect(captured[0]?.antiforgery).toBe('1');
  });
});

describe('managementApi tenant promotion', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  const promotionRequest = {
    sourceTenantId: 'acme-dev',
    environmentSettings: [
      {
        resourceType: 'client',
        resourceId: 'web',
        settings: { redirect_uris: ['https://a.test'] },
      },
    ],
  };

  it('previewTenantPromotion POSTs the request body to the target tenant preview route', async () => {
    const report = {
      teamId: 'acme',
      sourceTenantId: 'acme-dev',
      targetTenantId: 'acme-prod',
      isApplied: false,
      changes: [{ resourceType: 'client', resourceId: 'web', action: 'created' }],
      notes: [],
      errors: [],
    };
    const captured = mockFetch(jsonResponse(report));
    const store = createStore();

    const result = await store.dispatch(
      managementApi.endpoints.previewTenantPromotion.initiate({
        tenantId: 'acme-prod',
        cell: undefined,
        body: promotionRequest,
      }),
    );

    expect('data' in result && result.data).toEqual(report);
    expect(captured).toHaveLength(1);
    expect(captured[0]?.url).toBe('https://api.test/api/tenants/acme-prod/promotions/preview');
    expect(captured[0]?.method).toBe('POST');
    expect(JSON.parse(captured[0]?.body ?? '{}')).toEqual(promotionRequest);
  });

  it('promoteTenant POSTs the request body to the target tenant promotions route', async () => {
    const captured = mockFetch(
      jsonResponse({
        teamId: 'acme',
        sourceTenantId: 'acme-dev',
        targetTenantId: 'acme-prod',
        isApplied: true,
        changes: [],
        notes: [],
        errors: [],
      }),
    );
    const store = createStore();

    const result = await store.dispatch(
      managementApi.endpoints.promoteTenant.initiate({
        tenantId: 'acme-prod',
        cell: undefined,
        body: promotionRequest,
      }),
    );

    expect('data' in result && result.data?.isApplied).toBe(true);
    expect(captured).toHaveLength(1);
    expect(captured[0]?.url).toBe('https://api.test/api/tenants/acme-prod/promotions');
    expect(captured[0]?.method).toBe('POST');
    expect(JSON.parse(captured[0]?.body ?? '{}')).toEqual(promotionRequest);
  });

  it('promoteTenant refetches the target tenant and the settings of each reported resource', async () => {
    const settingsPaths = [
      '/api/tenants/acme-prod/settings',
      '/api/tenants/acme-prod/clients/web/settings',
      '/api/tenants/acme-prod/resource-servers/rs-1/settings',
      '/api/tenants/acme-dev/settings',
      '/api/tenants/acme-prod/clients/other/settings',
    ];
    const fetches = routeFetch((method) =>
      method === 'GET'
        ? jsonResponse({})
        : jsonResponse({
            teamId: 'acme',
            sourceTenantId: 'acme-dev',
            targetTenantId: 'acme-prod',
            isApplied: true,
            changes: [
              { resourceType: 'client', resourceId: 'web', action: 'created' },
              { resourceType: 'resource_server', resourceId: 'rs-1', action: 'updated' },
              { resourceType: 'client_grant', resourceId: 'g-1', action: 'created' },
            ],
            notes: [],
            errors: [],
          }),
    );
    const store = createStore();
    const owners: ManagementOwner[] = [
      { kind: 'tenant', tenantId: 'acme-prod' },
      { kind: 'client', tenantId: 'acme-prod', clientId: 'web' },
      { kind: 'resource-server', tenantId: 'acme-prod', resourceServerId: 'rs-1' },
      { kind: 'tenant', tenantId: 'acme-dev' },
      { kind: 'client', tenantId: 'acme-prod', clientId: 'other' },
    ];
    await Promise.all(
      owners.map((owner) =>
        store.dispatch(managementApi.endpoints.getSettings.initiate({ ...owner, cell: undefined })),
      ),
    );

    await store.dispatch(
      managementApi.endpoints.promoteTenant.initiate({
        tenantId: 'acme-prod',
        cell: undefined,
        body: promotionRequest,
      }),
    );
    await settle(store);

    expect(settingsPaths.map((path) => fetches.count('GET', path))).toEqual([2, 2, 2, 1, 1]);
  });
});

describe('managementApi client mutations (tenant scope)', () => {
  const tenantId = 'acme';

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('createClient POSTs under the tenant path', async () => {
    const captured = mockFetch(
      new Response(
        JSON.stringify({
          clientId: 'new',
          tenantId: 'acme',
          concurrencyToken: 'c1',
          isDisabled: false,
        }),
        { status: 201, headers: { 'content-type': 'application/json' } },
      ),
    );
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.createClient.initiate({
        tenantId,
        cell: undefined,
        body: { isDisabled: false, settings: {} },
      }),
    );

    expect(captured[0]?.url).toBe('https://api.test/api/tenants/acme/clients');
    expect(captured[0]?.method).toBe('POST');
    expect(JSON.parse(captured[0]?.body ?? '{}')).toEqual({ isDisabled: false, settings: {} });
  });

  it('createClient POSTs the resource-server binding of a bound client', async () => {
    const captured = mockFetch(
      new Response(
        JSON.stringify({
          clientId: 'new',
          tenantId: 'acme',
          concurrencyToken: 'c1',
          isDisabled: false,
          resourceServerId: 'rs-orders',
        }),
        { status: 201, headers: { 'content-type': 'application/json' } },
      ),
    );
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.createClient.initiate({
        tenantId,
        cell: undefined,
        body: { isDisabled: false, resourceServerId: 'rs-orders', settings: {} },
      }),
    );

    expect(captured[0]?.url).toBe('https://api.test/api/tenants/acme/clients');
    expect(JSON.parse(captured[0]?.body ?? '{}')).toEqual({
      isDisabled: false,
      resourceServerId: 'rs-orders',
      settings: {},
    });
  });

  it('updateClient PATCHes a replace patch with If-Match under the tenant path', async () => {
    const captured = mockFetch(new Response(null, { status: 204 }));
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.updateClient.initiate({
        tenantId,
        clientId: 'web',
        cell: undefined,
        concurrencyToken: 'etag-2',
        fields: { isDisabled: true },
      }),
    );

    expect(captured[0]?.url).toBe('https://api.test/api/tenants/acme/clients/web');
    expect(captured[0]?.method).toBe('PATCH');
    expect(captured[0]?.ifMatch).toBe('etag-2');
    expect(JSON.parse(captured[0]?.body ?? '[]')).toEqual([
      { op: 'replace', path: '/isDisabled', value: true },
    ]);
  });

  it('deleteClient DELETEs under the tenant path', async () => {
    const captured = mockFetch(new Response(null, { status: 204 }));
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.deleteClient.initiate({
        tenantId,
        clientId: 'web',
        cell: undefined,
      }),
    );

    expect(captured[0]?.url).toBe('https://api.test/api/tenants/acme/clients/web');
    expect(captured[0]?.method).toBe('DELETE');
  });
});

describe('managementApi machine clients (platform and team scope)', () => {
  const botList = { items: [{ clientId: 'bot', tenantId: 'control' }] };

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('listMachineClients GETs the platform machine clients', async () => {
    const captured = mockFetch(jsonResponse({ items: [] }));
    const store = createStore();

    await store.dispatch(managementApi.endpoints.listMachineClients.initiate({ kind: 'platform' }));

    expect(captured[0]?.url).toBe('https://api.test/api/platform/clients');
    expect(captured[0]?.method).toBe('GET');
  });

  it("listMachineClients GETs a team's machine clients by encoded id", async () => {
    const captured = mockFetch(jsonResponse({ items: [] }));
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.listMachineClients.initiate({ kind: 'team', teamId: 'a/b' }),
    );

    expect(captured[0]?.url).toBe('https://api.test/api/teams/a%2Fb/clients');
    expect(captured[0]?.method).toBe('GET');
  });

  it('createMachineClient POSTs the request body under the team path', async () => {
    const captured = mockFetch(
      jsonResponse({ clientId: 'bot', tenantId: 'control', isDisabled: false }, 201),
    );
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.createMachineClient.initiate({
        owner: { kind: 'team', teamId: 'acme' },
        body: { isDisabled: false, settings: {} },
      }),
    );

    expect(captured[0]?.method).toBe('POST');
    expect(captured[0]?.url).toBe('https://api.test/api/teams/acme/clients');
    expect(JSON.parse(captured[0]?.body ?? '{}')).toEqual({ isDisabled: false, settings: {} });
    expect(captured[0]?.antiforgery).toBe('1');
  });

  it("createMachineClient refetches the owner's list and the control-plane tenant's clients, not another team's", async () => {
    const fetches = routeFetch((method) =>
      method === 'GET'
        ? jsonResponse({ items: [] })
        : jsonResponse({ clientId: 'bot', tenantId: 'control', isDisabled: false }, 201),
    );
    const store = createStore();
    await store.dispatch(
      managementApi.endpoints.listMachineClients.initiate({ kind: 'team', teamId: 'acme' }),
    );
    await store.dispatch(
      managementApi.endpoints.listMachineClients.initiate({ kind: 'team', teamId: 'globex' }),
    );
    await store.dispatch(
      managementApi.endpoints.listClients.initiate({ tenantId: 'control', cell: undefined }),
    );

    await store.dispatch(
      managementApi.endpoints.createMachineClient.initiate({
        owner: { kind: 'team', teamId: 'acme' },
        body: { isDisabled: false, settings: {} },
      }),
    );
    await settle(store);

    expect(fetches.count('GET', '/api/teams/acme/clients')).toBe(2);
    expect(fetches.count('GET', '/api/teams/globex/clients')).toBe(1);
    expect(fetches.count('GET', '/api/tenants/control/clients')).toBe(2);
  });

  it("createMachineClient refetches only the owner's list when the create is refused", async () => {
    const fetches = routeFetch((method) =>
      method === 'GET' ? jsonResponse({ items: [] }) : jsonResponse({ title: 'Bad Request' }, 400),
    );
    const store = createStore();
    await store.dispatch(
      managementApi.endpoints.listMachineClients.initiate({ kind: 'team', teamId: 'acme' }),
    );
    await store.dispatch(
      managementApi.endpoints.listMachineClients.initiate({ kind: 'team', teamId: 'globex' }),
    );
    await store.dispatch(
      managementApi.endpoints.listClients.initiate({ tenantId: 'control', cell: undefined }),
    );

    await store.dispatch(
      managementApi.endpoints.createMachineClient.initiate({
        owner: { kind: 'team', teamId: 'acme' },
        body: { isDisabled: false, settings: {} },
      }),
    );
    await settle(store);

    expect(fetches.count('GET', '/api/teams/acme/clients')).toBe(2);
    expect(fetches.count('GET', '/api/teams/globex/clients')).toBe(1);
    expect(fetches.count('GET', '/api/tenants/control/clients')).toBe(1);
  });

  it('updateClient of a machine client refetches the list of the team that owns it', async () => {
    const fetches = routeFetch((method) =>
      method === 'GET' ? jsonResponse(botList) : new Response(null, { status: 204 }),
    );
    const store = createStore();
    await store.dispatch(
      managementApi.endpoints.listMachineClients.initiate({ kind: 'team', teamId: 'acme' }),
    );

    await store.dispatch(
      managementApi.endpoints.updateClient.initiate({
        tenantId: 'control',
        clientId: 'bot',
        cell: undefined,
        concurrencyToken: 'c1',
        fields: { isDisabled: true },
      }),
    );
    await settle(store);

    expect(fetches.count('GET', '/api/teams/acme/clients')).toBe(2);
  });
});

describe('managementApi resource-server + scope mutations (tenant scope)', () => {
  const tenantId = 'acme';

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('createResourceServer POSTs the request body under the tenant path', async () => {
    const captured = mockFetch(
      new Response(
        JSON.stringify({
          tenantId: 'acme',
          resourceServerId: 'rs-new',
          identifier: 'urn:api',
          concurrencyToken: 'c1',
          name: 'API',
          isSystem: false,
          isDisabled: false,
          scopes: [],
        }),
        { status: 201, headers: { 'content-type': 'application/json' } },
      ),
    );
    const store = createStore();

    const result = await store.dispatch(
      managementApi.endpoints.createResourceServer.initiate({
        tenantId,
        cell: undefined,
        body: { identifier: 'urn:api', name: 'API', isDisabled: false, settings: {} },
      }),
    );

    expect('data' in result && result.data?.resourceServerId).toBe('rs-new');
    expect(captured[0]?.url).toBe('https://api.test/api/tenants/acme/resource-servers');
    expect(captured[0]?.method).toBe('POST');
    expect(JSON.parse(captured[0]?.body ?? '{}')).toEqual({
      identifier: 'urn:api',
      name: 'API',
      isDisabled: false,
      settings: {},
    });
  });

  it('updateResourceServer PATCHes a replace patch with If-Match under the tenant path', async () => {
    const captured = mockFetch(new Response(null, { status: 204 }));
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.updateResourceServer.initiate({
        tenantId,
        resourceServerId: 'rs-1',
        cell: undefined,
        concurrencyToken: 'etag-3',
        fields: { name: 'Renamed', isDisabled: true },
      }),
    );

    expect(captured[0]?.url).toBe('https://api.test/api/tenants/acme/resource-servers/rs-1');
    expect(captured[0]?.method).toBe('PATCH');
    expect(captured[0]?.ifMatch).toBe('etag-3');
    expect(JSON.parse(captured[0]?.body ?? '[]')).toEqual([
      { op: 'replace', path: '/name', value: 'Renamed' },
      { op: 'replace', path: '/isDisabled', value: true },
    ]);
  });

  it('deleteResourceServer DELETEs under the tenant path', async () => {
    const captured = mockFetch(new Response(null, { status: 204 }));
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.deleteResourceServer.initiate({
        tenantId,
        resourceServerId: 'rs-1',
        cell: undefined,
      }),
    );

    expect(captured[0]?.url).toBe('https://api.test/api/tenants/acme/resource-servers/rs-1');
    expect(captured[0]?.method).toBe('DELETE');
  });

  it('createScope POSTs the scope body under the resource server', async () => {
    const captured = mockFetch(
      new Response(JSON.stringify({ value: 'read:x', description: null, isSystem: false }), {
        status: 201,
        headers: { 'content-type': 'application/json' },
      }),
    );
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.createScope.initiate({
        tenantId,
        resourceServerId: 'rs-1',
        cell: undefined,
        body: { value: 'read:x', description: 'Read X' },
      }),
    );

    expect(captured[0]?.url).toBe('https://api.test/api/tenants/acme/resource-servers/rs-1/scopes');
    expect(captured[0]?.method).toBe('POST');
    expect(JSON.parse(captured[0]?.body ?? '{}')).toEqual({
      value: 'read:x',
      description: 'Read X',
    });
  });

  it('updateScope PUTs the description for the (immutable) scope value', async () => {
    const captured = mockFetch(new Response(null, { status: 204 }));
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.updateScope.initiate({
        tenantId,
        resourceServerId: 'rs-1',
        scopeValue: 'read:x',
        cell: undefined,
        body: { description: 'Read all X' },
      }),
    );

    expect(captured[0]?.url).toBe(
      'https://api.test/api/tenants/acme/resource-servers/rs-1/scopes/read%3Ax',
    );
    expect(captured[0]?.method).toBe('PUT');
    expect(captured[0]?.ifMatch).toBeNull();
    expect(JSON.parse(captured[0]?.body ?? '{}')).toEqual({ description: 'Read all X' });
  });

  it('deleteScope DELETEs the scope by value', async () => {
    const captured = mockFetch(new Response(null, { status: 204 }));
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.deleteScope.initiate({
        tenantId,
        resourceServerId: 'rs-1',
        scopeValue: 'read:x',
        cell: undefined,
      }),
    );

    expect(captured[0]?.url).toBe(
      'https://api.test/api/tenants/acme/resource-servers/rs-1/scopes/read%3Ax',
    );
    expect(captured[0]?.method).toBe('DELETE');
  });
});

describe('managementApi grant queries + mutations (tenant scope)', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('listGrants GETs the tenant grants with subject/client query params', async () => {
    const captured = mockFetch(
      new Response(JSON.stringify({ items: [] }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      }),
    );
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.listGrants.initiate({
        tenantId: 'acme',
        subjectId: 'user-1',
        clientId: 'web',
        cell: undefined,
      }),
    );

    expect(captured[0]?.url).toBe(
      'https://api.test/api/tenants/acme/grants?subjectId=user-1&clientId=web',
    );
    expect(captured[0]?.method).toBe('GET');
  });

  it('revokeGrant DELETEs a single grant by id', async () => {
    const captured = mockFetch(new Response(null, { status: 204 }));
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.revokeGrant.initiate({
        tenantId: 'acme',
        grantId: 'g-1',
        cell: undefined,
      }),
    );

    expect(captured[0]?.url).toBe('https://api.test/api/tenants/acme/grants/g-1');
    expect(captured[0]?.method).toBe('DELETE');
  });

  it('revokeGrants DELETEs with the filter query params for a bulk revoke', async () => {
    const captured = mockFetch(
      new Response(JSON.stringify({ revoked: 3 }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      }),
    );
    const store = createStore();

    const result = await store.dispatch(
      managementApi.endpoints.revokeGrants.initiate({
        tenantId: 'acme',
        clientId: 'web',
        cell: undefined,
      }),
    );

    expect('data' in result && result.data?.revoked).toBe(3);
    expect(captured[0]?.url).toBe('https://api.test/api/tenants/acme/grants?clientId=web');
    expect(captured[0]?.method).toBe('DELETE');
  });

  it('revokeGrants sends only the subject filter when no client is given', async () => {
    const captured = mockFetch(jsonResponse({ revoked: 1 }));
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.revokeGrants.initiate({
        tenantId: 'acme',
        subjectId: 'user-1',
        cell: undefined,
      }),
    );

    expect(captured[0]?.url).toBe('https://api.test/api/tenants/acme/grants?subjectId=user-1');
    expect(captured[0]?.method).toBe('DELETE');
  });

  it('listGrants sends no filter parameters when none are given', async () => {
    const captured = mockFetch(jsonResponse({ items: [] }));
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.listGrants.initiate({ tenantId: 'acme', cell: undefined }),
    );

    const url = new URL(captured[0]?.url ?? '');
    expect(url.origin + url.pathname).toBe('https://api.test/api/tenants/acme/grants');
    expect([...url.searchParams.keys()]).toEqual([]);
    expect(captured[0]?.method).toBe('GET');
  });
});

describe('managementApi tenant usage', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('getTenantUsage GETs the tenant usage path without a range by default', async () => {
    const captured = mockFetch(
      new Response(JSON.stringify({ tenantId: 'acme', from: '', to: '', entries: [] }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      }),
    );
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.getTenantUsage.initiate({ tenantId: 'acme', cell: undefined }),
    );

    expect(captured[0]?.url).toBe('https://api.test/api/tenants/acme/usage');
    expect(captured[0]?.method).toBe('GET');
  });

  it('getTenantUsage sends the range as from/to query parameters', async () => {
    const captured = mockFetch(
      new Response(JSON.stringify({ tenantId: 'acme', from: '', to: '', entries: [] }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      }),
    );
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.getTenantUsage.initiate({
        tenantId: 'acme',
        from: '2026-10-01',
        to: '2026-10-21',
        cell: undefined,
      }),
    );

    expect(captured[0]?.url).toBe(
      'https://api.test/api/tenants/acme/usage?from=2026-10-01&to=2026-10-21',
    );
  });
});

describe('managementApi team CRUD endpoints (platform scope)', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('createTeam POSTs the request body under the team path', async () => {
    const captured = mockFetch(
      new Response(
        JSON.stringify({
          teamId: 'acme',
          concurrencyToken: 'c1',
          displayName: 'Acme',
          isDisabled: false,
        }),
        { status: 201, headers: { 'content-type': 'application/json' } },
      ),
    );
    const store = createStore();

    const result = await store.dispatch(
      managementApi.endpoints.createTeam.initiate({
        teamId: 'acme',
        displayName: 'Acme',
        isDisabled: false,
        settings: {},
      }),
    );

    expect('data' in result && result.data?.teamId).toBe('acme');
    expect(captured[0]?.url).toBe('https://api.test/api/teams');
    expect(captured[0]?.method).toBe('POST');
  });

  it('getTeam GETs the team by encoded id', async () => {
    const captured = mockFetch(
      new Response(
        JSON.stringify({
          teamId: 'acme/1',
          concurrencyToken: 'c1',
          displayName: 'Acme',
          isDisabled: false,
        }),
        { status: 200, headers: { 'content-type': 'application/json' } },
      ),
    );
    const store = createStore();

    await store.dispatch(managementApi.endpoints.getTeam.initiate('acme/1'));

    expect(captured[0]?.url).toBe('https://api.test/api/teams/acme%2F1');
    expect(captured[0]?.method).toBe('GET');
  });

  it("listChildTeams GETs the team's child teams", async () => {
    const captured = mockFetch(
      jsonResponse({
        items: [
          {
            teamId: 'acme-web',
            concurrencyToken: 'c1',
            displayName: 'Acme Web',
            isDisabled: false,
            parentTeamId: 'acme',
          },
        ],
      }),
    );
    const store = createStore();

    const result = await store.dispatch(
      managementApi.endpoints.listChildTeams.initiate({ teamId: 'acme' }),
    );

    expect('data' in result && result.data?.pages[0]?.items[0]?.parentTeamId).toBe('acme');
    expect(captured[0]?.url).toBe('https://api.test/api/teams/acme/teams');
    expect(captured[0]?.method).toBe('GET');
  });

  it('createChildTeam POSTs the request body under the parent team', async () => {
    const captured = mockFetch(
      new Response(
        JSON.stringify({
          teamId: 'acme-web',
          concurrencyToken: 'c1',
          displayName: 'Acme Web',
          isDisabled: false,
          parentTeamId: 'acme',
        }),
        { status: 201, headers: { 'content-type': 'application/json' } },
      ),
    );
    const store = createStore();

    const result = await store.dispatch(
      managementApi.endpoints.createChildTeam.initiate({
        teamId: 'acme',
        body: { teamId: 'acme-web', displayName: 'Acme Web', isDisabled: false, settings: {} },
      }),
    );

    expect('data' in result && result.data?.teamId).toBe('acme-web');
    expect(captured[0]?.url).toBe('https://api.test/api/teams/acme/teams');
    expect(captured[0]?.method).toBe('POST');
    expect(JSON.parse(captured[0]?.body ?? '{}')).toEqual({
      teamId: 'acme-web',
      displayName: 'Acme Web',
      isDisabled: false,
      settings: {},
    });
  });

  it('updateTeam PATCHes a replace patch with If-Match', async () => {
    const captured = mockFetch(new Response(null, { status: 204 }));
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.updateTeam.initiate({
        teamId: 'acme',
        concurrencyToken: 'etag-7',
        fields: { displayName: 'Renamed', isDisabled: true },
      }),
    );

    expect(captured[0]?.url).toBe('https://api.test/api/teams/acme');
    expect(captured[0]?.method).toBe('PATCH');
    expect(captured[0]?.ifMatch).toBe('etag-7');
    expect(JSON.parse(captured[0]?.body ?? '[]')).toEqual([
      { op: 'replace', path: '/displayName', value: 'Renamed' },
      { op: 'replace', path: '/isDisabled', value: true },
    ]);
  });

  it('deleteTeam DELETEs under the team path', async () => {
    const captured = mockFetch(new Response(null, { status: 204 }));
    const store = createStore();

    await store.dispatch(managementApi.endpoints.deleteTeam.initiate({ teamId: 'acme' }));

    expect(captured[0]?.url).toBe('https://api.test/api/teams/acme');
    expect(captured[0]?.method).toBe('DELETE');
  });

  it('listChildTeams reports a failure without provided tags', async () => {
    mockFetch(new Response(null, { status: 404 }));
    const store = createStore();

    const result = await store.dispatch(
      managementApi.endpoints.listChildTeams.initiate({ teamId: 'missing' }),
    );

    expect('error' in result && result.error).toBeTruthy();
  });
});

describe('managementApi caller queries', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('getCaller GETs the caller', async () => {
    const captured = mockFetch(
      jsonResponse({ principalId: 'p-1', isPlatformAdministrator: false, assignments: [] }),
    );
    const store = createStore();

    await store.dispatch(managementApi.endpoints.getCaller.initiate());

    expect(captured[0]?.url).toBe('https://api.test/api/me');
    expect(captured[0]?.method).toBe('GET');
  });

  it('listCallerTeams GETs the caller teams', async () => {
    const captured = mockFetch(jsonResponse({ items: [] }));
    const store = createStore();

    await store.dispatch(managementApi.endpoints.listCallerTeams.initiate());

    expect(captured[0]?.url).toBe('https://api.test/api/me/teams');
    expect(captured[0]?.method).toBe('GET');
  });

  it('listCallerTenants GETs the caller tenants', async () => {
    const captured = mockFetch(jsonResponse({ items: [] }));
    const store = createStore();

    await store.dispatch(managementApi.endpoints.listCallerTenants.initiate());

    expect(captured[0]?.url).toBe('https://api.test/api/me/tenants');
    expect(captured[0]?.method).toBe('GET');
  });

  it('createTeam refetches a subscribed getCaller', async () => {
    const fetches = routeFetch((method) =>
      method === 'GET'
        ? jsonResponse({ principalId: 'p-1', isPlatformAdministrator: false, assignments: [] })
        : jsonResponse({ teamId: 'new' }, 201),
    );
    const store = createStore();
    await store.dispatch(managementApi.endpoints.getCaller.initiate());
    expect(fetches.count('GET', '/api/me')).toBe(1);

    await store.dispatch(
      managementApi.endpoints.createTeam.initiate({
        teamId: 'new',
        displayName: 'New',
        isDisabled: false,
        settings: {},
      }),
    );
    await settle(store);

    expect(fetches.count('GET', '/api/me')).toBe(2);
  });
});

describe('managementApi team usage', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('getTeamUsage GETs the team usage path without a range by default', async () => {
    const captured = mockFetch(
      new Response(JSON.stringify({ teamId: 'acme', from: '', to: '', entries: [] }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      }),
    );
    const store = createStore();

    await store.dispatch(managementApi.endpoints.getTeamUsage.initiate({ teamId: 'acme' }));

    expect(captured[0]?.url).toBe('https://api.test/api/teams/acme/usage');
    expect(captured[0]?.method).toBe('GET');
  });

  it('getTeamUsage sends the range as from/to query parameters', async () => {
    const captured = mockFetch(
      new Response(JSON.stringify({ teamId: 'acme', from: '', to: '', entries: [] }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      }),
    );
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.getTeamUsage.initiate({
        teamId: 'acme',
        from: '2026-10-01',
        to: '2026-10-21',
      }),
    );

    expect(captured[0]?.url).toBe(
      'https://api.test/api/teams/acme/usage?from=2026-10-01&to=2026-10-21',
    );
  });
});

describe('managementApi initial access token mint (tenant scope)', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('createInitialAccessToken POSTs the request body under the tenant grants path', async () => {
    const captured = mockFetch(
      new Response(
        JSON.stringify({
          initialAccessToken: 'iat-xyz',
          grantId: 'g-1',
          tenantId: 'acme',
          description: 'Partner X',
          createdWhen: '2026-01-01T00:00:00Z',
          expiresWhen: '2026-01-01T01:00:00Z',
        }),
        { status: 201, headers: { 'content-type': 'application/json' } },
      ),
    );
    const store = createStore();

    const result = await store.dispatch(
      managementApi.endpoints.createInitialAccessToken.initiate({
        tenantId: 'acme',
        cell: undefined,
        body: { description: 'Partner X', lifetimeSeconds: 3600 },
      }),
    );

    expect('data' in result && result.data?.initialAccessToken).toBe('iat-xyz');
    expect(captured[0]?.url).toBe('https://api.test/api/tenants/acme/grants/initial-access-tokens');
    expect(captured[0]?.method).toBe('POST');
    expect(JSON.parse(captured[0]?.body ?? '{}')).toEqual({
      description: 'Partner X',
      lifetimeSeconds: 3600,
    });
  });

  it('createInitialAccessToken sends the resource server a scoped token names', async () => {
    const captured = mockFetch(
      new Response(
        JSON.stringify({
          initialAccessToken: 'iat-xyz',
          grantId: 'g-1',
          tenantId: 'acme',
          description: null,
          resourceServerId: 'rs-orders',
          createdWhen: '2026-01-01T00:00:00Z',
          expiresWhen: null,
        }),
        { status: 201, headers: { 'content-type': 'application/json' } },
      ),
    );
    const store = createStore();

    const result = await store.dispatch(
      managementApi.endpoints.createInitialAccessToken.initiate({
        tenantId: 'acme',
        cell: undefined,
        body: { description: null, lifetimeSeconds: null, resourceServerId: 'rs-orders' },
      }),
    );

    expect('data' in result && result.data?.resourceServerId).toBe('rs-orders');
    expect(JSON.parse(captured[0]?.body ?? '{}')).toEqual({
      description: null,
      lifetimeSeconds: null,
      resourceServerId: 'rs-orders',
    });
  });
});

type QueryRequestCase = {
  readonly name: string;
  readonly url: string;
  readonly run: (store: AppStore) => PromiseLike<unknown>;
};

const queryRequestCases: readonly QueryRequestCase[] = [
  {
    name: 'getCapabilities',
    url: 'https://api.test/api/capabilities',
    run: (s) => s.dispatch(managementApi.endpoints.getCapabilities.initiate()),
  },
  {
    name: 'listTeams',
    url: 'https://api.test/api/teams',
    run: (s) => s.dispatch(managementApi.endpoints.listTeams.initiate()),
  },
  {
    name: 'listTenants',
    url: 'https://api.test/api/teams/acme/tenants',
    run: (s) => s.dispatch(managementApi.endpoints.listTenants.initiate({ teamId: 'acme' })),
  },
  {
    name: 'getTenant',
    url: 'https://api.test/api/tenants/acme%2F1',
    run: (s) =>
      s.dispatch(
        managementApi.endpoints.getTenant.initiate({ tenantId: 'acme/1', cell: undefined }),
      ),
  },
  {
    name: 'getTenantAddress (derived issuer)',
    url: 'https://api.test/api/tenants/acme/address',
    run: (s) =>
      s.dispatch(
        managementApi.endpoints.getTenantAddress.initiate({ tenantId: 'acme', cell: undefined }),
      ),
  },
  {
    name: 'getTenantAddress (proposed issuer)',
    url: 'https://api.test/api/tenants/acme/address?issuer=https%3A%2F%2Facme.example',
    run: (s) =>
      s.dispatch(
        managementApi.endpoints.getTenantAddress.initiate({
          tenantId: 'acme',
          cell: undefined,
          issuer: 'https://acme.example',
        }),
      ),
  },
  {
    name: 'getTenantAddress (proposed issuer and mTLS endpoint alias)',
    url: 'https://api.test/api/tenants/acme/address?issuer=https%3A%2F%2Facme.example&mtlsEndpointAliasAuthority=https%3A%2F%2Facme.mtls.example',
    run: (s) =>
      s.dispatch(
        managementApi.endpoints.getTenantAddress.initiate({
          tenantId: 'acme',
          cell: undefined,
          issuer: 'https://acme.example',
          mtlsEndpointAliasAuthority: 'https://acme.mtls.example',
        }),
      ),
  },
  {
    name: 'getTenantAddress (proposed mTLS endpoint alias only)',
    url: 'https://api.test/api/tenants/acme/address?mtlsEndpointAliasAuthority=https%3A%2F%2Facme.mtls.example',
    run: (s) =>
      s.dispatch(
        managementApi.endpoints.getTenantAddress.initiate({
          tenantId: 'acme',
          cell: undefined,
          mtlsEndpointAliasAuthority: 'https://acme.mtls.example',
        }),
      ),
  },
  {
    name: 'getNewTenantAddress (derived issuer)',
    url: 'https://api.test/api/teams/acme/tenants/acme/address',
    run: (s) =>
      s.dispatch(
        managementApi.endpoints.getNewTenantAddress.initiate({
          teamId: 'acme',
          tenantId: 'acme',
          cell: undefined,
        }),
      ),
  },
  {
    name: 'getNewTenantAddress (proposed issuer)',
    url: 'https://api.test/api/teams/acme/tenants/acme/address?issuer=https%3A%2F%2Facme.example',
    run: (s) =>
      s.dispatch(
        managementApi.endpoints.getNewTenantAddress.initiate({
          teamId: 'acme',
          tenantId: 'acme',
          cell: undefined,
          issuer: 'https://acme.example',
        }),
      ),
  },
  {
    name: 'getNewTenantAddress (proposed issuer and mTLS endpoint alias)',
    url: 'https://api.test/api/teams/acme/tenants/acme/address?issuer=https%3A%2F%2Facme.example&mtlsEndpointAliasAuthority=https%3A%2F%2Facme.mtls.example',
    run: (s) =>
      s.dispatch(
        managementApi.endpoints.getNewTenantAddress.initiate({
          teamId: 'acme',
          tenantId: 'acme',
          cell: undefined,
          issuer: 'https://acme.example',
          mtlsEndpointAliasAuthority: 'https://acme.mtls.example',
        }),
      ),
  },
  {
    name: 'getNewTenantAddress (proposed mTLS endpoint alias only)',
    url: 'https://api.test/api/teams/acme/tenants/acme/address?mtlsEndpointAliasAuthority=https%3A%2F%2Facme.mtls.example',
    run: (s) =>
      s.dispatch(
        managementApi.endpoints.getNewTenantAddress.initiate({
          teamId: 'acme',
          tenantId: 'acme',
          cell: undefined,
          mtlsEndpointAliasAuthority: 'https://acme.mtls.example',
        }),
      ),
  },
  {
    name: 'listClients (tenant scope)',
    url: 'https://api.test/api/tenants/acme/clients',
    run: (s) =>
      s.dispatch(
        managementApi.endpoints.listClients.initiate({ tenantId: 'acme', cell: undefined }),
      ),
  },
  {
    name: 'listClients (resource server filter)',
    url: 'https://api.test/api/tenants/acme/clients?resourceServerId=rs-orders',
    run: (s) =>
      s.dispatch(
        managementApi.endpoints.listClients.initiate({
          tenantId: 'acme',
          resourceServerId: 'rs-orders',
          cell: undefined,
        }),
      ),
  },
  {
    name: 'listResourceServers (tenant scope)',
    url: 'https://api.test/api/tenants/acme/resource-servers',
    run: (s) =>
      s.dispatch(
        managementApi.endpoints.listResourceServers.initiate({ tenantId: 'acme', cell: undefined }),
      ),
  },
  {
    name: 'listGrants (subject filter)',
    url: 'https://api.test/api/tenants/acme/grants?subjectId=user-1',
    run: (s) =>
      s.dispatch(
        managementApi.endpoints.listGrants.initiate({
          tenantId: 'acme',
          subjectId: 'user-1',
          cell: undefined,
        }),
      ),
  },
];

describe('managementApi query request shapes', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it.each(queryRequestCases)(
    '$name GETs $url with the anti-forgery header',
    async ({ url, run }) => {
      const captured = mockFetch(jsonResponse({ items: [] }));

      await run(createStore());

      expect(captured).toEqual([{ url, method: 'GET', antiforgery: '1', ifMatch: null, body: '' }]);
    },
  );
});

type MutationRequestCase = {
  readonly name: string;
  readonly method: string;
  readonly url: string;
  readonly run: (store: AppStore) => PromiseLike<unknown>;
};

const tenantResourceMutationCases: readonly MutationRequestCase[] = [
  {
    name: 'createClient',
    method: 'POST',
    url: 'https://api.test/api/tenants/acme/clients',
    run: (s) =>
      s.dispatch(
        managementApi.endpoints.createClient.initiate({
          tenantId: 'acme',
          cell: undefined,
          body: { isDisabled: false, settings: {} },
        }),
      ),
  },
  {
    name: 'updateClient',
    method: 'PATCH',
    url: 'https://api.test/api/tenants/acme/clients/web',
    run: (s) =>
      s.dispatch(
        managementApi.endpoints.updateClient.initiate({
          tenantId: 'acme',
          clientId: 'web',
          cell: undefined,
          concurrencyToken: 'etag',
          fields: { isDisabled: true },
        }),
      ),
  },
  {
    name: 'deleteClient',
    method: 'DELETE',
    url: 'https://api.test/api/tenants/acme/clients/web',
    run: (s) =>
      s.dispatch(
        managementApi.endpoints.deleteClient.initiate({
          tenantId: 'acme',
          clientId: 'web',
          cell: undefined,
        }),
      ),
  },
  {
    name: 'createResourceServer',
    method: 'POST',
    url: 'https://api.test/api/tenants/acme/resource-servers',
    run: (s) =>
      s.dispatch(
        managementApi.endpoints.createResourceServer.initiate({
          tenantId: 'acme',
          cell: undefined,
          body: { identifier: 'urn:api', name: 'API', isDisabled: false, settings: {} },
        }),
      ),
  },
  {
    name: 'updateResourceServer',
    method: 'PATCH',
    url: 'https://api.test/api/tenants/acme/resource-servers/rs-1',
    run: (s) =>
      s.dispatch(
        managementApi.endpoints.updateResourceServer.initiate({
          tenantId: 'acme',
          resourceServerId: 'rs-1',
          cell: undefined,
          concurrencyToken: 'etag',
          fields: { name: 'API', isDisabled: false },
        }),
      ),
  },
  {
    name: 'deleteResourceServer',
    method: 'DELETE',
    url: 'https://api.test/api/tenants/acme/resource-servers/rs-1',
    run: (s) =>
      s.dispatch(
        managementApi.endpoints.deleteResourceServer.initiate({
          tenantId: 'acme',
          resourceServerId: 'rs-1',
          cell: undefined,
        }),
      ),
  },
  {
    name: 'createScope',
    method: 'POST',
    url: 'https://api.test/api/tenants/acme/resource-servers/rs-1/scopes',
    run: (s) =>
      s.dispatch(
        managementApi.endpoints.createScope.initiate({
          tenantId: 'acme',
          resourceServerId: 'rs-1',
          cell: undefined,
          body: { value: 'read:x', description: null },
        }),
      ),
  },
  {
    name: 'updateScope',
    method: 'PUT',
    url: 'https://api.test/api/tenants/acme/resource-servers/rs-1/scopes/read%3Ax',
    run: (s) =>
      s.dispatch(
        managementApi.endpoints.updateScope.initiate({
          tenantId: 'acme',
          resourceServerId: 'rs-1',
          scopeValue: 'read:x',
          cell: undefined,
          body: { description: null },
        }),
      ),
  },
  {
    name: 'deleteScope',
    method: 'DELETE',
    url: 'https://api.test/api/tenants/acme/resource-servers/rs-1/scopes/read%3Ax',
    run: (s) =>
      s.dispatch(
        managementApi.endpoints.deleteScope.initiate({
          tenantId: 'acme',
          resourceServerId: 'rs-1',
          scopeValue: 'read:x',
          cell: undefined,
        }),
      ),
  },
];

describe('managementApi tenant resource mutations', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it.each(tenantResourceMutationCases)(
    '$name sends $method $url under the tenant',
    async ({ method, url, run }) => {
      const captured = mockFetch(new Response(null, { status: 204 }));

      await run(createStore());

      expect(captured).toHaveLength(1);
      expect(captured[0]?.method).toBe(method);
      expect(captured[0]?.url).toBe(url);
      expect(captured[0]?.antiforgery).toBe('1');
    },
  );
});

type ListTagCase = {
  readonly name: string;
  readonly path: string;
  readonly body: unknown;
  readonly run: (store: AppStore) => PromiseLike<{ readonly isError: boolean }>;
  readonly collectionTag: ManagementTag;
  readonly itemTag: ManagementTag;
};

const listTagCases: readonly ListTagCase[] = [
  {
    name: 'listTenants',
    path: '/api/teams/acme/tenants',
    body: { items: [{ tenantId: 'acme-dev' }] },
    run: (s) => s.dispatch(managementApi.endpoints.listTenants.initiate({ teamId: 'acme' })),
    collectionTag: { type: 'Tenant', id: 'team:acme:LIST' },
    itemTag: { type: 'Tenant', id: 'platform:acme-dev' },
  },
  {
    name: 'listTeams',
    path: '/api/teams',
    body: { items: [{ teamId: 'acme' }] },
    run: (s) => s.dispatch(managementApi.endpoints.listTeams.initiate()),
    collectionTag: { type: 'Team', id: 'platform:LIST' },
    itemTag: { type: 'Team', id: 'platform:acme' },
  },
  {
    name: 'listClients',
    path: '/api/tenants/acme/clients',
    body: { items: [{ clientId: 'web' }] },
    run: (s) =>
      s.dispatch(
        managementApi.endpoints.listClients.initiate({ tenantId: 'acme', cell: undefined }),
      ),
    collectionTag: { type: 'Client', id: 'tenant:acme:LIST' },
    itemTag: { type: 'Client', id: 'tenant:acme:web' },
  },
  {
    name: 'listMachineClients',
    path: '/api/teams/acme/clients',
    body: { items: [{ clientId: 'bot', tenantId: 'control' }] },
    run: (s) =>
      s.dispatch(
        managementApi.endpoints.listMachineClients.initiate({ kind: 'team', teamId: 'acme' }),
      ),
    collectionTag: { type: 'Client', id: 'team:acme:LIST' },
    itemTag: { type: 'Client', id: 'tenant:control:bot' },
  },
  {
    name: 'listResourceServers',
    path: '/api/tenants/acme/resource-servers',
    body: { items: [{ resourceServerId: 'rs-1' }] },
    run: (s) =>
      s.dispatch(
        managementApi.endpoints.listResourceServers.initiate({ tenantId: 'acme', cell: undefined }),
      ),
    collectionTag: { type: 'ResourceServer', id: 'tenant:acme:LIST' },
    itemTag: { type: 'ResourceServer', id: 'tenant:acme:rs-1' },
  },
  {
    name: 'listGrants',
    path: '/api/tenants/acme/grants',
    body: { items: [{ grantId: 'g-1' }] },
    run: (s) =>
      s.dispatch(
        managementApi.endpoints.listGrants.initiate({ tenantId: 'acme', cell: undefined }),
      ),
    collectionTag: { type: 'Grant', id: 'tenant:acme:LIST' },
    itemTag: { type: 'Grant', id: 'tenant:acme:g-1' },
  },
];

describe('managementApi provided cache tags', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it.each(listTagCases)(
    '$name provides its collection tag and one tag per returned item',
    async ({ name, path, body, run, collectionTag, itemTag }) => {
      routeFetch((_method, requested) =>
        requested === path ? jsonResponse(body) : jsonResponse({ title: 'Not Found' }, 404),
      );
      const store = createStore();

      expect((await run(store)).isError).toBe(false);

      expect(invalidatedBy(store, [collectionTag])).toEqual([name]);
      expect(invalidatedBy(store, [itemTag])).toEqual([name]);
      expect(invalidatedBy(store, [{ type: itemTag.type, id: 'unrelated' }])).toEqual([]);
    },
  );

  it.each(listTagCases)(
    '$name still provides its collection tag when the request fails',
    async ({ name, run, collectionTag, itemTag }) => {
      routeFetch(() => jsonResponse({ title: 'Service Unavailable' }, 503));
      const store = createStore();

      expect((await run(store)).isError).toBe(true);

      expect(invalidatedBy(store, [collectionTag])).toEqual([name]);
      expect(invalidatedBy(store, [itemTag])).toEqual([]);
    },
  );
});

type CachedQuery = {
  readonly label: string;
  readonly path: string;
  readonly body: unknown;
  readonly subscribe: (store: AppStore) => PromiseLike<unknown>;
};

/**
 * One subscribed cache entry per tagged (and a few untagged) query, spanning both scopes. The same
 * resource ids recur in a second tenant, so every mutation also proves that it
 * leaves the other scopes' caches alone.
 */
const cachedQueries: readonly CachedQuery[] = [
  {
    label: 'capabilities',
    path: '/api/capabilities',
    body: {},
    subscribe: (s) => s.dispatch(managementApi.endpoints.getCapabilities.initiate()),
  },
  {
    label: 'tenants',
    path: '/api/teams/acme/tenants',
    body: { items: [{ tenantId: 'acme' }, { tenantId: 'acme-dev' }, { tenantId: 'other' }] },
    subscribe: (s) => s.dispatch(managementApi.endpoints.listTenants.initiate({ teamId: 'acme' })),
  },
  {
    label: 'tenants:globex',
    path: '/api/teams/globex/tenants',
    body: { items: [{ tenantId: 'globex-dev' }] },
    subscribe: (s) =>
      s.dispatch(managementApi.endpoints.listTenants.initiate({ teamId: 'globex' })),
  },
  {
    label: 'tenant:acme',
    path: '/api/tenants/acme',
    body: { tenantId: 'acme' },
    subscribe: (s) =>
      s.dispatch(managementApi.endpoints.getTenant.initiate({ tenantId: 'acme', cell: undefined })),
  },
  {
    label: 'tenant:acme-dev',
    path: '/api/tenants/acme-dev',
    body: { tenantId: 'acme-dev' },
    subscribe: (s) =>
      s.dispatch(
        managementApi.endpoints.getTenant.initiate({ tenantId: 'acme-dev', cell: undefined }),
      ),
  },
  {
    label: 'tenant:other',
    path: '/api/tenants/other',
    body: { tenantId: 'other' },
    subscribe: (s) =>
      s.dispatch(
        managementApi.endpoints.getTenant.initiate({ tenantId: 'other', cell: undefined }),
      ),
  },
  {
    label: 'teams',
    path: '/api/teams',
    body: { items: [{ teamId: 'acme' }, { teamId: 'globex' }] },
    subscribe: (s) => s.dispatch(managementApi.endpoints.listTeams.initiate()),
  },
  {
    label: 'team:acme',
    path: '/api/teams/acme',
    body: { teamId: 'acme' },
    subscribe: (s) => s.dispatch(managementApi.endpoints.getTeam.initiate('acme')),
  },
  {
    label: 'team:globex',
    path: '/api/teams/globex',
    body: { teamId: 'globex' },
    subscribe: (s) => s.dispatch(managementApi.endpoints.getTeam.initiate('globex')),
  },
  {
    label: 'childTeams:acme',
    path: '/api/teams/acme/teams',
    body: { items: [{ teamId: 'acme-web' }] },
    subscribe: (s) =>
      s.dispatch(managementApi.endpoints.listChildTeams.initiate({ teamId: 'acme' })),
  },
  {
    label: 'childTeams:globex',
    path: '/api/teams/globex/teams',
    body: { items: [{ teamId: 'globex-web' }] },
    subscribe: (s) =>
      s.dispatch(managementApi.endpoints.listChildTeams.initiate({ teamId: 'globex' })),
  },
  {
    label: 'clients',
    path: '/api/tenants/acme/clients',
    body: { items: [{ clientId: 'web' }] },
    subscribe: (s) =>
      s.dispatch(
        managementApi.endpoints.listClients.initiate({ tenantId: 'acme', cell: undefined }),
      ),
  },
  {
    label: 'clients:other',
    path: '/api/tenants/other/clients',
    body: { items: [{ clientId: 'web' }] },
    subscribe: (s) =>
      s.dispatch(
        managementApi.endpoints.listClients.initiate({ tenantId: 'other', cell: undefined }),
      ),
  },
  {
    label: 'resourceServers',
    path: '/api/tenants/acme/resource-servers',
    body: { items: [{ resourceServerId: 'rs-1' }] },
    subscribe: (s) =>
      s.dispatch(
        managementApi.endpoints.listResourceServers.initiate({ tenantId: 'acme', cell: undefined }),
      ),
  },
  {
    label: 'resourceServers:other',
    path: '/api/tenants/other/resource-servers',
    body: { items: [{ resourceServerId: 'rs-1' }] },
    subscribe: (s) =>
      s.dispatch(
        managementApi.endpoints.listResourceServers.initiate({
          tenantId: 'other',
          cell: undefined,
        }),
      ),
  },
  {
    label: 'grants',
    path: '/api/tenants/acme/grants',
    body: { items: [{ grantId: 'g-1' }] },
    subscribe: (s) =>
      s.dispatch(
        managementApi.endpoints.listGrants.initiate({ tenantId: 'acme', cell: undefined }),
      ),
  },
  {
    label: 'grants:other',
    path: '/api/tenants/other/grants',
    body: { items: [{ grantId: 'g-1' }] },
    subscribe: (s) =>
      s.dispatch(
        managementApi.endpoints.listGrants.initiate({ tenantId: 'other', cell: undefined }),
      ),
  },
];

type InvalidationCase = {
  readonly name: string;
  readonly mutate: (store: AppStore) => PromiseLike<unknown>;
  /** The {@link cachedQueries} labels expected to refetch, in fixture order. */
  readonly refetches: readonly string[];
};

const invalidationCases: readonly InvalidationCase[] = [
  {
    name: 'createTenant',
    mutate: (s) =>
      s.dispatch(
        managementApi.endpoints.createTenant.initiate({
          teamId: 'acme',
          cell: undefined,
          body: {
            tenantId: 'new',
            issuer: null,
            mtlsEndpointAliasAuthority: null,
            displayName: 'New',
            isDisabled: false,
            settings: {},
          },
        }),
      ),
    refetches: ['tenants'],
  },
  {
    name: 'updateTenant',
    mutate: (s) =>
      s.dispatch(
        managementApi.endpoints.updateTenant.initiate({
          teamId: 'acme',
          tenantId: 'acme-dev',
          cell: undefined,
          concurrencyToken: 'etag',
          fields: {
            issuer: null,
            mtlsEndpointAliasAuthority: null,
            displayName: 'Acme',
            isDisabled: false,
          },
        }),
      ),
    refetches: ['tenants', 'tenant:acme-dev'],
  },
  {
    name: 'deleteTenant',
    mutate: (s) =>
      s.dispatch(
        managementApi.endpoints.deleteTenant.initiate({
          teamId: 'acme',
          tenantId: 'acme-dev',
          cell: undefined,
        }),
      ),
    refetches: ['tenants', 'tenant:acme-dev'],
  },
  {
    name: 'previewTenantPromotion',
    mutate: (s) =>
      s.dispatch(
        managementApi.endpoints.previewTenantPromotion.initiate({
          tenantId: 'acme',
          cell: undefined,
          body: { sourceTenantId: 'acme-dev', environmentSettings: [] },
        }),
      ),
    refetches: [],
  },
  {
    name: 'promoteTenant',
    mutate: (s) =>
      s.dispatch(
        managementApi.endpoints.promoteTenant.initiate({
          tenantId: 'acme',
          cell: undefined,
          body: { sourceTenantId: 'acme-dev', environmentSettings: [] },
        }),
      ),
    refetches: ['tenants', 'tenant:acme', 'clients', 'resourceServers'],
  },
  {
    name: 'releaseMovedTenant',
    mutate: (s) =>
      s.dispatch(
        managementApi.endpoints.releaseMovedTenant.initiate({ cell: 'eu-1', tenantId: 'acme-dev' }),
      ),
    refetches: ['tenants', 'tenant:acme-dev'],
  },
  {
    name: 'createTeam',
    mutate: (s) =>
      s.dispatch(
        managementApi.endpoints.createTeam.initiate({
          teamId: 'new',
          displayName: 'New',
          isDisabled: false,
          settings: {},
        }),
      ),
    refetches: ['teams'],
  },
  {
    name: 'updateTeam',
    mutate: (s) =>
      s.dispatch(
        managementApi.endpoints.updateTeam.initiate({
          teamId: 'acme',
          concurrencyToken: 'etag',
          fields: { displayName: 'Acme', isDisabled: false },
        }),
      ),
    refetches: ['teams', 'team:acme'],
  },
  {
    name: 'deleteTeam',
    mutate: (s) => s.dispatch(managementApi.endpoints.deleteTeam.initiate({ teamId: 'acme' })),
    refetches: ['teams', 'team:acme'],
  },
  {
    name: 'updateTeam of a child team',
    mutate: (s) =>
      s.dispatch(
        managementApi.endpoints.updateTeam.initiate({
          teamId: 'acme-web',
          concurrencyToken: 'etag',
          fields: { displayName: 'Web', isDisabled: false },
        }),
      ),
    refetches: ['teams', 'childTeams:acme'],
  },
  {
    name: 'deleteTeam of a child team',
    mutate: (s) => s.dispatch(managementApi.endpoints.deleteTeam.initiate({ teamId: 'acme-web' })),
    refetches: ['teams', 'childTeams:acme'],
  },
  {
    name: 'createChildTeam',
    mutate: (s) =>
      s.dispatch(
        managementApi.endpoints.createChildTeam.initiate({
          teamId: 'acme',
          body: { teamId: 'new', displayName: 'New', isDisabled: false, settings: {} },
        }),
      ),
    refetches: ['teams', 'childTeams:acme'],
  },
  {
    name: 'createClient',
    mutate: (s) =>
      s.dispatch(
        managementApi.endpoints.createClient.initiate({
          tenantId: 'acme',
          cell: undefined,
          body: { isDisabled: false, settings: {} },
        }),
      ),
    refetches: ['clients'],
  },
  {
    name: 'updateClient',
    mutate: (s) =>
      s.dispatch(
        managementApi.endpoints.updateClient.initiate({
          tenantId: 'acme',
          clientId: 'web',
          cell: undefined,
          concurrencyToken: 'etag',
          fields: { isDisabled: true },
        }),
      ),
    refetches: ['clients'],
  },
  {
    name: 'deleteClient',
    mutate: (s) =>
      s.dispatch(
        managementApi.endpoints.deleteClient.initiate({
          tenantId: 'acme',
          clientId: 'web',
          cell: undefined,
        }),
      ),
    refetches: ['clients'],
  },
  {
    name: 'createResourceServer',
    mutate: (s) =>
      s.dispatch(
        managementApi.endpoints.createResourceServer.initiate({
          tenantId: 'acme',
          cell: undefined,
          body: { identifier: 'urn:api', name: 'API', isDisabled: false, settings: {} },
        }),
      ),
    refetches: ['resourceServers'],
  },
  {
    name: 'updateResourceServer',
    mutate: (s) =>
      s.dispatch(
        managementApi.endpoints.updateResourceServer.initiate({
          tenantId: 'acme',
          resourceServerId: 'rs-1',
          cell: undefined,
          concurrencyToken: 'etag',
          fields: { name: 'API', isDisabled: true },
        }),
      ),
    refetches: ['resourceServers'],
  },
  {
    name: 'deleteResourceServer',
    mutate: (s) =>
      s.dispatch(
        managementApi.endpoints.deleteResourceServer.initiate({
          tenantId: 'acme',
          resourceServerId: 'rs-1',
          cell: undefined,
        }),
      ),
    refetches: ['resourceServers'],
  },
  {
    name: 'createScope',
    mutate: (s) =>
      s.dispatch(
        managementApi.endpoints.createScope.initiate({
          tenantId: 'acme',
          resourceServerId: 'rs-1',
          cell: undefined,
          body: { value: 'read:x', description: null },
        }),
      ),
    refetches: ['resourceServers'],
  },
  {
    name: 'updateScope',
    mutate: (s) =>
      s.dispatch(
        managementApi.endpoints.updateScope.initiate({
          tenantId: 'acme',
          resourceServerId: 'rs-1',
          scopeValue: 'read:x',
          cell: undefined,
          body: { description: 'Read X' },
        }),
      ),
    refetches: ['resourceServers'],
  },
  {
    name: 'deleteScope',
    mutate: (s) =>
      s.dispatch(
        managementApi.endpoints.deleteScope.initiate({
          tenantId: 'acme',
          resourceServerId: 'rs-1',
          scopeValue: 'read:x',
          cell: undefined,
        }),
      ),
    refetches: ['resourceServers'],
  },
  {
    name: 'revokeGrant',
    mutate: (s) =>
      s.dispatch(
        managementApi.endpoints.revokeGrant.initiate({
          tenantId: 'acme',
          grantId: 'g-1',
          cell: undefined,
        }),
      ),
    refetches: ['grants'],
  },
  {
    name: 'revokeGrants',
    mutate: (s) =>
      s.dispatch(
        managementApi.endpoints.revokeGrants.initiate({
          tenantId: 'acme',
          subjectId: 'user-1',
          cell: undefined,
        }),
      ),
    refetches: ['grants'],
  },
  {
    name: 'createInitialAccessToken',
    mutate: (s) =>
      s.dispatch(
        managementApi.endpoints.createInitialAccessToken.initiate({
          tenantId: 'acme',
          cell: undefined,
          body: { description: null, lifetimeSeconds: null },
        }),
      ),
    refetches: ['grants'],
  },
  {
    name: 'deleteClient in another tenant',
    mutate: (s) =>
      s.dispatch(
        managementApi.endpoints.deleteClient.initiate({
          tenantId: 'other',
          clientId: 'web',
          cell: undefined,
        }),
      ),
    refetches: ['clients:other'],
  },
  {
    name: 'updateResourceServer in another tenant',
    mutate: (s) =>
      s.dispatch(
        managementApi.endpoints.updateResourceServer.initiate({
          tenantId: 'other',
          resourceServerId: 'rs-1',
          cell: undefined,
          concurrencyToken: 'etag',
          fields: { name: 'API', isDisabled: true },
        }),
      ),
    refetches: ['resourceServers:other'],
  },
  {
    name: 'revokeGrant in another tenant',
    mutate: (s) =>
      s.dispatch(
        managementApi.endpoints.revokeGrant.initiate({
          tenantId: 'other',
          grantId: 'g-1',
          cell: undefined,
        }),
      ),
    refetches: ['grants:other'],
  },
];

describe('managementApi cache invalidation', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it.each(invalidationCases)(
    '$name refetches only the cache entries it affects',
    async ({ mutate, refetches }) => {
      const fetches = routeFetch((method, path) =>
        method === 'GET'
          ? jsonResponse(cachedQueries.find((q) => q.path === path)?.body ?? {})
          : new Response(null, { status: 204 }),
      );
      const store = createStore();
      await Promise.all(cachedQueries.map((q) => q.subscribe(store)));
      expect(cachedQueries.filter((q) => fetches.count('GET', q.path) !== 1)).toEqual([]);

      await mutate(store);
      await settle(store);

      expect(fetches.calls.filter((call) => !call.startsWith('GET '))).toHaveLength(1);
      const refetched = cachedQueries
        .filter((q) => fetches.count('GET', q.path) === 2)
        .map((q) => q.label);
      expect(refetched).toEqual(refetches);
    },
  );

  it('also refetches after a rejected mutation so a stale concurrency token is replaced', async () => {
    const fetches = routeFetch((method, path) =>
      method === 'GET'
        ? jsonResponse(cachedQueries.find((q) => q.path === path)?.body ?? {})
        : jsonResponse({ title: 'Precondition Failed' }, 412),
    );
    const store = createStore();
    await Promise.all(cachedQueries.map((q) => q.subscribe(store)));

    const result = await store.dispatch(
      managementApi.endpoints.updateTenant.initiate({
        teamId: 'acme',
        tenantId: 'acme-dev',
        cell: undefined,
        concurrencyToken: 'stale',
        fields: {
          issuer: null,
          mtlsEndpointAliasAuthority: null,
          displayName: 'Acme',
          isDisabled: false,
        },
      }),
    );
    await settle(store);

    expect('error' in result && result.error).toMatchObject({ status: 412 });
    expect(fetches.count('GET', '/api/teams/acme/tenants')).toBe(2);
    expect(fetches.count('GET', '/api/teams/globex/tenants')).toBe(1);
    expect(fetches.count('GET', '/api/tenants/acme-dev')).toBe(2);
    expect(fetches.count('GET', '/api/tenants/acme')).toBe(1);
    expect(fetches.count('GET', '/api/tenants/other')).toBe(1);
  });
});

type OwnerCase = {
  readonly key: string;
  readonly owner: ManagementOwner;
  readonly path: string;
};

type SecretOwnerCase = OwnerCase & { readonly owner: SecretOwner };

/**
 * One secret-holding owner of every kind, in a tenant, with the API path its requests
 * use.
 */
const secretOwnerCases: readonly SecretOwnerCase[] = [
  { key: 'tenant', owner: { kind: 'tenant', tenantId: 'acme' }, path: '/api/tenants/acme' },
  {
    key: 'tenant-client',
    owner: { kind: 'client', tenantId: 'acme', clientId: 'web' },
    path: '/api/tenants/acme/clients/web',
  },
  {
    key: 'tenant-resource-server',
    owner: { kind: 'resource-server', tenantId: 'acme', resourceServerId: 'rs-1' },
    path: '/api/tenants/acme/resource-servers/rs-1',
  },
];

/** Every owner of settings: the platform and a team, which hold settings only, and the secret-holding owners. */
const ownerCases: readonly OwnerCase[] = [
  { key: 'platform', owner: { kind: 'platform' }, path: '/api/platform' },
  { key: 'team', owner: { kind: 'team', teamId: 'acme' }, path: '/api/teams/acme' },
  ...secretOwnerCases,
];

const encryptionKeyOwnerCases = ownerCases.filter(
  (c): c is OwnerCase & { readonly owner: EncryptionKeyOwner } =>
    c.owner.kind === 'platform' || c.owner.kind === 'tenant',
);

describe('managementApi encryption key requests', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it.each(encryptionKeyOwnerCases)(
    'listEncryptionKeys GETs the encryption keys of the $key owner',
    async ({ owner, path }) => {
      const captured = mockFetch(
        jsonResponse({
          keys: [],
          recoveryConfigured: true,
          rootKey: null,
          rootKeyProviders: [],
          rootKeyAccessBinding: null,
        }),
      );
      const store = createStore();

      await store.dispatch(
        managementApi.endpoints.listEncryptionKeys.initiate({ ...owner, cell: undefined }),
      );

      expect(captured[0]?.url).toBe(`https://api.test${path}/encryption-keys`);
      expect(captured[0]?.method).toBe('GET');
    },
  );

  it.each(encryptionKeyOwnerCases)(
    'rekeyEncryptionKeys POSTs the rekey of the $key owner and refetches only its keys',
    async ({ owner, path }) => {
      const otherOwner: EncryptionKeyOwner =
        owner.kind === 'platform' ? { kind: 'tenant', tenantId: 'other' } : { kind: 'platform' };
      const captured = mockFetch(
        jsonResponse({
          keys: [],
          recoveryConfigured: true,
          rootKey: null,
          rootKeyProviders: [],
          rootKeyAccessBinding: null,
        }),
      );
      const store = createStore();
      const subscriptions = [
        store.dispatch(
          managementApi.endpoints.listEncryptionKeys.initiate({ ...owner, cell: undefined }),
        ),
        store.dispatch(
          managementApi.endpoints.listEncryptionKeys.initiate({ ...otherOwner, cell: undefined }),
        ),
      ];
      await Promise.all(subscriptions);
      const before = captured.length;

      await store.dispatch(
        managementApi.endpoints.rekeyEncryptionKeys.initiate({ ...owner, cell: undefined }),
      );
      await settle(store);

      expect(captured[before]?.url).toBe(`https://api.test${path}/encryption-keys/rekey`);
      expect(captured[before]?.method).toBe('POST');
      const refetched = captured.slice(before + 1).map((request) => request.url);
      expect(refetched).toEqual([`https://api.test${path}/encryption-keys`]);
      for (const subscription of subscriptions) subscription.unsubscribe();
    },
  );
});

describe('managementApi tenant root key requests', () => {
  const rootKey = {
    provider: 'aws-kms',
    keyReference: 'arn:aws:kms:us-east-1:111122223333:key/abc',
    accessIdentity: 'arn:aws:iam::111122223333:role/ncode-identity',
    state: 'active',
    createdWhen: '2026-10-01T09:00:00Z',
    stateChangedWhen: '2026-10-01T09:00:00Z',
    validatedWhen: '2026-10-01T10:00:00Z',
    failedWhen: null,
    failure: null,
  };
  const tenant: EncryptionKeyOwner = { kind: 'tenant', tenantId: 'acme' };
  const otherTenant: EncryptionKeyOwner = { kind: 'tenant', tenantId: 'other' };

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  /** Primes the keys of two tenants, runs the mutation and returns the requests it caused. */
  async function runMutation(
    response: Response,
    dispatch: (store: ReturnType<typeof createStore>) => PromiseLike<unknown>,
  ) {
    const captured = mockFetch(response);
    const store = createStore();
    const subscriptions = [
      store.dispatch(
        managementApi.endpoints.listEncryptionKeys.initiate({ ...tenant, cell: undefined }),
      ),
      store.dispatch(
        managementApi.endpoints.listEncryptionKeys.initiate({ ...otherTenant, cell: undefined }),
      ),
    ];
    await Promise.all(subscriptions);
    const before = captured.length;
    await dispatch(store);
    await settle(store);
    for (const subscription of subscriptions) subscription.unsubscribe();
    return { mutation: captured[before], refetched: captured.slice(before + 1) };
  }

  it('setTenantRootKey PUTs the root key and refetches only the tenant keys', async () => {
    const body = {
      provider: 'aws-kms',
      keyReference: rootKey.keyReference,
      accessIdentity: rootKey.accessIdentity,
    };

    const { mutation, refetched } = await runMutation(
      jsonResponse({ rootKey, activeKey: null, retiredKey: null, rewrappedKeys: 2, failedKeys: 0 }),
      (store) =>
        store.dispatch(
          managementApi.endpoints.setTenantRootKey.initiate({
            tenantId: 'acme',
            body,
            cell: undefined,
          }),
        ),
    );

    expect(mutation?.url).toBe('https://api.test/api/tenants/acme/encryption-keys/root-key');
    expect(mutation?.method).toBe('PUT');
    expect(JSON.parse(mutation?.body ?? '{}')).toEqual(body);
    expect(refetched.map((request) => request.url)).toEqual([
      'https://api.test/api/tenants/acme/encryption-keys',
    ]);
  });

  it('removeTenantRootKey DELETEs the root key and resolves null on 204', async () => {
    const captured = mockFetch(new Response(null, { status: 204 }));
    const store = createStore();

    const result = await store.dispatch(
      managementApi.endpoints.removeTenantRootKey.initiate({ tenantId: 'acme', cell: undefined }),
    );

    expect(captured[0]?.url).toBe('https://api.test/api/tenants/acme/encryption-keys/root-key');
    expect(captured[0]?.method).toBe('DELETE');
    expect(result).toMatchObject({ data: null });
  });

  it('removeTenantRootKey resolves the removing root key on 202 and refetches only the tenant keys', async () => {
    const removing = { ...rootKey, state: 'removing' };

    const { mutation, refetched } = await runMutation(jsonResponse(removing, 202), (store) =>
      store.dispatch(
        managementApi.endpoints.removeTenantRootKey.initiate({ tenantId: 'acme', cell: undefined }),
      ),
    );

    expect(mutation?.method).toBe('DELETE');
    expect(refetched.map((request) => request.url)).toEqual([
      'https://api.test/api/tenants/acme/encryption-keys',
    ]);
  });

  it('validateTenantRootKey POSTs the validation and refetches only the tenant keys', async () => {
    const { mutation, refetched } = await runMutation(jsonResponse(rootKey), (store) =>
      store.dispatch(
        managementApi.endpoints.validateTenantRootKey.initiate({
          tenantId: 'acme',
          cell: undefined,
        }),
      ),
    );

    expect(mutation?.url).toBe(
      'https://api.test/api/tenants/acme/encryption-keys/root-key/validate',
    );
    expect(mutation?.method).toBe('POST');
    expect(refetched.map((request) => request.url)).toEqual([
      'https://api.test/api/tenants/acme/encryption-keys',
    ]);
  });
});

describe('managementApi cells', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('listCells GETs the cells of the platform', async () => {
    const cells = [{ name: 'eu-1', region: 'eu', kind: 'shared', isControlPlane: true }];
    const captured = mockFetch(jsonResponse(cells));
    const store = createStore();

    const result = await store.dispatch(managementApi.endpoints.listCells.initiate());

    expect(captured[0]?.url).toBe('https://api.test/api/cells');
    expect(captured[0]?.method).toBe('GET');
    expect(result).toMatchObject({ data: cells });
  });
});

describe('managementApi across cells', () => {
  const tenant = (tenantId: string) => ({
    tenantId,
    teamId: 'acme',
    concurrencyToken: 'c1',
    issuer: null,
    mtlsEndpointAliasAuthority: null,
    isDisabled: false,
    displayName: tenantId,
  });
  const cellRuntime: BffRuntimeConfig = { ...runtime, cells: ['eu-1', 'us-1'] };
  const createCellStore = () => createAppStore({ config: { runtime: cellRuntime } });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('sends a tenant-scoped call with a cell to the cell forwarder', async () => {
    const captured = mockFetch(jsonResponse({ items: [] }));
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.listClients.initiate({ tenantId: 'acme-prod', cell: 'eu-1' }),
    );

    expect(captured[0]?.url).toBe('https://api.test/cells/eu-1/api/tenants/acme-prod/clients');
  });

  it('keeps the query string of a tenant-scoped call with a cell', async () => {
    const captured = mockFetch(jsonResponse({ items: [] }));
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.listClients.initiate({
        tenantId: 'acme-prod',
        resourceServerId: 'rs-1',
        cell: 'eu-1',
      }),
    );

    expect(captured[0]?.url).toBe(
      'https://api.test/cells/eu-1/api/tenants/acme-prod/clients?resourceServerId=rs-1',
    );
  });

  it('sends a tenant-scoped call without a cell to the control plane', async () => {
    const captured = mockFetch(jsonResponse({ items: [] }));
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.listClients.initiate({ tenantId: 'acme-prod', cell: undefined }),
    );

    expect(captured[0]?.url).toBe('https://api.test/api/tenants/acme-prod/clients');
  });

  it('sends an owner-scoped call with a cell to the cell forwarder', async () => {
    const captured = mockFetch(jsonResponse({ secrets: [] }));
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.listSecrets.initiate({
        kind: 'tenant',
        tenantId: 'acme-prod',
        cell: 'us-1',
      }),
    );

    expect(captured[0]?.url).toBe('https://api.test/cells/us-1/api/tenants/acme-prod/secrets');
  });

  it('sends a mutation with a cell to the cell forwarder with its method and body', async () => {
    const captured = mockFetch(new Response(null, { status: 204 }));
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.revokeGrant.initiate({
        tenantId: 'acme-prod',
        grantId: 'g-1',
        cell: 'eu-1',
      }),
    );

    expect(captured[0]?.url).toBe('https://api.test/cells/eu-1/api/tenants/acme-prod/grants/g-1');
    expect(captured[0]?.method).toBe('DELETE');
  });

  it('getTenant sends a call with a cell to the cell forwarder and one without to the control plane', async () => {
    const captured = mockFetch(jsonResponse(tenant('acme-prod')));
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.getTenant.initiate({ tenantId: 'acme-prod', cell: 'eu-1' }),
    );
    await store.dispatch(
      managementApi.endpoints.getTenant.initiate({ tenantId: 'acme-prod', cell: undefined }),
    );

    expect(captured.map((request) => request.url)).toEqual([
      'https://api.test/cells/eu-1/api/tenants/acme-prod',
      'https://api.test/api/tenants/acme-prod',
    ]);
  });

  it('getNewTenantAddress sends a call with a cell to the cell forwarder and one without to the control plane', async () => {
    const captured = mockFetch(jsonResponse({ issuer: 'https://new.auth.test' }));
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.getNewTenantAddress.initiate({
        teamId: 'acme',
        tenantId: 'new',
        issuer: 'https://new.auth.test',
        cell: 'eu-1',
      }),
    );
    await store.dispatch(
      managementApi.endpoints.getNewTenantAddress.initiate({
        teamId: 'acme',
        tenantId: 'new',
        cell: undefined,
      }),
    );

    expect(captured.map((request) => request.url)).toEqual([
      'https://api.test/cells/eu-1/api/teams/acme/tenants/new/address?issuer=https%3A%2F%2Fnew.auth.test',
      'https://api.test/api/teams/acme/tenants/new/address',
    ]);
  });

  it('createTenant sends a call with a cell to the cell forwarder and one without to the control plane', async () => {
    const captured = mockFetch(jsonResponse(tenant('new'), 201));
    const store = createStore();
    const body = {
      tenantId: 'new',
      issuer: 'https://new.auth.test',
      mtlsEndpointAliasAuthority: 'https://new.mtls.test',
      displayName: 'New',
      isDisabled: false,
      settings: {},
    };

    await store.dispatch(
      managementApi.endpoints.createTenant.initiate({ teamId: 'acme', body, cell: 'eu-1' }),
    );
    await store.dispatch(
      managementApi.endpoints.createTenant.initiate({ teamId: 'acme', body, cell: undefined }),
    );

    expect(captured.map((request) => `${request.method} ${request.url}`)).toEqual([
      'POST https://api.test/cells/eu-1/api/teams/acme/tenants',
      'POST https://api.test/api/teams/acme/tenants',
    ]);
    expect(JSON.parse(captured[0]?.body ?? '{}')).toEqual(body);
  });

  it('previewTenantPromotion and promoteTenant send a call with a cell to the cell forwarder and one without to the control plane', async () => {
    const captured = mockFetch(jsonResponse({ changes: [] }));
    const store = createStore();
    const body = {
      sourceTenantId: 'acme-dev',
      environmentSettings: [],
    };

    await store.dispatch(
      managementApi.endpoints.previewTenantPromotion.initiate({
        tenantId: 'acme-prod',
        body,
        cell: 'eu-1',
      }),
    );
    await store.dispatch(
      managementApi.endpoints.previewTenantPromotion.initiate({
        tenantId: 'acme-prod',
        body,
        cell: undefined,
      }),
    );
    await store.dispatch(
      managementApi.endpoints.promoteTenant.initiate({ tenantId: 'acme-prod', body, cell: 'eu-1' }),
    );
    await store.dispatch(
      managementApi.endpoints.promoteTenant.initiate({
        tenantId: 'acme-prod',
        body,
        cell: undefined,
      }),
    );

    expect(captured.map((request) => request.url)).toEqual([
      'https://api.test/cells/eu-1/api/tenants/acme-prod/promotions/preview',
      'https://api.test/api/tenants/acme-prod/promotions/preview',
      'https://api.test/cells/eu-1/api/tenants/acme-prod/promotions',
      'https://api.test/api/tenants/acme-prod/promotions',
    ]);
  });

  it('getTenantConfiguration sends a call with a cell to the cell forwarder and one without to the control plane', async () => {
    const captured = mockFetch(jsonResponse({ sourceTenantId: 'acme-dev', region: 'eu' }));
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.getTenantConfiguration.initiate({
        tenantId: 'acme-dev',
        cell: 'eu-1',
      }),
    );
    await store.dispatch(
      managementApi.endpoints.getTenantConfiguration.initiate({
        tenantId: 'acme-dev',
        cell: undefined,
      }),
    );

    expect(captured.map((request) => `${request.method} ${request.url}`)).toEqual([
      'GET https://api.test/cells/eu-1/api/tenants/acme-dev/configuration',
      'GET https://api.test/api/tenants/acme-dev/configuration',
    ]);
  });

  it('the four calls of a move are addressed to their cells and carry their bodies', async () => {
    const captured: CapturedRequest[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const request = input as Request;
        captured.push({
          url: request.url,
          method: request.method,
          antiforgery: request.headers.get('x-csrf'),
          ifMatch: request.headers.get('if-match'),
          body: await request.clone().text(),
        });
        if (request.url.endsWith('/transfer-keys')) {
          return jsonResponse({ kid: 'k-1', jwk: { kty: 'EC', crv: 'P-256' } });
        }
        if (request.url.endsWith('/export')) {
          return jsonResponse({ package: 'jwe' });
        }
        if (request.url.endsWith('/api/tenant-imports')) {
          return jsonResponse({ tenantId: 'acme-dev', sourceCell: 'eu-1' });
        }
        return new Response(null, { status: 204 });
      }),
    );
    const store = createStore();

    await store.dispatch(managementApi.endpoints.createTransferKey.initiate({ cell: 'eu-2' }));
    await store.dispatch(
      managementApi.endpoints.exportTenant.initiate({
        cell: 'eu-1',
        tenantId: 'acme-dev',
        targetCell: 'eu-2',
        jwk: { kty: 'EC', crv: 'P-256' },
      }),
    );
    await store.dispatch(
      managementApi.endpoints.importTenant.initiate({ cell: 'eu-2', kid: 'k-1', package: 'jwe' }),
    );
    await store.dispatch(
      managementApi.endpoints.releaseMovedTenant.initiate({ cell: 'eu-1', tenantId: 'acme-dev' }),
    );

    expect(captured.map((request) => `${request.method} ${request.url}`)).toEqual([
      'POST https://api.test/cells/eu-2/api/tenant-imports/transfer-keys',
      'POST https://api.test/cells/eu-1/api/tenants/acme-dev/export',
      'POST https://api.test/cells/eu-2/api/tenant-imports',
      'POST https://api.test/cells/eu-1/api/tenants/acme-dev/release-moved',
    ]);
    expect(captured[0]?.body).toBe('');
    expect(JSON.parse(captured[1]?.body ?? '{}')).toEqual({
      jwk: { kty: 'EC', crv: 'P-256' },
      targetCell: 'eu-2',
    });
    expect(JSON.parse(captured[2]?.body ?? '{}')).toEqual({ kid: 'k-1', package: 'jwe' });
    expect(captured[3]?.body).toBe('');
  });

  it('importTenant refreshes the tenant it imported', async () => {
    const fetches = routeFetch((method, path) => {
      if (method === 'GET') {
        return jsonResponse({ tenantId: 'acme-dev' });
      }
      return path.endsWith('/api/tenant-imports')
        ? jsonResponse({ tenantId: 'acme-dev', sourceCell: 'eu-1' })
        : new Response(null, { status: 204 });
    });
    const store = createStore();
    await store.dispatch(
      managementApi.endpoints.getTenant.initiate({ tenantId: 'acme-dev', cell: undefined }),
    );
    await store.dispatch(
      managementApi.endpoints.getTenant.initiate({ tenantId: 'other', cell: undefined }),
    );

    await store.dispatch(
      managementApi.endpoints.importTenant.initiate({ cell: 'eu-2', kid: 'k-1', package: 'jwe' }),
    );
    await settle(store);

    expect(fetches.count('GET', '/api/tenants/acme-dev')).toBe(2);
    expect(fetches.count('GET', '/api/tenants/other')).toBe(1);
  });

  it('listTenants asks the control plane alone when no cell is named', async () => {
    const fetches = routeFetch(() => jsonResponse({ items: [tenant('a')] }));
    const store = createStore();

    const result = await store.dispatch(
      managementApi.endpoints.listTenants.initiate({ teamId: 'acme' }),
    );

    expect(fetches.calls).toEqual(['GET /api/teams/acme/tenants']);
    expect(pagedTenants(result.data)).toEqual({ items: [tenant('a')], unavailableCells: [] });
  });

  it('listTenants refuses a surface its BFF does not serve without calling a cell', async () => {
    const openIdRuntime: OpenIdRuntimeConfig = {
      kind: 'openid',
      apiBaseUrl: 'https://api.test/',
      issuer: 'https://issuer.test/',
      authorizationEndpoint: 'https://issuer.test/authorize',
      tokenEndpoint: 'https://issuer.test/token',
      scopes: [],
      features: {},
    };
    const fetches = routeFetch(() => jsonResponse({ items: [tenant('a')] }));
    const store = createAppStore({ config: { runtime: openIdRuntime } });

    const result = await store.dispatch(
      managementApi.endpoints.listTenants.initiate({ teamId: 'acme' }),
    );

    expect(fetches.calls).toEqual([]);
    expect(result.error).toMatchObject({ status: 'CUSTOM_ERROR' });
  });

  it('importTenant leaves the tenant it failed to import cached', async () => {
    const fetches = routeFetch((method, path) =>
      method === 'GET' && path === '/api/tenants/acme-dev'
        ? jsonResponse({ tenantId: 'acme-dev' })
        : new Response(null, { status: 500 }),
    );
    const store = createStore();
    await store.dispatch(
      managementApi.endpoints.getTenant.initiate({ tenantId: 'acme-dev', cell: undefined }),
    );

    const result = await store.dispatch(
      managementApi.endpoints.importTenant.initiate({ cell: 'eu-2', kid: 'k-1', package: 'jwe' }),
    );
    await settle(store);

    expect(result).toHaveProperty('error');
    expect(fetches.count('GET', '/api/tenants/acme-dev')).toBe(1);
  });

  it('listTenants merges the tenants of every cell, each tagged with its cell', async () => {
    const fetches = routeFetch((_method, path) =>
      path.startsWith('/cells/eu-1/')
        ? jsonResponse({ items: [tenant('a'), tenant('b')] })
        : jsonResponse({ items: [tenant('c')] }),
    );
    const store = createCellStore();

    const result = await store.dispatch(
      managementApi.endpoints.listTenants.initiate({ teamId: 'acme' }),
    );

    expect([...fetches.calls].sort()).toEqual([
      'GET /cells/eu-1/api/teams/acme/tenants',
      'GET /cells/us-1/api/teams/acme/tenants',
    ]);
    expect(pagedTenants(result.data)).toEqual({
      items: [
        { ...tenant('a'), cell: 'eu-1' },
        { ...tenant('b'), cell: 'eu-1' },
        { ...tenant('c'), cell: 'us-1' },
      ],
      unavailableCells: [],
    });
  });

  it('listTenants marks a failing cell unavailable and keeps the other cells', async () => {
    routeFetch((_method, path) =>
      path.startsWith('/cells/eu-1/')
        ? jsonResponse({ title: 'Unavailable' }, 503)
        : jsonResponse({ items: [tenant('c')] }),
    );
    const store = createCellStore();

    const result = await store.dispatch(
      managementApi.endpoints.listTenants.initiate({ teamId: 'acme' }),
    );

    expect(result.error).toBeUndefined();
    expect(pagedTenants(result.data)).toEqual({
      items: [{ ...tenant('c'), cell: 'us-1' }],
      unavailableCells: ['eu-1'],
    });
  });

  it('listCallerTenants merges the caller tenants of every cell and reports a failing cell', async () => {
    const fetches = routeFetch((_method, path) =>
      path.startsWith('/cells/us-1/')
        ? jsonResponse({ title: 'Bad gateway' }, 502)
        : jsonResponse({ items: [tenant('a')] }),
    );
    const store = createCellStore();

    const result = await store.dispatch(managementApi.endpoints.listCallerTenants.initiate());

    expect([...fetches.calls].sort()).toEqual([
      'GET /cells/eu-1/api/me/tenants',
      'GET /cells/us-1/api/me/tenants',
    ]);
    expect(result.data).toEqual({
      items: [{ ...tenant('a'), cell: 'eu-1' }],
      unavailableCells: ['us-1'],
    });
  });

  it('listTenants fails when the control plane fails and no cell is named', async () => {
    routeFetch(() => jsonResponse({ title: 'Unavailable' }, 503));
    const store = createStore();

    const result = await store.dispatch(
      managementApi.endpoints.listTenants.initiate({ teamId: 'acme' }),
    );

    expect(result.data).toBeUndefined();
    expect(result.error).toMatchObject({ status: 503 });
  });
});

describe('managementApi team root key requests', () => {
  const teamRootKey = {
    region: 'eu west',
    provider: 'aws-kms',
    keyReference: 'arn:aws:kms:eu-west-1:111122223333:key/abc',
    accessIdentity: 'arn:aws:iam::111122223333:role/ncode-identity',
  };
  const rootKeys = {
    rootKeys: [teamRootKey],
    rootKeyProviders: ['aws-kms'],
    rootKeyAccessBinding: 'team:acme',
  };
  const removing = {
    provider: 'aws-kms',
    keyReference: teamRootKey.keyReference,
    accessIdentity: teamRootKey.accessIdentity,
    state: 'removing',
    createdWhen: '2026-10-01T09:00:00Z',
    stateChangedWhen: '2026-10-01T09:00:00Z',
    validatedWhen: null,
    failedWhen: null,
    failure: null,
  };
  const listPath = 'https://api.test/api/teams/acme/encryption-keys/root-keys';
  const regionPath = `${listPath}/eu%20west`;

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  /** Primes the root keys of two teams, runs the mutation and returns the requests it caused. */
  async function runMutation(
    response: Response,
    dispatch: (store: ReturnType<typeof createStore>) => PromiseLike<unknown>,
  ) {
    const captured = mockFetch(response);
    const store = createStore();
    const subscriptions = [
      store.dispatch(managementApi.endpoints.listTeamRootKeys.initiate({ teamId: 'acme' })),
      store.dispatch(managementApi.endpoints.listTeamRootKeys.initiate({ teamId: 'other' })),
    ];
    await Promise.all(subscriptions);
    const before = captured.length;
    await dispatch(store);
    await settle(store);
    for (const subscription of subscriptions) subscription.unsubscribe();
    return { mutation: captured[before], refetched: captured.slice(before + 1) };
  }

  it('listTeamRootKeys GETs the root keys of the team', async () => {
    const captured = mockFetch(jsonResponse(rootKeys));
    const store = createStore();

    const result = await store.dispatch(
      managementApi.endpoints.listTeamRootKeys.initiate({ teamId: 'acme' }),
    );

    expect(captured[0]?.url).toBe(listPath);
    expect(captured[0]?.method).toBe('GET');
    expect(result).toMatchObject({ data: rootKeys });
  });

  it('setTeamRootKey PUTs the root key of the region and refetches only the team root keys', async () => {
    const body = {
      provider: 'aws-kms',
      keyReference: teamRootKey.keyReference,
      accessIdentity: teamRootKey.accessIdentity,
    };

    const { mutation, refetched } = await runMutation(jsonResponse(teamRootKey), (store) =>
      store.dispatch(
        managementApi.endpoints.setTeamRootKey.initiate({
          teamId: 'acme',
          region: 'eu west',
          body,
        }),
      ),
    );

    expect(mutation?.url).toBe(regionPath);
    expect(mutation?.method).toBe('PUT');
    expect(JSON.parse(mutation?.body ?? '{}')).toEqual(body);
    expect(refetched.map((request) => request.url)).toEqual([listPath]);
  });

  it('removeTeamRootKey DELETEs the root key of the region and resolves null on 204', async () => {
    const captured = mockFetch(new Response(null, { status: 204 }));
    const store = createStore();

    const result = await store.dispatch(
      managementApi.endpoints.removeTeamRootKey.initiate({ teamId: 'acme', region: 'eu west' }),
    );

    expect(captured[0]?.url).toBe(regionPath);
    expect(captured[0]?.method).toBe('DELETE');
    expect(result).toMatchObject({ data: null });
  });

  it('removeTeamRootKey resolves the removing root key on 202 and refetches only the team root keys', async () => {
    const { mutation, refetched } = await runMutation(jsonResponse(removing, 202), (store) =>
      store.dispatch(
        managementApi.endpoints.removeTeamRootKey.initiate({ teamId: 'acme', region: 'eu west' }),
      ),
    );

    expect(mutation?.method).toBe('DELETE');
    expect(refetched.map((request) => request.url)).toEqual([listPath]);
  });

  it('validateTeamRootKey POSTs the validation of the region and refetches only the team root keys', async () => {
    const { mutation, refetched } = await runMutation(
      jsonResponse({ ...removing, state: 'active' }),
      (store) =>
        store.dispatch(
          managementApi.endpoints.validateTeamRootKey.initiate({
            teamId: 'acme',
            region: 'eu west',
          }),
        ),
    );

    expect(mutation?.url).toBe(`${regionPath}/validate`);
    expect(mutation?.method).toBe('POST');
    expect(refetched.map((request) => request.url)).toEqual([listPath]);
  });
});

describe('managementApi owner-generic secrets and settings requests', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it.each(secretOwnerCases)(
    'listSecrets GETs the secrets of the $key owner',
    async ({ owner, path }) => {
      const captured = mockFetch(jsonResponse({ secrets: [] }));
      const store = createStore();

      await store.dispatch(
        managementApi.endpoints.listSecrets.initiate({ ...owner, cell: undefined }),
      );

      expect(captured[0]?.url).toBe(`https://api.test${path}/secrets`);
      expect(captured[0]?.method).toBe('GET');
    },
  );

  it.each(secretOwnerCases)(
    'getSecretPublicKey GETs the public key of a secret of the $key owner',
    async ({ owner, path }) => {
      const captured = mockFetch(jsonResponse({}));
      const store = createStore();

      await store.dispatch(
        managementApi.endpoints.getSecretPublicKey.initiate({
          owner,
          secretId: 's-1',
          cell: undefined,
        }),
      );

      expect(captured[0]?.url).toBe(`https://api.test${path}/secrets/s-1/public-key`);
      expect(captured[0]?.method).toBe('GET');
    },
  );

  it.each(ownerCases)(
    'getSettings GETs the settings of the $key owner',
    async ({ owner, path }) => {
      const captured = mockFetch(jsonResponse({}));
      const store = createStore();

      await store.dispatch(
        managementApi.endpoints.getSettings.initiate({ ...owner, cell: undefined }),
      );

      expect(captured[0]?.url).toBe(`https://api.test${path}/settings`);
      expect(captured[0]?.method).toBe('GET');
    },
  );

  it.each(ownerCases)(
    'getEffectiveSettings GETs the effective settings of the $key owner',
    async ({ owner, path }) => {
      const captured = mockFetch(jsonResponse({}));
      const store = createStore();

      await store.dispatch(
        managementApi.endpoints.getEffectiveSettings.initiate({ ...owner, cell: undefined }),
      );

      expect(captured[0]?.url).toBe(`https://api.test${path}/effective-settings`);
      expect(captured[0]?.method).toBe('GET');
    },
  );

  it.each(ownerCases)(
    'getSettingLimits GETs the setting limits of the $key owner',
    async ({ owner, path }) => {
      const captured = mockFetch(jsonResponse({ names: [], canChange: true }));
      const store = createStore();

      await store.dispatch(
        managementApi.endpoints.getSettingLimits.initiate({ ...owner, cell: undefined }),
      );

      expect(captured[0]?.url).toBe(`https://api.test${path}/settings/limits`);
      expect(captured[0]?.method).toBe('GET');
    },
  );

  it.each(secretOwnerCases)(
    'createSecret POSTs the request body under the $key owner',
    async ({ owner, path }) => {
      const captured = mockFetch(jsonResponse({ secretId: 's-1' }, 201));
      const store = createStore();

      await store.dispatch(
        managementApi.endpoints.createSecret.initiate({
          owner,
          body: secretRequest,
          cell: undefined,
        }),
      );

      expect(captured[0]?.url).toBe(`https://api.test${path}/secrets`);
      expect(captured[0]?.method).toBe('POST');
      expect(JSON.parse(captured[0]?.body ?? '{}')).toEqual(secretRequest);
    },
  );

  it.each(secretOwnerCases)(
    'deleteSecret DELETEs the secret by id under the $key owner',
    async ({ owner, path }) => {
      const captured = mockFetch(new Response(null, { status: 204 }));
      const store = createStore();

      await store.dispatch(
        managementApi.endpoints.deleteSecret.initiate({
          owner,
          secretId: 's-1',
          cell: undefined,
        }),
      );

      expect(captured[0]?.url).toBe(`https://api.test${path}/secrets/s-1`);
      expect(captured[0]?.method).toBe('DELETE');
    },
  );

  it.each(ownerCases)(
    'updateSettings PATCHes a JSON Patch diff with If-Match under the $key owner',
    async ({ owner, path }) => {
      const captured = mockFetch(new Response(null, { status: 204 }));
      const store = createStore();

      const issued = await store
        .dispatch(
          managementApi.endpoints.updateSettings.initiate({
            owner,
            concurrencyToken: 'etag',
            original: { keep: 1, drop: 2 },
            desired: { keep: 1 },
            cell: undefined,
          }),
        )
        .unwrap();

      expect(captured[0]?.url).toBe(`https://api.test${path}/settings`);
      expect(captured[0]?.method).toBe('PATCH');
      expect(captured[0]?.ifMatch).toBe('etag');
      expect(JSON.parse(captured[0]?.body ?? '[]')).toEqual([{ op: 'remove', path: '/drop' }]);
      // A 204 issues no secret, so the mutation resolves to null rather than an empty body.
      expect(issued).toBeNull();
    },
  );

  it('updateSettings resolves to the secret a client settings change issues, revealed once', async () => {
    // A change to the client's authentication method can issue the shared secret the method needs, which the server
    // answers with 200 and the created secret (ADR-1391).
    const created: CreatedSecretResource = {
      secretId: 's-issued',
      keyId: 's-issued',
      concurrencyToken: 'sc1',
      use: null,
      algorithm: null,
      createdWhen: '2026-01-01T00:00:00Z',
      activatesWhen: '2026-01-01T00:00:00Z',
      retiresWhen: '2027-01-01T00:00:00Z',
      expiresWhen: '2027-01-01T00:00:00Z',
      secretType: 'symmetric',
      keySizeBits: 256,
      isVerifier: false,
      managedBy: null,
      remoteKeyProvider: null,
      secretMaterial: 'issued-once',
    };
    mockFetch(jsonResponse(created));
    const store = createStore();

    const issued = await store
      .dispatch(
        managementApi.endpoints.updateSettings.initiate({
          owner: { kind: 'client', tenantId: 'acme', clientId: 'web' },
          concurrencyToken: 'etag',
          original: {},
          desired: { token_endpoint_auth_method: 'client_secret_jwt' },
          cell: undefined,
        }),
      )
      .unwrap();

    expect(issued).toEqual(created);
  });

  it('rotateSecret POSTs the replacement expiry to the secret rotate path of a client', async () => {
    const captured = mockFetch(jsonResponse({ secretId: 's-2' }, 201));
    const store = createStore();
    const body = { expiresWhen: '2027-01-01T00:00:00Z' };

    await store.dispatch(
      managementApi.endpoints.rotateSecret.initiate({
        owner: { kind: 'client', tenantId: 'acme', clientId: 'web' },
        secretId: 's-1',
        body,
        cell: undefined,
      }),
    );

    expect(captured[0]?.url).toBe(
      'https://api.test/api/tenants/acme/clients/web/secrets/s-1/rotate',
    );
    expect(captured[0]?.method).toBe('POST');
    expect(JSON.parse(captured[0]?.body ?? '{}')).toEqual(body);
  });

  it("createRemoteSecret POSTs the request body to the tenant's remote secrets path", async () => {
    const captured = mockFetch(jsonResponse({ secretId: 's-3' }, 201));
    const store = createStore();
    const body = {
      remoteKeyProvider: 'aws-kms',
      secretType: 'ecc',
      keySizeBits: 256,
      curve: 'P-256',
      algorithm: 'ES256',
      expiresWhen: '2027-01-01T00:00:00Z',
    };

    await store.dispatch(
      managementApi.endpoints.createRemoteSecret.initiate({
        tenantId: 'acme',
        body,
        cell: undefined,
      }),
    );

    expect(captured[0]?.url).toBe('https://api.test/api/tenants/acme/secrets/remote');
    expect(captured[0]?.method).toBe('POST');
    expect(JSON.parse(captured[0]?.body ?? '{}')).toEqual(body);
  });

  it("importRemoteSecret POSTs the request body to the tenant's remote import path", async () => {
    const captured = mockFetch(jsonResponse({ secretId: 's-4' }, 201));
    const store = createStore();
    const body = {
      remoteKeyProvider: 'aws-kms',
      remoteKeyId: 'key-1',
      secretType: 'rsa',
      keySizeBits: 2048,
      algorithm: 'RS256',
      expiresWhen: '2027-01-01T00:00:00Z',
    };

    await store.dispatch(
      managementApi.endpoints.importRemoteSecret.initiate({
        tenantId: 'acme',
        body,
        cell: undefined,
      }),
    );

    expect(captured[0]?.url).toBe('https://api.test/api/tenants/acme/secrets/remote/import');
    expect(captured[0]?.method).toBe('POST');
    expect(JSON.parse(captured[0]?.body ?? '{}')).toEqual(body);
  });
});

/**
 * For every owner of {@link ownerCases}: its secrets, its settings and its effective settings. The same
 * ids recur across owners and scopes, so every mutation also proves that it leaves the other owners' caches alone.
 */
const ownerCachedQueries: readonly CachedQuery[] = [
  ...secretOwnerCases.map(({ key, owner, path }): CachedQuery => ({
    label: `secrets:${key}`,
    path: `${path}/secrets`,
    body: { secrets: [{ secretId: `${key}-secret` }] },
    subscribe: (s) =>
      s.dispatch(managementApi.endpoints.listSecrets.initiate({ ...owner, cell: undefined })),
  })),
  ...ownerCases.flatMap(({ key, owner, path }): CachedQuery[] => [
    {
      label: `settings:${key}`,
      path: `${path}/settings`,
      body: {},
      subscribe: (s) =>
        s.dispatch(managementApi.endpoints.getSettings.initiate({ ...owner, cell: undefined })),
    },
    {
      label: `effective:${key}`,
      path: `${path}/effective-settings`,
      body: {},
      subscribe: (s) =>
        s.dispatch(
          managementApi.endpoints.getEffectiveSettings.initiate({ ...owner, cell: undefined }),
        ),
    },
  ]),
];

/** Subscribes every owner cache entry, runs the mutation, and returns the labels that were fetched again. */
async function refetchedByOwnerMutation(
  mutate: (store: AppStore) => PromiseLike<unknown>,
): Promise<string[]> {
  const fetches = routeFetch((method, path) =>
    method === 'GET'
      ? jsonResponse(ownerCachedQueries.find((q) => q.path === path)?.body ?? {})
      : new Response(null, { status: 204 }),
  );
  const store = createStore();
  await Promise.all(ownerCachedQueries.map((q) => q.subscribe(store)));
  expect(ownerCachedQueries.filter((q) => fetches.count('GET', q.path) !== 1)).toEqual([]);

  await mutate(store);
  await settle(store);

  expect(fetches.calls.filter((call) => !call.startsWith('GET '))).toHaveLength(1);
  return ownerCachedQueries.filter((q) => fetches.count('GET', q.path) === 2).map((q) => q.label);
}

describe('managementApi owner-generic cache invalidation', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it.each(secretOwnerCases)(
    'createSecret for the $key owner refetches only its secrets',
    async ({ key, owner }) => {
      const refetched = await refetchedByOwnerMutation((s) =>
        s.dispatch(
          managementApi.endpoints.createSecret.initiate({
            owner,
            body: secretRequest,
            cell: undefined,
          }),
        ),
      );

      expect(refetched).toEqual([`secrets:${key}`]);
    },
  );

  it.each(secretOwnerCases)(
    'deleteSecret for the $key owner refetches only its secrets',
    async ({ key, owner }) => {
      const refetched = await refetchedByOwnerMutation((s) =>
        s.dispatch(
          managementApi.endpoints.deleteSecret.initiate({
            owner,
            secretId: `${key}-secret`,
            cell: undefined,
          }),
        ),
      );

      expect(refetched).toEqual([`secrets:${key}`]);
    },
  );

  it.each(ownerCases)(
    'updateSettings for the $key owner refetches only its settings and secrets',
    async ({ key, owner }) => {
      const refetched = await refetchedByOwnerMutation((s) =>
        s.dispatch(
          managementApi.endpoints.updateSettings.initiate({
            owner,
            concurrencyToken: 'etag',
            original: { keep: 1, drop: 2 },
            desired: { keep: 1 },
            cell: undefined,
          }),
        ),
      );

      // A client's settings change can issue or convert its secrets, so the owner's secrets are refetched too; the
      // platform and a team hold no secrets.
      const holdsSecrets = secretOwnerCases.some((c) => c.key === key);
      expect([...refetched].sort()).toEqual(
        [`effective:${key}`, `settings:${key}`, ...(holdsSecrets ? [`secrets:${key}`] : [])].sort(),
      );
    },
  );

  it('rotateSecret for the tenant client refetches only its secrets', async () => {
    const refetched = await refetchedByOwnerMutation((s) =>
      s.dispatch(
        managementApi.endpoints.rotateSecret.initiate({
          owner: { kind: 'client', tenantId: 'acme', clientId: 'web' },
          secretId: 'tenant-client-secret',
          body: { expiresWhen: '2027-01-01T00:00:00Z' },
          cell: undefined,
        }),
      ),
    );

    expect(refetched).toEqual(['secrets:tenant-client']);
  });

  it("createRemoteSecret refetches only the tenant's secrets", async () => {
    const refetched = await refetchedByOwnerMutation((s) =>
      s.dispatch(
        managementApi.endpoints.createRemoteSecret.initiate({
          tenantId: 'acme',
          body: {
            remoteKeyProvider: 'aws-kms',
            secretType: 'ecc',
            keySizeBits: 256,
            curve: 'P-256',
            algorithm: 'ES256',
            expiresWhen: '2027-01-01T00:00:00Z',
          },
          cell: undefined,
        }),
      ),
    );

    expect(refetched).toEqual(['secrets:tenant']);
  });

  it("importRemoteSecret refetches only the tenant's secrets", async () => {
    const refetched = await refetchedByOwnerMutation((s) =>
      s.dispatch(
        managementApi.endpoints.importRemoteSecret.initiate({
          tenantId: 'acme',
          body: {
            remoteKeyProvider: 'aws-kms',
            remoteKeyId: 'key-1',
            secretType: 'rsa',
            keySizeBits: 2048,
            algorithm: 'RS256',
            expiresWhen: '2027-01-01T00:00:00Z',
          },
          cell: undefined,
        }),
      ),
    );

    expect(refetched).toEqual(['secrets:tenant']);
  });

  it('listSecrets still provides its owner tag when the request fails', async () => {
    routeFetch(() => jsonResponse({ title: 'Service Unavailable' }, 503));
    const store = createStore();

    const result = await store.dispatch(
      managementApi.endpoints.listSecrets.initiate({
        kind: 'tenant',
        tenantId: 'acme',
        cell: undefined,
      }),
    );

    expect(result.isError).toBe(true);
    expect(invalidatedBy(store, [{ type: 'Secret', id: 'tenant:acme:tenant:acme' }])).toEqual([
      'listSecrets',
    ]);
  });
});

describe('managementApi role assignment requests', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  const teamAdmins: RoleList = { role: 'admins', owner: { kind: 'team', teamId: 't/1' } };
  const tenantOwners: RoleList = { role: 'owners', owner: { kind: 'tenant', tenantId: 'acme' } };

  it('listRoleAssignments GETs the administrators of a team', async () => {
    const captured = mockFetch(jsonResponse({ items: [], canChange: true }));
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.listRoleAssignments.initiate({ ...teamAdmins, cell: undefined }),
    );

    expect(captured[0]?.url).toBe('https://api.test/api/teams/t%2F1/admins');
    expect(captured[0]?.method).toBe('GET');
  });

  it('addRoleAssignment POSTs the principal id to the owners of a tenant', async () => {
    const captured = mockFetch(
      jsonResponse({ assignmentId: 'a-1', principalId: 'p-1', roleName: 'Owner' }, 201),
    );
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.addRoleAssignment.initiate({
        list: tenantOwners,
        holder: { principalId: 'p-1' },
        cell: undefined,
      }),
    );

    expect(captured[0]?.url).toBe('https://api.test/api/tenants/acme/owners');
    expect(captured[0]?.method).toBe('POST');
    expect(JSON.parse(captured[0]?.body ?? '{}')).toEqual({ principalId: 'p-1' });
  });

  it('addRoleAssignment posts the holder a grant names', async () => {
    const captured = mockFetch(
      jsonResponse({ assignmentId: 'a-1', principalId: 'p-1', roleName: 'Owner' }, 201),
    );
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.addRoleAssignment.initiate({
        list: tenantOwners,
        holder: { userName: 'alice' },
        cell: undefined,
      }),
    );

    expect(captured[0]?.url).toBe('https://api.test/api/tenants/acme/owners');
    expect(captured[0]?.body).toBe('{"userName":"alice"}');
  });

  it('removeRoleAssignment DELETEs the principal by encoded id', async () => {
    const captured = mockFetch(new Response(null, { status: 204 }));
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.removeRoleAssignment.initiate({
        list: teamAdmins,
        principalId: 'a b/c',
        cell: undefined,
      }),
    );

    expect(captured[0]?.url).toBe('https://api.test/api/teams/t%2F1/admins/a%20b%2Fc');
    expect(captured[0]?.method).toBe('DELETE');
  });

  it('addRoleAssignment refetches every role list, whose canChange a role change can change', async () => {
    const fetches = routeFetch((method) =>
      method === 'GET'
        ? jsonResponse({ items: [], canChange: true })
        : jsonResponse({ assignmentId: 'a-1', principalId: 'p-1', roleName: 'Owner' }, 201),
    );
    const store = createStore();
    await store.dispatch(
      managementApi.endpoints.listRoleAssignments.initiate({ ...tenantOwners, cell: undefined }),
    );
    await store.dispatch(
      managementApi.endpoints.listRoleAssignments.initiate({ ...teamAdmins, cell: undefined }),
    );
    expect(fetches.count('GET', '/api/tenants/acme/owners')).toBe(1);
    expect(fetches.count('GET', '/api/teams/t%2F1/admins')).toBe(1);

    await store.dispatch(
      managementApi.endpoints.addRoleAssignment.initiate({
        list: tenantOwners,
        holder: { principalId: 'p-1' },
        cell: undefined,
      }),
    );
    await settle(store);

    expect(fetches.count('GET', '/api/tenants/acme/owners')).toBe(2);
    expect(fetches.count('GET', '/api/teams/t%2F1/admins')).toBe(2);
  });

  it('removeRoleAssignment refetches every role list', async () => {
    const fetches = routeFetch((method) =>
      method === 'GET'
        ? jsonResponse({ items: [], canChange: true })
        : new Response(null, { status: 204 }),
    );
    const store = createStore();
    await store.dispatch(
      managementApi.endpoints.listRoleAssignments.initiate({ ...tenantOwners, cell: undefined }),
    );
    await store.dispatch(
      managementApi.endpoints.listRoleAssignments.initiate({ ...teamAdmins, cell: undefined }),
    );
    expect(fetches.count('GET', '/api/tenants/acme/owners')).toBe(1);
    expect(fetches.count('GET', '/api/teams/t%2F1/admins')).toBe(1);

    await store.dispatch(
      managementApi.endpoints.removeRoleAssignment.initiate({
        list: tenantOwners,
        principalId: 'p-1',
        cell: undefined,
      }),
    );
    await settle(store);

    expect(fetches.count('GET', '/api/tenants/acme/owners')).toBe(2);
    expect(fetches.count('GET', '/api/teams/t%2F1/admins')).toBe(2);
  });
});

describe('managementApi local accounts', () => {
  const account = {
    tenantId: 'acme',
    localAccountId: 'la/1',
    principalId: null,
    userName: 'ada',
    displayName: 'Ada',
    email: null,
    emailVerified: false,
    isEnabled: true,
    concurrencyToken: 'c1',
  };
  const itemPath = '/api/tenants/acme/local-accounts/la%2F1';
  const newAccount = { userName: 'ada', password: 'pw' };
  const fields = { displayName: 'Ada', userName: 'ada', email: null, emailVerified: false };

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('listLocalAccounts GETs the platform collection', async () => {
    const captured = mockFetch(jsonResponse({ items: [] }));
    const store = createStore();

    await store.dispatch(managementApi.endpoints.listLocalAccounts.initiate({ kind: 'platform' }));

    expect(captured[0]?.url).toBe('https://api.test/api/platform/local-accounts');
    expect(captured[0]?.method).toBe('GET');
  });

  it('listLocalAccounts caches an error under its list tag alone', async () => {
    mockFetch(jsonResponse({ title: 'Server Error' }, 500));
    const store = createStore();

    const result = await store.dispatch(
      managementApi.endpoints.listLocalAccounts.initiate({ kind: 'platform' }),
    );

    expect(result.isError).toBe(true);
  });

  it("listLocalAccounts GETs a tenant's collection by encoded id", async () => {
    const captured = mockFetch(jsonResponse({ items: [] }));
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.listLocalAccounts.initiate({ kind: 'tenant', tenantId: 'a/b' }),
    );

    expect(captured[0]?.url).toBe('https://api.test/api/tenants/a%2Fb/local-accounts');
  });

  it("listLocalAccounts GETs a tenant's collection in its cell", async () => {
    const captured = mockFetch(jsonResponse({ items: [] }));
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.listLocalAccounts.initiate({
        kind: 'tenant',
        tenantId: 'acme',
        cell: 'eu-1',
      }),
    );

    expect(captured[0]?.url).toBe('https://api.test/cells/eu-1/api/tenants/acme/local-accounts');
  });

  it('createLocalAccount POSTs the body to the platform, tenant and cell collections', async () => {
    const captured = mockFetch(jsonResponse(account, 201));
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.createLocalAccount.initiate({
        owner: { kind: 'platform' },
        body: newAccount,
      }),
    );
    await store.dispatch(
      managementApi.endpoints.createLocalAccount.initiate({
        owner: { kind: 'tenant', tenantId: 'acme' },
        body: newAccount,
      }),
    );
    await store.dispatch(
      managementApi.endpoints.createLocalAccount.initiate({
        owner: { kind: 'tenant', tenantId: 'acme', cell: 'eu-1' },
        body: newAccount,
      }),
    );

    expect(captured.map((c) => c.method)).toEqual(['POST', 'POST', 'POST']);
    expect(captured.map((c) => c.url)).toEqual([
      'https://api.test/api/platform/local-accounts',
      'https://api.test/api/tenants/acme/local-accounts',
      'https://api.test/cells/eu-1/api/tenants/acme/local-accounts',
    ]);
    expect(JSON.parse(captured[0]?.body ?? '{}')).toEqual(newAccount);
    expect(captured[0]?.antiforgery).toBe('1');
  });

  it("createLocalAccount refetches its owner's list and no other", async () => {
    const fetches = routeFetch((method) =>
      method === 'GET' ? jsonResponse({ items: [] }) : jsonResponse(account, 201),
    );
    const store = createStore();
    await store.dispatch(managementApi.endpoints.listLocalAccounts.initiate({ kind: 'platform' }));
    await store.dispatch(
      managementApi.endpoints.listLocalAccounts.initiate({ kind: 'tenant', tenantId: 'acme' }),
    );
    await store.dispatch(
      managementApi.endpoints.listLocalAccounts.initiate({ kind: 'tenant', tenantId: 'other' }),
    );

    await store.dispatch(
      managementApi.endpoints.createLocalAccount.initiate({
        owner: { kind: 'tenant', tenantId: 'acme' },
        body: newAccount,
      }),
    );
    await settle(store);
    await store.dispatch(
      managementApi.endpoints.createLocalAccount.initiate({
        owner: { kind: 'platform' },
        body: newAccount,
      }),
    );
    await settle(store);

    expect(fetches.count('GET', '/api/tenants/acme/local-accounts')).toBe(2);
    expect(fetches.count('GET', '/api/tenants/other/local-accounts')).toBe(1);
    expect(fetches.count('GET', '/api/platform/local-accounts')).toBe(2);
  });

  it('updateLocalAccount PATCHes a replace document with If-Match, in the cell when given', async () => {
    const captured = mockFetch(new Response(null, { status: 204 }));
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.updateLocalAccount.initiate({
        tenantId: 'acme',
        localAccountId: 'la/1',
        concurrencyToken: 'c1',
        fields,
        cell: undefined,
      }),
    );
    await store.dispatch(
      managementApi.endpoints.updateLocalAccount.initiate({
        tenantId: 'acme',
        localAccountId: 'la/1',
        concurrencyToken: 'c2',
        fields,
        cell: 'eu-1',
      }),
    );

    expect(captured[0]?.method).toBe('PATCH');
    expect(captured[0]?.url).toBe(`https://api.test${itemPath}`);
    expect(captured[0]?.ifMatch).toBe('c1');
    expect(JSON.parse(captured[0]?.body ?? '[]')).toEqual([
      { op: 'replace', path: '/displayName', value: 'Ada' },
      { op: 'replace', path: '/userName', value: 'ada' },
      { op: 'replace', path: '/email', value: null },
      { op: 'replace', path: '/emailVerified', value: false },
    ]);
    expect(captured[1]?.url).toBe(`https://api.test/cells/eu-1${itemPath}`);
    expect(captured[1]?.ifMatch).toBe('c2');
  });

  it('deleteLocalAccount DELETEs the account, in the cell when given', async () => {
    const captured = mockFetch(new Response(null, { status: 204 }));
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.deleteLocalAccount.initiate({
        tenantId: 'acme',
        localAccountId: 'la/1',
        cell: undefined,
      }),
    );
    await store.dispatch(
      managementApi.endpoints.deleteLocalAccount.initiate({
        tenantId: 'acme',
        localAccountId: 'la/1',
        cell: 'eu-1',
      }),
    );

    expect(captured.map((c) => c.method)).toEqual(['DELETE', 'DELETE']);
    expect(captured.map((c) => c.url)).toEqual([
      `https://api.test${itemPath}`,
      `https://api.test/cells/eu-1${itemPath}`,
    ]);
  });

  it('enableLocalAccount and disableLocalAccount POST to their routes', async () => {
    const captured = mockFetch(new Response(null, { status: 204 }));
    const store = createStore();
    const args = { tenantId: 'acme', localAccountId: 'la/1', cell: 'eu-1' };

    await store.dispatch(managementApi.endpoints.enableLocalAccount.initiate(args));
    await store.dispatch(managementApi.endpoints.disableLocalAccount.initiate(args));

    expect(captured.map((c) => c.method)).toEqual(['POST', 'POST']);
    expect(captured.map((c) => c.url)).toEqual([
      `https://api.test/cells/eu-1${itemPath}/enable`,
      `https://api.test/cells/eu-1${itemPath}/disable`,
    ]);
  });

  it('resetLocalAccountPassword POSTs the password body, in the cell when given', async () => {
    const captured = mockFetch(new Response(null, { status: 204 }));
    const store = createStore();
    const body = { password: 'new-pw' };

    await store.dispatch(
      managementApi.endpoints.resetLocalAccountPassword.initiate({
        tenantId: 'acme',
        localAccountId: 'la/1',
        body,
        cell: undefined,
      }),
    );
    await store.dispatch(
      managementApi.endpoints.resetLocalAccountPassword.initiate({
        tenantId: 'acme',
        localAccountId: 'la/1',
        body,
        cell: 'eu-1',
      }),
    );

    expect(captured.map((c) => c.method)).toEqual(['POST', 'POST']);
    expect(captured[0]?.url).toBe(`https://api.test${itemPath}/password`);
    expect(captured[1]?.url).toBe(`https://api.test/cells/eu-1${itemPath}/password`);
    expect(JSON.parse(captured[0]?.body ?? '{}')).toEqual(body);
  });

  const accountArgs = { tenantId: 'acme', localAccountId: 'la/1', cell: undefined };
  const mutationActions: readonly (readonly [string, () => unknown])[] = [
    [
      'updateLocalAccount',
      () =>
        managementApi.endpoints.updateLocalAccount.initiate({
          ...accountArgs,
          concurrencyToken: 'c1',
          fields,
        }),
    ],
    ['deleteLocalAccount', () => managementApi.endpoints.deleteLocalAccount.initiate(accountArgs)],
    ['enableLocalAccount', () => managementApi.endpoints.enableLocalAccount.initiate(accountArgs)],
    [
      'disableLocalAccount',
      () => managementApi.endpoints.disableLocalAccount.initiate(accountArgs),
    ],
    [
      'resetLocalAccountPassword',
      () =>
        managementApi.endpoints.resetLocalAccountPassword.initiate({
          ...accountArgs,
          body: { password: 'pw' },
        }),
    ],
  ];

  it.each(mutationActions)(
    '%s refetches the list that holds the account',
    async (_name, action) => {
      const fetches = routeFetch((method, path) => {
        if (method !== 'GET') {
          return new Response(null, { status: 204 });
        }
        return jsonResponse({ items: path.startsWith('/api/tenants/') ? [account] : [] });
      });
      const store = createStore();
      await store.dispatch(
        managementApi.endpoints.listLocalAccounts.initiate({ kind: 'tenant', tenantId: 'acme' }),
      );
      await store.dispatch(
        managementApi.endpoints.listLocalAccounts.initiate({ kind: 'platform' }),
      );

      await (store.dispatch as (a: unknown) => Promise<unknown>)(action());
      await settle(store);

      expect(fetches.count('GET', '/api/tenants/acme/local-accounts')).toBe(2);
      expect(fetches.count('GET', '/api/platform/local-accounts')).toBe(1);
    },
  );
});

describe('local account profile', () => {
  const path = '/api/tenants/acme/local-accounts/la%2F1';

  it('getLocalAccountClaims and getLocalAccountMetadata GET their routes, in the cell when given', async () => {
    const captured = mockFetch(new Response('{}', { status: 200 }));
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.getLocalAccountClaims.initiate({
        tenantId: 'acme',
        localAccountId: 'la/1',
        cell: undefined,
      }),
    );
    await store.dispatch(
      managementApi.endpoints.getLocalAccountMetadata.initiate({
        tenantId: 'acme',
        localAccountId: 'la/1',
        cell: 'eu-1',
      }),
    );

    expect(captured[0]?.method).toBe('GET');
    expect(captured[0]?.url).toBe(`https://api.test${path}/claims`);
    expect(captured[1]?.method).toBe('GET');
    expect(captured[1]?.url).toBe(`https://api.test/cells/eu-1${path}/metadata`);
  });

  it('setLocalAccountClaims PUTs the claims with If-Match', async () => {
    const captured = mockFetch(new Response('{}', { status: 200 }));
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.setLocalAccountClaims.initiate({
        tenantId: 'acme',
        localAccountId: 'la/1',
        concurrencyToken: 'c1',
        body: { claims: [{ type: 'locale', value: 'en' }] },
        cell: 'eu-1',
      }),
    );

    expect(captured[0]?.method).toBe('PUT');
    expect(captured[0]?.url).toBe(`https://api.test/cells/eu-1${path}/claims`);
    expect(captured[0]?.ifMatch).toBe('c1');
    expect(JSON.parse(captured[0]?.body ?? '{}')).toEqual({
      claims: [{ type: 'locale', value: 'en' }],
    });
  });

  it('setLocalAccountMetadata PATCHes the metadata bags with If-Match', async () => {
    const captured = mockFetch(new Response('{}', { status: 200 }));
    const store = createStore();

    await store.dispatch(
      managementApi.endpoints.setLocalAccountMetadata.initiate({
        tenantId: 'acme',
        localAccountId: 'la/1',
        concurrencyToken: 'p1',
        body: { profileMetadata: { theme: 'dark' } },
        cell: undefined,
      }),
    );

    expect(captured[0]?.method).toBe('PATCH');
    expect(captured[0]?.url).toBe(`https://api.test${path}/metadata`);
    expect(captured[0]?.ifMatch).toBe('p1');
    expect(JSON.parse(captured[0]?.body ?? '{}')).toEqual({ profileMetadata: { theme: 'dark' } });
  });
});

describe('paged collections', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  const client = (clientId: string) => ({ clientId, tenantId: 'acme' });

  it('reads the first page without a cursor, the next with the token and ends without one', async () => {
    const calls: string[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const url = (input as Request).url;
        calls.push(url);
        return url.includes('cursor=t1')
          ? jsonResponse({ items: [client('c2')] })
          : jsonResponse({ items: [client('c1')], continuationToken: 't1' });
      }),
    );
    const store = createStore();
    const arg = { tenantId: 'acme', cell: undefined };

    const first = await store.dispatch(managementApi.endpoints.listClients.initiate(arg));
    const next = await store.dispatch(
      managementApi.endpoints.listClients.initiate(arg, { direction: 'forward' }),
    );

    expect(calls).toEqual([
      'https://api.test/api/tenants/acme/clients',
      'https://api.test/api/tenants/acme/clients?cursor=t1',
    ]);
    expect(first.hasNextPage).toBe(true);
    expect(next.hasNextPage).toBe(false);
    expect(pagedItems(next.data).map((c) => c.clientId)).toEqual(['c1', 'c2']);
  });

  it('flattens the items of every page', () => {
    expect(pagedItems({ pages: [{ items: [1, 2] }, { items: [3] }] })).toEqual([1, 2, 3]);
    expect(pagedItems(undefined)).toEqual([]);
  });
});

describe('tenant pages', () => {
  const tenant = (tenantId: string) => ({ tenantId, teamId: 'acme' });
  const cellStore = () =>
    createAppStore({ config: { runtime: { ...runtime, cells: ['eu-1', 'us-1'] } } });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('asks every cell for its first page and only the cells with a cursor for the next', async () => {
    const calls: string[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const url = (input as Request).url;
        calls.push(url);
        if (url.includes('/cells/eu-1/')) {
          return url.includes('cursor=t1')
            ? jsonResponse({ items: [tenant('b')] })
            : jsonResponse({ items: [tenant('a')], continuationToken: 't1' });
        }
        return jsonResponse({ items: [tenant('c')] });
      }),
    );
    const store = cellStore();
    const arg = { teamId: 'acme' };

    const first = await store.dispatch(managementApi.endpoints.listTenants.initiate(arg));
    const next = await store.dispatch(
      managementApi.endpoints.listTenants.initiate(arg, { direction: 'forward' }),
    );

    expect([...calls].sort()).toEqual([
      'https://api.test/cells/eu-1/api/teams/acme/tenants',
      'https://api.test/cells/eu-1/api/teams/acme/tenants?cursor=t1',
      'https://api.test/cells/us-1/api/teams/acme/tenants',
    ]);
    expect(first.hasNextPage).toBe(true);
    expect(next.hasNextPage).toBe(false);
    expect(pagedTenants(next.data).items.map((t) => t.tenantId)).toEqual(['a', 'c', 'b']);
  });

  it('pages the control plane alone by its continuation token', async () => {
    const calls: string[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const url = (input as Request).url;
        calls.push(url);
        return url.includes('cursor=t1')
          ? jsonResponse({ items: [tenant('b')] })
          : jsonResponse({ items: [tenant('a')], continuationToken: 't1' });
      }),
    );
    const store = createAppStore({ config: { runtime } });
    const arg = { teamId: 'acme' };

    await store.dispatch(managementApi.endpoints.listTenants.initiate(arg));
    const next = await store.dispatch(
      managementApi.endpoints.listTenants.initiate(arg, { direction: 'forward' }),
    );

    expect(calls).toEqual([
      'https://api.test/api/teams/acme/tenants',
      'https://api.test/api/teams/acme/tenants?cursor=t1',
    ]);
    expect(next.hasNextPage).toBe(false);
    expect(pagedTenants(next.data).items.map((t) => t.tenantId)).toEqual(['a', 'b']);
  });

  it('retries a cell whose next page failed with the cursor it was asked with', async () => {
    const calls: string[] = [];
    let failed = false;
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const url = (input as Request).url;
        calls.push(url);
        if (!url.includes('/cells/eu-1/')) {
          return jsonResponse({ items: [tenant('c')] });
        }
        if (!url.includes('cursor=t1')) {
          return jsonResponse({ items: [tenant('a')], continuationToken: 't1' });
        }
        if (!failed) {
          failed = true;
          return jsonResponse({ title: 'down' }, 503);
        }
        return jsonResponse({ items: [tenant('b')] });
      }),
    );
    const store = cellStore();
    const arg = { teamId: 'acme' };

    await store.dispatch(managementApi.endpoints.listTenants.initiate(arg));
    const second = await store.dispatch(
      managementApi.endpoints.listTenants.initiate(arg, { direction: 'forward' }),
    );
    const third = await store.dispatch(
      managementApi.endpoints.listTenants.initiate(arg, { direction: 'forward' }),
    );

    expect(pagedTenants(second.data).unavailableCells).toEqual(['eu-1']);
    expect(second.hasNextPage).toBe(true);
    expect(calls.filter((url) => url.includes('cursor=t1'))).toEqual([
      'https://api.test/cells/eu-1/api/teams/acme/tenants?cursor=t1',
      'https://api.test/cells/eu-1/api/teams/acme/tenants?cursor=t1',
    ]);
    expect(pagedTenants(third.data).items.map((t) => t.tenantId)).toEqual(['a', 'c', 'b']);
    expect(pagedTenants(third.data).unavailableCells).toEqual([]);
    expect(third.hasNextPage).toBe(false);
  });

  it('retries a cell whose first page failed with its first page', async () => {
    const calls: string[] = [];
    let failed = false;
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const url = (input as Request).url;
        calls.push(url);
        if (url.includes('/cells/us-1/') && !failed) {
          failed = true;
          return jsonResponse({ title: 'down' }, 503);
        }
        return jsonResponse({ items: [tenant(url.includes('/cells/us-1/') ? 'c' : 'a')] });
      }),
    );
    const store = cellStore();
    const arg = { teamId: 'acme' };

    const first = await store.dispatch(managementApi.endpoints.listTenants.initiate(arg));
    const next = await store.dispatch(
      managementApi.endpoints.listTenants.initiate(arg, { direction: 'forward' }),
    );

    expect(first.hasNextPage).toBe(true);
    expect(calls.at(-1)).toBe('https://api.test/cells/us-1/api/teams/acme/tenants');
    expect(calls).toHaveLength(3);
    expect(pagedTenants(next.data).items.map((t) => t.tenantId)).toEqual(['a', 'c']);
    expect(pagedTenants(next.data).unavailableCells).toEqual([]);
  });

  it('names only the cells whose last request failed', () => {
    const page = (unavailableCells: string[]) => ({ items: [], unavailableCells, cursors: {} });

    expect(pagedTenants({ pages: [page(['eu-1']), page([])] }).unavailableCells).toEqual([]);
    expect(pagedTenants({ pages: [] })).toEqual({ items: [], unavailableCells: [] });
  });

  it('fails the caller tenants when the control plane fails', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse({ title: 'down' }, 503)),
    );
    const store = createAppStore({ config: { runtime } });

    const result = await store.dispatch(managementApi.endpoints.listCallerTenants.initiate());

    expect(result.isError).toBe(true);
    expect(result.error).toMatchObject({ status: 503 });
  });
});
