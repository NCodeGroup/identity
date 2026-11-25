import { describe, expect, it, beforeEach, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { Provider } from 'react-redux';
import { createAppStore } from '@ncodegroup/identity-store';
import type { BffRuntimeConfig } from '@ncodegroup/identity-config';
import { HomePage } from './HomePage.js';
import { cellRoute, platformRoute, teamRoute, tenantRoute } from '../routes.js';

const runtime: BffRuntimeConfig = {
  kind: 'bff',
  apiBaseUrl: 'https://api.test',
  bff: { basePath: '/bff', antiforgeryHeader: { name: 'X-CSRF', value: '1' } },
  features: {},
};

function renderPage(onNavigate = vi.fn(), config: BffRuntimeConfig = runtime) {
  const store = createAppStore({
    config: { runtime: config },
  });
  render(
    <Provider store={store}>
      <HomePage onNavigate={onNavigate} />
    </Provider>,
  );
  return onNavigate;
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  });
}

const team = {
  teamId: 'support',
  concurrencyToken: 'tc1',
  displayName: 'Support',
  isDisabled: false,
};

const tenant = {
  tenantId: 'acme',
  teamId: 'support',
  concurrencyToken: 'tn1',
  displayName: 'Acme',
  issuer: 'https://acme.test',
  mtlsEndpointAliasAuthority: null,
  isDisabled: false,
};

/**
 * Serves the caller's resource, its teams and its tenants; any other request is unexpected; `unavailableCell` names a
 * cell that does not answer.
 */
function installFetch(options: {
  readonly isPlatformAdministrator: boolean;
  readonly teams: readonly unknown[];
  readonly tenants: readonly unknown[];
  readonly unavailableCell?: string;
}) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    const request = input as Request;
    if (request.method === 'GET' && request.url.endsWith('/api/me')) {
      return jsonResponse({
        principalId: 'principal-1',
        isPlatformAdministrator: options.isPlatformAdministrator,
        assignments: [],
      });
    }
    if (request.method === 'GET' && request.url.endsWith('/api/me/teams')) {
      return jsonResponse({ items: options.teams });
    }
    if (
      request.method === 'GET' &&
      request.url.endsWith(`/cells/${options.unavailableCell}/api/me/tenants`)
    ) {
      return jsonResponse({ title: 'Bad Gateway' }, 502);
    }
    if (request.method === 'GET' && request.url.endsWith('/api/me/tenants')) {
      return jsonResponse({ items: options.tenants });
    }
    throw new Error(`Unexpected ${request.method} ${request.url}`);
  });
  vi.stubGlobal('fetch', fetchMock);
}

