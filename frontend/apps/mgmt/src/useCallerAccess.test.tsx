import type { ReactNode } from 'react';
import { describe, expect, it, beforeEach, vi } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { Provider } from 'react-redux';
import { createAppStore } from '@ncodegroup/identity-store';
import type { BffRuntimeConfig } from '@ncodegroup/identity-config';
import { useCallerAccess } from './useCallerAccess.js';

const runtime: BffRuntimeConfig = {
  kind: 'bff',
  apiBaseUrl: 'https://api.test',
  bff: { basePath: '/bff', antiforgeryHeader: { name: 'X-CSRF', value: '1' } },
  features: {},
};

function renderAccess(isSignedIn: boolean, cells?: readonly string[], console?: 'cell') {
  const store = createAppStore({
    config: {
      runtime: { ...runtime, ...(cells ? { cells } : {}), ...(console ? { console } : {}) },
    },
  });
  const wrapper = ({ children }: { readonly children: ReactNode }) => (
    <Provider store={store}>{children}</Provider>
  );
  return renderHook(() => useCallerAccess(isSignedIn), { wrapper });
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  });
}

/** Serves the caller's resource, its teams and its tenants; any other request is unexpected. */
function installFetch({ tenants = [] }: { readonly tenants?: readonly unknown[] } = {}) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    const request = input as Request;
    if (request.method === 'GET' && request.url.endsWith('/api/me')) {
      return jsonResponse({
        principalId: 'principal-1',
        isPlatformAdministrator: false,
        assignments: [],
      });
    }
    if (request.method === 'GET' && request.url.endsWith('/api/me/teams')) {
      return jsonResponse({
        items: [
          { teamId: 'support', concurrencyToken: 'tc1', displayName: 'Support', isDisabled: false },
        ],
      });
    }
    if (request.method === 'GET' && request.url.endsWith('/api/me/tenants')) {
      return jsonResponse({ items: tenants });
    }
    throw new Error(`Unexpected ${request.method} ${request.url}`);
  });
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

describe('useCallerAccess', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('is unknown and sends no request while signed out', () => {
    const fetchMock = installFetch();

    const { result } = renderAccess(false);

    expect(result.current).toBeUndefined();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('is unknown while the queries are loading', async () => {
    installFetch();

    const { result } = renderAccess(true);

    expect(result.current).toBeUndefined();
    await waitFor(() => expect(result.current).toBeDefined());
  });

  it('combines the caller, its teams and its tenants once they answer', async () => {
    installFetch();

    const { result } = renderAccess(true);

    await waitFor(() =>
      expect(result.current).toEqual({
        isPlatformAdministrator: false,
        teamIds: ['support'],
        tenants: [],
      }),
    );
  });

  it("carries a sole tenant's cell once the queries answer", async () => {
    installFetch({ tenants: [{ tenantId: 'web', concurrencyToken: 't1' }] });

    const { result } = renderAccess(true, ['eu']);

    await waitFor(() => expect(result.current?.tenants).toEqual([{ tenantId: 'web', cell: 'eu' }]));
  });

  it('sends no teams request in a cell console and holds no team', async () => {
    const fetchMock = installFetch();

    const { result } = renderAccess(true, undefined, 'cell');

    await waitFor(() =>
      expect(result.current).toEqual({
        isPlatformAdministrator: false,
        teamIds: [],
        tenants: [],
      }),
    );
    const urls = fetchMock.mock.calls.map(([input]) => (input as Request).url);
    expect(urls.some((url) => url.endsWith('/api/me/teams'))).toBe(false);
  });
});
