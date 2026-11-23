import { describe, expect, it, beforeEach, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { Provider } from 'react-redux';
import { createAppStore } from '@ncodegroup/identity-store';
import type { ConnectionResource } from '@ncodegroup/identity-api';
import type { BffRuntimeConfig } from '@ncodegroup/identity-config';
import { ConnectionDetailPage } from './ConnectionDetailPage.js';

const runtime: BffRuntimeConfig = {
  kind: 'bff',
  apiBaseUrl: 'https://api.test',
  bff: { basePath: '/bff', antiforgeryHeader: { name: 'X-CSRF', value: '1' } },
  features: {},
};

const connection: ConnectionResource = {
  tenantId: 'acme',
  connectionId: 'c-local',
  concurrencyToken: 'k1',
  name: 'local',
  displayName: 'Username and password',
  strategy: 'local',
  isSystem: false,
  isEnabled: true,
  options: { store: 'default' },
};

function renderPage(onBack = vi.fn()) {
  const store = createAppStore({ config: { runtime } });
  render(
    <Provider store={store}>
      <ConnectionDetailPage tenantId="acme" connection={connection} onBack={onBack} />
    </Provider>,
  );
  return onBack;
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  });
}

type Call = { readonly url: string; readonly method: string; readonly body: string };

function installFetch(handler: (url: string, method: string) => Response) {
  const calls: Call[] = [];
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    const request = input as Request;
    calls.push({
      url: request.url,
      method: request.method,
      body: request.method === 'GET' ? '' : await request.clone().text(),
    });
    return handler(request.url, request.method);
  });
  vi.stubGlobal('fetch', fetchMock);
  return calls;
}

const base = '/api/tenants/acme/connections/c-local';

describe('ConnectionDetailPage', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('shows the connection and calls back to the list', () => {
    installFetch(() => jsonResponse({}));
    const onBack = renderPage();

    expect(screen.getByText('Username and password')).toBeInTheDocument();
    fireEvent.click(screen.getByTestId('connection-detail-back'));
    expect(onBack).toHaveBeenCalledTimes(1);
  });

  it('saves the general fields as an If-Match JSON Patch', async () => {
    const calls = installFetch((url, method) => {
      if (url.endsWith(base) && method === 'PATCH') {
        return new Response(null, { status: 204 });
      }
      throw new Error(`Unexpected ${method} ${url}`);
    });
    renderPage();

    expect(screen.getByTestId('connection-detail-store')).toHaveValue('default');
    expect(screen.getByTestId('connection-detail-store')).toBeDisabled();
    fireEvent.change(screen.getByTestId('connection-detail-display-name'), {
      target: { value: 'Staff' },
    });
    fireEvent.click(screen.getByTestId('connection-general-save'));

    expect(await screen.findByTestId('connection-general-saved')).toBeInTheDocument();
    const patch = calls.find((c) => c.method === 'PATCH');
    expect(JSON.parse(patch?.body ?? '[]')).toEqual([
      { op: 'replace', path: '/displayName', value: 'Staff' },
      { op: 'replace', path: '/isEnabled', value: true },
      { op: 'replace', path: '/options', value: { store: 'default' } },
    ]);
  });

  it('saves the enabled switch as disabled', async () => {
    const calls = installFetch((url, method) => {
      if (url.endsWith(base) && method === 'PATCH') {
        return new Response(null, { status: 204 });
      }
      throw new Error(`Unexpected ${method} ${url}`);
    });
    renderPage();

    fireEvent.click(screen.getByRole('switch', { name: 'Enabled' }));
    fireEvent.click(screen.getByTestId('connection-general-save'));

    expect(await screen.findByTestId('connection-general-saved')).toBeInTheDocument();
    const patch = calls.find((c) => c.method === 'PATCH');
    expect(JSON.parse(patch?.body ?? '[]')).toContainEqual({
      op: 'replace',
      path: '/isEnabled',
      value: false,
    });
  });

  it('shows a save failure on the general tab', async () => {
    installFetch(() => jsonResponse({ title: 'Conflict', detail: 'Stale token.' }, 412));
    renderPage();

    fireEvent.click(screen.getByTestId('connection-general-save'));

    expect(await screen.findByTestId('connection-general-error')).toBeInTheDocument();
  });

  it('saves edited connection settings as a JSON Patch diff beside the effective settings and limits', async () => {
    const calls = installFetch((url, method) => {
      if (url.endsWith(`${base}/settings`) && method === 'PATCH') {
        return new Response(null, { status: 204 });
      }
      if (url.endsWith(`${base}/settings/limits`)) {
        return jsonResponse({ names: [], canChange: true });
      }
      if (url.endsWith(`${base}/settings`)) {
        return jsonResponse({
          tenantId: 'acme',
          connectionId: 'c-local',
          concurrencyToken: 'sc9',
          settings: { a: 1 },
        });
      }
      if (url.endsWith(`${base}/effective-settings`)) {
        return jsonResponse({ settings: { a: 1, password_min_length: 8 } });
      }
      throw new Error(`Unexpected ${method} ${url}`);
    });
    renderPage();

    fireEvent.click(screen.getByTestId('connection-tab-settings'));
    const editor = await screen.findByTestId('settings-json');
    await waitFor(() =>
      expect(screen.getByTestId('effective-settings-json')).toHaveValue(
        JSON.stringify({ a: 1, password_min_length: 8 }, null, 2),
      ),
    );
    fireEvent.change(editor, { target: { value: '{"a": 2}' } });
    fireEvent.click(screen.getByTestId('settings-save'));

    await waitFor(() => {
      const patch = calls.find((c) => c.method === 'PATCH');
      expect(patch?.url).toBe(`https://api.test${base}/settings`);
      expect(JSON.parse(patch?.body ?? '[]')).toEqual([{ op: 'replace', path: '/a', value: 2 }]);
    });
  });
});