describe('HomePage', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('lists the platform, the teams and the tenants and navigates on each click', async () => {
    installFetch({
      isPlatformAdministrator: true,
      teams: [team, { ...team, teamId: 'sales', displayName: 'Sales' }],
      tenants: [tenant],
    });

    const onNavigate = renderPage();

    fireEvent.click(await screen.findByTestId('home-open-platform'));
    expect(onNavigate).toHaveBeenLastCalledWith(platformRoute('teams'));

    fireEvent.click(screen.getByTestId('team-row-support'));
    expect(onNavigate).toHaveBeenLastCalledWith(teamRoute('support'));

    fireEvent.click(screen.getByTestId('tenant-row-acme'));
    expect(onNavigate).toHaveBeenLastCalledWith(tenantRoute('acme'));
    expect(onNavigate).toHaveBeenCalledTimes(3);
  });

  it("opens a tenant in the tenant's cell", async () => {
    installFetch({ isPlatformAdministrator: true, teams: [team], tenants: [tenant] });

    const onNavigate = renderPage(vi.fn(), { ...runtime, cells: ['eu-1'] });

    fireEvent.click(await screen.findByTestId('tenant-row-eu-1/acme'));
    expect(onNavigate).toHaveBeenLastCalledWith(tenantRoute('acme', undefined, 'eu-1'));
  });

  it('opens the copy of a tenant in the cell of its row when two cells list it', async () => {
    installFetch({ isPlatformAdministrator: true, teams: [team], tenants: [tenant] });

    const onNavigate = renderPage(vi.fn(), { ...runtime, cells: ['eu-1', 'us-1'] });

    expect(await screen.findByTestId('tenant-cell-us-1/acme')).toHaveTextContent('us-1');
    fireEvent.click(screen.getByTestId('tenant-row-us-1/acme'));
    expect(onNavigate).toHaveBeenLastCalledWith(tenantRoute('acme', undefined, 'us-1'));
  });

  it('opens the only team with replace', async () => {
    installFetch({ isPlatformAdministrator: false, teams: [team], tenants: [] });

    const onNavigate = renderPage();

    await waitFor(() =>
      expect(onNavigate).toHaveBeenCalledWith(teamRoute('support'), { replace: true }),
    );
    expect(onNavigate).toHaveBeenCalledTimes(1);
  });

  it('opens the platform for an administrator with no role of its own', async () => {
    installFetch({ isPlatformAdministrator: true, teams: [], tenants: [] });

    const onNavigate = renderPage();

    await waitFor(() =>
      expect(onNavigate).toHaveBeenCalledWith(platformRoute('teams'), { replace: true }),
    );
    expect(onNavigate).toHaveBeenCalledTimes(1);
  });

  it('names each cell that did not answer and opens no sole destination while one did not', async () => {
    installFetch({
      isPlatformAdministrator: false,
      teams: [team],
      tenants: [],
      unavailableCell: 'us-1',
    });

    const onNavigate = renderPage(vi.fn(), { ...runtime, cells: ['eu-1', 'us-1'] });

    expect(await screen.findByTestId('unavailable-cells')).toHaveTextContent(
      'The cell us-1 did not answer, so its tenants are not listed.',
    );
    await screen.findByTestId('team-row-support');
    expect(onNavigate).not.toHaveBeenCalled();
  });

  it('does not say the caller holds no role while a cell did not answer', async () => {
    installFetch({
      isPlatformAdministrator: false,
      teams: [],
      tenants: [],
      unavailableCell: 'us-1',
    });

    const onNavigate = renderPage(vi.fn(), { ...runtime, cells: ['eu-1', 'us-1'] });

    await screen.findByTestId('unavailable-cells');
    await waitFor(() => expect(screen.queryByTestId('data-loading')).not.toBeInTheDocument());
    expect(screen.queryByTestId('data-empty')).not.toBeInTheDocument();
    expect(onNavigate).not.toHaveBeenCalled();
  });

  it('shows the empty message when the caller holds no role', async () => {
    installFetch({ isPlatformAdministrator: false, teams: [], tenants: [] });

    const onNavigate = renderPage();

    expect(await screen.findByTestId('data-empty')).toHaveTextContent(
      'You hold no role in this console. Ask an administrator for access to a team or a tenant.',
    );
    expect(onNavigate).not.toHaveBeenCalled();
  });

  it('shows a loading indicator while the access loads', async () => {
    installFetch({ isPlatformAdministrator: false, teams: [], tenants: [] });

    renderPage();

    expect(screen.getByTestId('data-loading')).toBeInTheDocument();
    expect(await screen.findByTestId('data-empty')).toBeInTheDocument();
  });

  it('shows an error when the access cannot be loaded', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse({ title: 'Internal Server Error' }, 500)),
    );

    renderPage();

    expect(await screen.findByTestId('data-error')).toHaveTextContent(
      'Failed to load your access.',
    );
  });
});

