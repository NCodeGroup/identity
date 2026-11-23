import { describe, expect, it, beforeEach, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { Provider } from 'react-redux';
import { createAppStore } from '@ncodegroup/identity-store';
import type { BffRuntimeConfig } from '@ncodegroup/identity-config';
import { ConnectionsPage } from './ConnectionsPage.js';

const runtime: BffRuntimeConfig = {
  kind: 'bff',
  apiBaseUrl: 'https://api.test',
  bff: { basePath: '/bff', antiforgeryHeader: { name: 'X-CSRF', value: '1' } },
  features: {},
};

function renderPage() {
  const store = createAppStore({ config: { runtime } });
  return render(
    <Provider store={store}>
      <ConnectionsPage tenantId="acme" />
    </Provider>,
  );
}

const local = {
  tenantId: 'acme',
  connectionId: 'c-local',
  concurrencyToken: 'k1',
  name: 'local',
  displayName: 'Username and password',
  strategy: 'local',
  isSystem: false,
  isEnabled: true,
  options: { store: 'default', extra: 1 },
};

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  });
}

type Call = {
  readonly url: string;
  readonly method: string;
  readonly body: string;
  readonly ifMatch: string | null;
};

function installFetch(handler: (url: string, method: string) => Response) {
  const calls: Call[] = [];
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    const request = input as Request;
    calls.push({
      url: request.url,
      method: request.method,
      body: request.method === 'GET' ? '' : await request.clone().text(),
      ifMatch: request.headers.get('if-match'),
    });
    return handler(request.url, request.method);
  });
  vi.stubGlobal('fetch', fetchMock);
  return calls;
}

const listUrl = '/api/tenants/acme/connections';

