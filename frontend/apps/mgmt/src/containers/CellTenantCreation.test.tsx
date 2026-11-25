import { describe, expect, it, beforeEach, vi } from 'vitest';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import { Provider } from 'react-redux';
import { createAppStore } from '@ncodegroup/identity-store';
import type { BffRuntimeConfig } from '@ncodegroup/identity-config';
import { CellTenantCreation } from './CellTenantCreation.js';

const runtime: BffRuntimeConfig = {
  kind: 'bff',
  apiBaseUrl: 'https://api.test',
  bff: { basePath: '/bff', antiforgeryHeader: { name: 'X-CSRF', value: '1' } },
  features: {},
  console: 'cell',
  cell: 'eu-1',
};

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  });
}

const teams = [
  { teamId: 'acme', concurrencyToken: 't1', displayName: 'Acme', isDisabled: false },
  { teamId: 'globex', concurrencyToken: 't2', displayName: 'Globex', isDisabled: false },
];

/**
 * Serves the teams that grant the cell, the address previews and the creation; `createStatus` answers the creation and
 * `taken` marks every previewed identifier as taken.
 */
function installFetch(options: {
  readonly teams: readonly unknown[];
  readonly createStatus?: number;
  readonly taken?: boolean;
}) {
  const requests: { method: string; url: string; body?: unknown }[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const request = input as Request;
      const url = request.url;
      if (url.endsWith('/api/cell/teams')) {
        return jsonResponse({ items: options.teams });
      }
      if (request.method === 'GET' && url.includes('/address')) {
        requests.push({ method: 'GET', url });
        const tenantId = decodeURIComponent(new URL(url).pathname.split('/').at(-2) ?? '');
        return jsonResponse({
          tenantId,
          issuer: `https://eu-1.test/${tenantId}`,
          isIssuerDerived: true,
          mtlsEndpointAliasAuthority: null,
          isMtlsEndpointAliasAuthorityDerived: false,
          exists: options.taken ?? false,
          failures: [],
        });
      }
      if (request.method === 'POST' && url.includes('/tenants')) {
        const body: unknown = await request.json();
        requests.push({ method: 'POST', url, body });
        const status = options.createStatus ?? 201;
        return status === 201
          ? jsonResponse(
              {
                tenantId: (body as { tenantId: string }).tenantId,
                teamId: 'acme',
                concurrencyToken: 'c1',
                displayName: 'New',
                issuer: null,
                mtlsEndpointAliasAuthority: null,
                isDisabled: false,
              },
              201,
            )
          : jsonResponse({ title: 'Conflict', detail: 'The tenant is taken.' }, status);
      }
      throw new Error(`Unexpected ${request.method} ${url}`);
    }),
  );
  return requests;
}

function renderCreation(onCreated = vi.fn()) {
  const store = createAppStore({ config: { runtime } });
  render(
    <Provider store={store}>
      <CellTenantCreation onCreated={onCreated} addressPreviewDelayMs={0} />
    </Provider>,
  );
  return onCreated;
}

async function fillAndCreate(tenantId: string, extra?: { issuer?: string; mtls?: string }) {
  fireEvent.click(await screen.findByTestId('cell-create-tenant'));
  const dialog = await screen.findByRole('dialog');
  fireEvent.change(within(dialog).getByLabelText(/Tenant ID/i), { target: { value: tenantId } });
  fireEvent.change(within(dialog).getByLabelText(/Display name/i), { target: { value: 'New' } });
  if (extra?.issuer) {
    fireEvent.change(within(dialog).getByLabelText(/Issuer/i), { target: { value: extra.issuer } });
  }
  if (extra?.mtls) {
    fireEvent.change(within(dialog).getByLabelText(/mTLS endpoint alias/i), {
      target: { value: extra.mtls },
    });
  }
  fireEvent.click(within(dialog).getByRole('button', { name: 'Create' }));
}

describe('CellTenantCreation', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('renders nothing while no team grants the cell', async () => {
    installFetch({ teams: [] });

    const { container } = render(
      <Provider store={createAppStore({ config: { runtime } })}>
        <CellTenantCreation />
      </Provider>,
    );

    await waitFor(() => expect(container).toBeEmptyDOMElement());
  });

  it('creates a tenant of the first granting team and reports it', async () => {
    const requests = installFetch({ teams });

    const onCreated = renderCreation();
    await fillAndCreate('acme-dev');

    await waitFor(() => expect(onCreated).toHaveBeenCalledWith('acme-dev'));
    const created = requests.find((request) => request.method === 'POST');
    expect(created?.url).toBe('https://api.test/api/teams/acme/tenants');
    expect(created?.body).toMatchObject({
      tenantId: 'acme-dev',
      issuer: null,
      mtlsEndpointAliasAuthority: null,
    });
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('creates a tenant of the chosen team with the issuer and alias entered', async () => {
    const requests = installFetch({ teams });

    renderCreation();
    const select = await screen.findByTestId('cell-tenant-team');
    fireEvent.change(select, { target: { value: 'globex' } });
    await fillAndCreate('globex-dev', {
      issuer: 'https://login.globex.test',
      mtls: 'https://mtls.globex.test',
    });

    await waitFor(() => expect(requests.some((request) => request.method === 'POST')).toBe(true));
    const created = requests.find((request) => request.method === 'POST');
    expect(created?.url).toBe('https://api.test/api/teams/globex/tenants');
    expect(created?.body).toMatchObject({
      issuer: 'https://login.globex.test',
      mtlsEndpointAliasAuthority: 'https://mtls.globex.test',
    });
  });

  it('previews the address through the chosen team and shows a taken identifier', async () => {
    const requests = installFetch({ teams, taken: true });

    renderCreation();
    fireEvent.click(await screen.findByTestId('cell-create-tenant'));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText(/Tenant ID/i), {
      target: { value: 'acme-dev' },
    });

    expect(await within(dialog).findByText('A tenant with this ID already exists.')).toBeVisible();
    expect(requests.some((request) => request.url.includes('/api/teams/acme/tenants/'))).toBe(true);
  });

  it('shows the error of a refused creation and keeps the dialog open', async () => {
    installFetch({ teams, createStatus: 409 });

    const onCreated = renderCreation();
    await fillAndCreate('acme-dev');

    expect(await screen.findByText(/The tenant is taken\./)).toBeVisible();
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(onCreated).not.toHaveBeenCalled();
  });

  it('closes the dialog on cancel without creating', async () => {
    const requests = installFetch({ teams });

    renderCreation();
    fireEvent.click(await screen.findByTestId('cell-create-tenant'));
    const dialog = await screen.findByRole('dialog');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(requests.some((request) => request.method === 'POST')).toBe(false);
  });
});