describe('HomePage of a cell console', () => {
  const cellRuntime: BffRuntimeConfig = { ...runtime, console: 'cell', cell: 'eu-1' };

  function installCellFetch(
    cellRoles: readonly string[],
    tenants: readonly unknown[],
    grantingTeams: readonly unknown[] = [],
  ) {
    const urls: string[] = [];
    const created: unknown[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const request = input as Request;
        urls.push(`${request.method} ${request.url}`);
        if (request.url.endsWith('/api/cell/teams')) {
          return jsonResponse({ items: grantingTeams });
        }
        if (request.method === 'GET' && request.url.includes('/api/teams/support/tenants/')) {
          return jsonResponse({ title: 'Not Found' }, 404);
        }
        if (request.method === 'POST' && request.url.endsWith('/api/teams/support/tenants')) {
          const body: unknown = await request.json();
          created.push(body);
          return jsonResponse({ ...tenant, tenantId: 'globex', displayName: 'Globex' }, 201);
        }
        if (request.url.endsWith('/api/me')) {
          return jsonResponse({
            principalId: 'principal-1',
            isPlatformAdministrator: false,
            assignments: [],
            cellRoles,
          });
        }
        if (request.url.endsWith('/api/cell')) {
          return jsonResponse({
            name: 'eu-1',
            region: 'eu',
            kind: 'shared',
            hasManagementTenant: true,
          });
        }
        if (request.url.includes('/api/cell/tenants')) {
          return jsonResponse({ items: tenants });
        }
        throw new Error(`Unexpected ${request.method} ${request.url}`);
      }),
    );
    return Object.assign(urls, { created });
  }

  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("lists the cell's tenants with their team and opens one without a cell", async () => {
    const urls = installCellFetch(['CellAdmin'], [tenant]);

    const onNavigate = renderPage(vi.fn(), cellRuntime);

    expect(await screen.findByTestId('tenant-team-acme')).toHaveTextContent('support');
    expect(screen.getByTestId('cell-title')).toHaveTextContent('Cell eu-1');
    fireEvent.click(screen.getByTestId('tenant-row-acme'));
    expect(onNavigate).toHaveBeenLastCalledWith(tenantRoute('acme'));
    expect(urls.some((url) => url.includes('/cells/'))).toBe(false);
    expect(urls.some((url) => url.endsWith('/api/me/teams'))).toBe(false);
  });

  it('creates a tenant of a team that grants the cell, in the cell itself', async () => {
    const urls = installCellFetch(['CellAdmin'], [tenant], [team]);

    renderPage(vi.fn(), cellRuntime);

    fireEvent.click(await screen.findByTestId('cell-create-tenant'));
    fireEvent.change(await screen.findByLabelText(/Tenant ID/i), {
      target: { value: 'globex' },
    });
    fireEvent.change(screen.getByLabelText(/Display name/i), { target: { value: 'Globex' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create' }));

    await waitFor(() => expect(urls.created).toHaveLength(1));
    expect(urls.created[0]).toMatchObject({ tenantId: 'globex', displayName: 'Globex' });
    expect(urls.some((url) => url.includes('/cells/'))).toBe(false);
  });

  it('offers no tenant creation while no team grants the cell', async () => {
    installCellFetch(['CellAdmin'], [tenant]);

    renderPage(vi.fn(), cellRuntime);

    await screen.findByTestId('home-open-cell-admins');
    await waitFor(() => expect(screen.queryByTestId('cell-create-tenant')).not.toBeInTheDocument());
  });

  it('links to the administrators for a CellAdmin', async () => {
    installCellFetch(['CellAdmin'], [tenant]);

    const onNavigate = renderPage(vi.fn(), cellRuntime);

    fireEvent.click(await screen.findByTestId('home-open-cell-admins'));
    expect(onNavigate).toHaveBeenLastCalledWith(cellRoute('admins'));
  });

  it('shows no administrators link to a caller without a cell role', async () => {
    installCellFetch([], [tenant]);

    renderPage(vi.fn(), cellRuntime);

    await screen.findByTestId('tenant-row-acme');
    expect(screen.queryByTestId('home-open-cell-admins')).not.toBeInTheDocument();
  });
  it('loads the next page of the cell tenants and marks a disabled tenant', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const request = input as Request;
        if (request.url.endsWith('/api/me')) {
          return jsonResponse({
            principalId: 'principal-1',
            isPlatformAdministrator: false,
            assignments: [],
            cellRoles: [],
          });
        }
        if (request.url.endsWith('/api/cell')) {
          return jsonResponse({
            name: 'eu-1',
            region: 'eu',
            kind: 'shared',
            hasManagementTenant: true,
          });
        }
        if (request.url.includes('/api/cell/tenants')) {
          return request.url.includes('cursor=next')
            ? jsonResponse({ items: [{ ...tenant, tenantId: 'globex', isDisabled: true }] })
            : jsonResponse({ items: [tenant], continuationToken: 'next' });
        }
        throw new Error(`Unexpected ${request.method} ${request.url}`);
      }),
    );

    renderPage(vi.fn(), cellRuntime);
    fireEvent.click(await screen.findByTestId('load-more'));

    expect(await screen.findByTestId('tenant-row-globex')).toHaveTextContent('Disabled');
    expect(screen.getByTestId('tenant-row-acme')).toHaveTextContent('Active');
    await waitFor(() => expect(screen.queryByTestId('load-more')).not.toBeInTheDocument());
  });
});
