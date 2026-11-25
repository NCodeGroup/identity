import { describe, expect, it, beforeEach, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { Provider } from 'react-redux';
import { createAppStore } from '@ncodegroup/identity-store';
import type { BffRuntimeConfig } from '@ncodegroup/identity-config';
import { CellAdminsPage } from './CellAdminsPage.js';

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

const holder = {
  assignmentId: 'a1',
  principalId: 'p1',
  roleName: 'CellAdmin',
  principal: { principalId: 'p1', principalType: 'user', displayName: 'Ada', userName: 'ada' },
};

type Call = { readonly url: string; readonly method: string; readonly body: string };

function installFetch(canChange: boolean) {
  const calls: Call[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const request = input as Request;
      calls.push({ url: request.url, method: request.method, body: await request.clone().text() });
      if (request.url.endsWith('/api/cell/admins') && request.method === 'GET') {
        return jsonResponse({ items: [holder], canChange });
      }
      if (request.url.endsWith('/api/cell/admins') && request.method === 'POST') {
        return jsonResponse({ ...holder, assignmentId: 'a2', principalId: 'p2' }, 201);
      }
      if (request.url.endsWith('/api/cell/admins/p1') && request.method === 'DELETE') {
        return new Response(null, { status: 204 });
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
      <CellAdminsPage />
    </Provider>,
  );
}

describe('CellAdminsPage', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('lists the cell administrators', async () => {
    installFetch(true);

    renderPage();

    expect(await screen.findByTestId('CellAdmin-name-p1')).toHaveTextContent('Ada');
  });

  it('grants the role by username', async () => {
    const calls = installFetch(true);

    renderPage();

    fireEvent.change(await screen.findByTestId('CellAdmin-principal'), {
      target: { value: 'grace' },
    });
    fireEvent.click(screen.getByTestId('CellAdmin-grant'));

    await waitFor(() =>
      expect(calls.some((c) => c.method === 'POST' && c.body === '{"userName":"grace"}')).toBe(
        true,
      ),
    );
  });

  it('revokes the role after confirmation', async () => {
    const calls = installFetch(true);

    renderPage();

    fireEvent.click(await screen.findByLabelText('Revoke Ada'));
    fireEvent.click(await screen.findByRole('button', { name: 'Revoke' }));

    await waitFor(() => expect(calls.some((c) => c.method === 'DELETE')).toBe(true));
  });

  it('offers no grant or revoke to a caller who cannot change the list', async () => {
    installFetch(false);

    renderPage();

    expect(await screen.findByTestId('CellAdmin-read-only')).toBeInTheDocument();
    expect(screen.queryByTestId('CellAdmin-grant')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Revoke Ada')).not.toBeInTheDocument();
  });
});
