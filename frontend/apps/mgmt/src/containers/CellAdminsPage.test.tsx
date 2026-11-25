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

function installFetch(canChange: boolean, failWrites = false) {
  const calls: Call[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const request = input as Request;
      calls.push({ url: request.url, method: request.method, body: await request.clone().text() });
      if (request.url.endsWith('/api/cell/admins') && request.method === 'GET') {
        return jsonResponse({ items: [holder], canChange });
      }
      if (failWrites && request.method !== 'GET') {
        return jsonResponse({ title: 'Forbidden', detail: 'The change is refused.' }, 403);
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
  it('grants the role by principal ID after switching the grant mode', async () => {
    const calls = installFetch(true);

    renderPage();

    fireEvent.click(await screen.findByTestId('CellAdmin-grant-by-principal-id'));
    fireEvent.change(screen.getByTestId('CellAdmin-principal'), { target: { value: ' p2 ' } });
    fireEvent.click(screen.getByTestId('CellAdmin-grant'));

    await waitFor(() =>
      expect(calls.some((c) => c.method === 'POST' && c.body === '{"principalId":"p2"}')).toBe(
        true,
      ),
    );
    await waitFor(() => expect(screen.getByTestId('CellAdmin-principal')).toHaveValue(''));
  });

  it('keeps the grant mode when the selected toggle is pressed again', async () => {
    installFetch(true);

    renderPage();

    fireEvent.click(await screen.findByTestId('CellAdmin-grant-by-user-name'));

    expect(screen.getByTestId('CellAdmin-grant-by-user-name')).toHaveAttribute(
      'aria-pressed',
      'true',
    );
  });

  it('offers no grant for an empty principal', async () => {
    installFetch(true);

    renderPage();

    expect(await screen.findByTestId('CellAdmin-grant')).toBeDisabled();
  });

  it('shows the server error of a refused grant', async () => {
    installFetch(true, true);

    renderPage();

    fireEvent.change(await screen.findByTestId('CellAdmin-principal'), {
      target: { value: 'grace' },
    });
    fireEvent.click(screen.getByTestId('CellAdmin-grant'));

    expect(await screen.findByTestId('CellAdmin-error')).toHaveTextContent('refused');
  });

  it('shows the server error of a refused revoke and closes the confirmation', async () => {
    installFetch(true, true);

    renderPage();

    fireEvent.click(await screen.findByLabelText('Revoke Ada'));
    fireEvent.click(await screen.findByRole('button', { name: 'Revoke' }));

    expect(await screen.findByTestId('CellAdmin-error')).toHaveTextContent('refused');
    await waitFor(() => expect(screen.queryByTestId('confirm-accept')).not.toBeInTheDocument());
  });

  it('closes the revoke confirmation on cancel without revoking', async () => {
    const calls = installFetch(true);

    renderPage();

    fireEvent.click(await screen.findByLabelText('Revoke Ada'));
    fireEvent.click(await screen.findByTestId('confirm-cancel'));

    await waitFor(() => expect(screen.queryByTestId('confirm-accept')).not.toBeInTheDocument());
    expect(calls.some((c) => c.method === 'DELETE')).toBe(false);
  });
});
