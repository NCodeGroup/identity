import { describe, expect, it, beforeEach, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { Provider } from 'react-redux';
import { createAppStore } from '@ncodegroup/identity-store';
import type { BffRuntimeConfig } from '@ncodegroup/identity-config';
import { CellsPage } from './CellsPage.js';

const runtime: BffRuntimeConfig = {
  kind: 'bff',
  apiBaseUrl: 'https://api.test',
  bff: { basePath: '/bff', antiforgeryHeader: { name: 'X-CSRF', value: '1' } },
  features: {},
  console: 'platform',
  cells: ['eu-1', 'us-1'],
};

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  });
}

const cell = {
  name: 'eu-1',
  region: 'eu',
  kind: 'shared',
  isControlPlane: false,
  isRegistered: true,
  isRetired: false,
};

type Call = { readonly url: string; readonly method: string; readonly body: string };

function installFetch(retireStatus = 204) {
  const calls: Call[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const request = input as Request;
      calls.push({ url: request.url, method: request.method, body: await request.clone().text() });
      if (request.url.endsWith('/api/cells') && request.method === 'GET') {
        return jsonResponse([cell]);
      }
      if (request.url.endsWith('/api/cells/new-1') && request.method === 'PUT') {
        return jsonResponse({ ...cell, name: 'new-1' });
      }
      if (request.url.endsWith('/api/cells/eu-1') && request.method === 'DELETE') {
        return retireStatus === 204
          ? new Response(null, { status: 204 })
          : jsonResponse({ title: 'Conflict', detail: 'The cell still holds tenants.' }, 409);
      }
      throw new Error(`Unexpected ${request.method} ${request.url}`);
    }),
  );
  return calls;
}

function renderPage() {
  const store = createAppStore({ config: { runtime } });
  render(
    <Provider store={store}>
      <CellsPage />
    </Provider>,
  );
}

describe('CellsPage', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('lists the cells and names a configured cell the list lacks', async () => {
    installFetch();

    renderPage();

    expect(await screen.findByTestId('cell-row-eu-1')).toHaveTextContent('eu');
    expect(screen.getByTestId('unavailable-cells')).toHaveTextContent('us-1');
  });

  it('refuses a JWKS without keys', async () => {
    const calls = installFetch();

    renderPage();
    await screen.findByTestId('cell-row-eu-1');
    fireEvent.click(screen.getByTestId('register-cell'));
    fireEvent.change(screen.getByTestId('cell-name'), { target: { value: 'new-1' } });
    fireEvent.change(screen.getByTestId('cell-region'), { target: { value: 'eu' } });
    fireEvent.change(screen.getByTestId('cell-jwks'), { target: { value: '{"keys":[]}' } });
    fireEvent.click(screen.getByTestId('register-cell-submit'));

    expect(await screen.findByTestId('register-cell-error')).toHaveTextContent('non-empty');
    expect(calls.some((c) => c.method === 'PUT')).toBe(false);
  });

  it('registers a cell', async () => {
    const calls = installFetch();

    renderPage();
    await screen.findByTestId('cell-row-eu-1');
    fireEvent.click(screen.getByTestId('register-cell'));
    fireEvent.change(screen.getByTestId('cell-name'), { target: { value: 'new-1' } });
    fireEvent.change(screen.getByTestId('cell-region'), { target: { value: 'eu' } });
    fireEvent.change(screen.getByTestId('cell-jwks'), {
      target: { value: '{"keys":[{"kty":"EC"}]}' },
    });
    fireEvent.click(screen.getByTestId('register-cell-submit'));

    await waitFor(() =>
      expect(
        calls.some(
          (c) =>
            c.method === 'PUT' &&
            c.body === '{"region":"eu","kind":"shared","jwks":{"keys":[{"kty":"EC"}]}}',
        ),
      ).toBe(true),
    );
  });

  it('retires a cell after a confirmation', async () => {
    const calls = installFetch();

    renderPage();
    fireEvent.click(await screen.findByTestId('retire-cell-eu-1'));
    fireEvent.click(screen.getByTestId('confirm-accept'));

    await waitFor(() => expect(calls.some((c) => c.method === 'DELETE')).toBe(true));
  });

  it('shows the detail of a refused retirement', async () => {
    installFetch(409);

    renderPage();
    fireEvent.click(await screen.findByTestId('retire-cell-eu-1'));
    fireEvent.click(screen.getByTestId('confirm-accept'));

    expect(await screen.findByTestId('confirm-error')).toHaveTextContent('still holds tenants');
  });
});