describe('ConnectionsPage', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('shows the empty state when the tenant has no connections', async () => {
    installFetch(() => jsonResponse({ items: [] }));

    renderPage();

    expect(await screen.findByTestId('data-empty')).toHaveTextContent(
      'No connections in this tenant yet.',
    );
  });

  it('reports a failure to load the connection list', async () => {
    installFetch(() => jsonResponse({ title: 'Internal Server Error' }, 500));

    renderPage();

    expect(await screen.findByTestId('data-error')).toHaveTextContent(
      'Failed to load connections.',
    );
  });

  it('creates a connection whose options carry the store', async () => {
    const calls = installFetch((url, method) => {
      if (url.endsWith(listUrl) && method === 'POST') {
        return jsonResponse({ ...local, connectionId: 'c-new' }, 201);
      }
      if (url.endsWith(listUrl)) {
        return jsonResponse({ items: [local] });
      }
      throw new Error(`Unexpected ${method} ${url}`);
    });

    renderPage();
    expect(await screen.findByText('Username and password')).toBeInTheDocument();

    fireEvent.click(screen.getByTestId('create-connection'));
    fireEvent.change(screen.getByTestId('connection-name'), { target: { value: 'staff' } });
    fireEvent.change(screen.getByTestId('connection-display-name'), {
      target: { value: 'Staff' },
    });
    fireEvent.change(screen.getByTestId('connection-store'), { target: { value: 'staff-db' } });
    fireEvent.click(screen.getByTestId('connection-form-submit'));

    await waitFor(() =>
      expect(screen.queryByTestId('connection-form-submit')).not.toBeInTheDocument(),
    );
    const post = calls.find((c) => c.method === 'POST');
    expect(post?.url).toBe(`https://api.test${listUrl}`);
    expect(JSON.parse(post?.body ?? '{}')).toEqual({
      name: 'staff',
      displayName: 'Staff',
      strategy: 'local',
      isEnabled: true,
      options: { store: 'staff-db' },
    });
  });

  it('edits a connection with an If-Match JSON Patch that keeps the other options', async () => {
    const calls = installFetch((url, method) => {
      if (url.endsWith(`${listUrl}/c-local`) && method === 'PATCH') {
        return new Response(null, { status: 204 });
      }
      if (url.endsWith(listUrl)) {
        return jsonResponse({ items: [local] });
      }
      throw new Error(`Unexpected ${method} ${url}`);
    });

    renderPage();
    fireEvent.click(await screen.findByTestId('connection-edit-c-local'));

    expect(screen.getByTestId('connection-name')).toBeDisabled();
    expect(screen.getByTestId('connection-store')).toHaveValue('default');
    expect(screen.getByTestId('connection-store')).toBeDisabled();
    fireEvent.change(screen.getByTestId('connection-store'), { target: { value: 'other' } });
    fireEvent.click(screen.getByRole('switch', { name: 'Enabled' }));
    fireEvent.click(screen.getByTestId('connection-form-submit'));

    await waitFor(() => expect(calls.some((c) => c.method === 'PATCH')).toBe(true));
    const patch = calls.find((c) => c.method === 'PATCH');
    expect(patch?.ifMatch).toBe('k1');
    expect(JSON.parse(patch?.body ?? '[]')).toEqual([
      { op: 'replace', path: '/displayName', value: 'Username and password' },
      { op: 'replace', path: '/isEnabled', value: false },
      { op: 'replace', path: '/options', value: { store: 'default', extra: 1 } },
    ]);
  });

  it('keeps the confirm dialog open and shows the server detail when a delete is refused', async () => {
    const calls = installFetch((url, method) => {
      if (url.endsWith(`${listUrl}/c-local`) && method === 'DELETE') {
        return jsonResponse(
          { title: 'Conflict', detail: 'The connection has local accounts.' },
          409,
        );
      }
      if (url.endsWith(listUrl)) {
        return jsonResponse({ items: [local] });
      }
      throw new Error(`Unexpected ${method} ${url}`);
    });

    renderPage();
    fireEvent.click(await screen.findByTestId('connection-delete-c-local'));
    fireEvent.click(screen.getByRole('button', { name: 'Delete' }));

    expect(await screen.findByText('The connection has local accounts.')).toBeInTheDocument();
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(calls.some((c) => c.method === 'DELETE')).toBe(true);
  });

  it('deletes a connection and closes the confirm dialog', async () => {
    const calls = installFetch((url, method) => {
      if (url.endsWith(`${listUrl}/c-local`) && method === 'DELETE') {
        return new Response(null, { status: 204 });
      }
      if (url.endsWith(listUrl)) {
        return jsonResponse({ items: [local] });
      }
      throw new Error(`Unexpected ${method} ${url}`);
    });

    renderPage();
    fireEvent.click(await screen.findByTestId('connection-delete-c-local'));
    fireEvent.click(screen.getByRole('button', { name: 'Delete' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(calls.some((c) => c.method === 'DELETE')).toBe(true);
  });

  it('closes the form and the confirm dialog on cancel', async () => {
    installFetch((url) => {
      if (url.endsWith(listUrl)) {
        return jsonResponse({ items: [local] });
      }
      throw new Error(`Unexpected ${url}`);
    });

    renderPage();
    fireEvent.click(await screen.findByTestId('create-connection'));
    fireEvent.click(screen.getByTestId('connection-form-cancel'));
    await waitFor(() =>
      expect(screen.queryByTestId('connection-form-submit')).not.toBeInTheDocument(),
    );

    fireEvent.click(screen.getByTestId('connection-delete-c-local'));
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('opens a connection in its detail page and returns to the list', async () => {
    installFetch((url) => {
      if (url.endsWith(listUrl)) {
        return jsonResponse({ items: [local] });
      }
      throw new Error(`Unexpected ${url}`);
    });

    renderPage();
    fireEvent.click(await screen.findByTestId('connection-row-c-local'));

    expect(await screen.findByTestId('connection-tab-general')).toBeInTheDocument();
    fireEvent.click(screen.getByTestId('connection-detail-back'));
    expect(await screen.findByTestId('create-connection')).toBeInTheDocument();
  });
});
