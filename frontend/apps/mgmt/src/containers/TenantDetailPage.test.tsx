import { describe, expect, it, beforeEach, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { Provider } from 'react-redux';
import { createAppStore } from '@ncode/store';
import type { CreatedSecretResource } from '@ncode/api';
import type { BffRuntimeConfig } from '@ncode/config';
import { TenantDetailPage } from './TenantDetailPage.js';
import type { TenantOwnSurface } from '../routes.js';
import { TenantCellProvider } from '../tenantCell.js';

const runtime: BffRuntimeConfig = {
  kind: 'bff',
  apiBaseUrl: 'https://api.test',
  bff: { basePath: '/bff', antiforgeryHeader: { name: 'X-CSRF', value: '1' } },
  features: {},
};

function renderPage(surface: TenantOwnSurface = 'secrets', cell?: string) {
  const store = createAppStore({
    config: { runtime },
  });
  return render(
    <Provider store={store}>
      <TenantCellProvider cell={cell}>
        <TenantDetailPage tenantId="acme" surface={surface} />
      </TenantCellProvider>
    </Provider>,
  );
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

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  });
}

/** The shared-secret type the server advertises, so the create form offers it. */
const symmetricType = {
  secretType: 'symmetric',
  isAsymmetric: false,
  supportsPublicKeyExport: false,
};

const createdSecret: CreatedSecretResource = {
  secretId: 's-new',
  keyId: 's-new',
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
  remoteKeyProvider: null,
  managedBy: null,
  secretMaterial: 'one-time-material',
};

describe('TenantDetailPage', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("reads and deletes the tenant's secrets in the tenant's cell", async () => {
    let deleted = false;
    const calls = installFetch((url, method) => {
      if (url.endsWith('/api/capabilities')) {
        return jsonResponse({ secretTypes: [] });
      }
      if (url.endsWith('/cells/eu-1/api/tenants/acme/secrets/s-old') && method === 'DELETE') {
        deleted = true;
        return new Response(null, { status: 204 });
      }
      if (url.endsWith('/cells/eu-1/api/tenants/acme/secrets')) {
        return jsonResponse({
          concurrencyToken: 'c1',
          secrets: deleted ? [] : [{ ...createdSecret, secretId: 's-old' }],
        });
      }
      throw new Error(`Unexpected ${method} ${url}`);
    });

    renderPage('secrets', 'eu-1');
    fireEvent.click(await screen.findByTestId('secret-delete-s-old'));
    fireEvent.click(screen.getByTestId('confirm-accept'));

    expect(await screen.findByTestId('data-empty')).toBeInTheDocument();
    expect(calls.find((c) => c.method === 'DELETE')?.url).toBe(
      'https://api.test/cells/eu-1/api/tenants/acme/secrets/s-old',
    );
  });

  it("reads the tenant's usage in the tenant's cell", async () => {
    const calls = installFetch((url, method) => {
      if (url.endsWith('/cells/eu-1/api/tenants/acme/usage')) {
        return jsonResponse({
          tenantId: 'acme',
          from: '2026-09-21',
          to: '2026-10-21',
          entries: [],
        });
      }
      throw new Error(`Unexpected ${method} ${url}`);
    });

    renderPage('usage', 'eu-1');

    expect(await screen.findByTestId('data-empty')).toBeInTheDocument();
    expect(calls.find((c) => c.url.includes('/usage'))?.url).toBe(
      'https://api.test/cells/eu-1/api/tenants/acme/usage',
    );
  });

  it('identifies the tenant whose own secrets are listed', async () => {
    installFetch((url, method) => {
      if (url.endsWith('/api/capabilities')) {
        return jsonResponse({ secretTypes: [] });
      }
      if (url.endsWith('/api/tenants/acme/secrets')) {
        return jsonResponse({ concurrencyToken: 'c1', secrets: [] });
      }
      throw new Error(`Unexpected ${method} ${url}`);
    });

    renderPage();

    expect(screen.getByRole('heading', { name: 'Tenant' })).toBeInTheDocument();
    expect(screen.getByText('acme')).toBeInTheDocument();
    expect(await screen.findByTestId('data-empty')).toHaveTextContent(
      'This tenant has no secrets yet.',
    );
  });

  it('deletes a tenant secret under the tenant path after confirmation', async () => {
    let deleted = false;
    const calls = installFetch((url, method) => {
      if (url.endsWith('/api/capabilities')) {
        return jsonResponse({ secretTypes: [] });
      }
      if (url.endsWith('/api/tenants/acme/secrets/s-old') && method === 'DELETE') {
        deleted = true;
        return new Response(null, { status: 204 });
      }
      if (url.endsWith('/api/tenants/acme/secrets')) {
        return jsonResponse({
          concurrencyToken: 'c1',
          secrets: deleted ? [] : [{ ...createdSecret, secretId: 's-old' }],
        });
      }
      throw new Error(`Unexpected ${method} ${url}`);
    });

    renderPage();
    fireEvent.click(await screen.findByTestId('secret-delete-s-old'));
    fireEvent.click(screen.getByTestId('confirm-accept'));

    expect(await screen.findByTestId('data-empty')).toBeInTheDocument();
    expect(calls.find((c) => c.method === 'DELETE')?.url).toBe(
      'https://api.test/api/tenants/acme/secrets/s-old',
    );
  });

  it('exports a tenant secret public key', async () => {
    installFetch((url, method) => {
      if (url.endsWith('/api/capabilities')) {
        return jsonResponse({
          secretTypes: [{ secretType: 'rsa', isAsymmetric: true, supportsPublicKeyExport: true }],
        });
      }
      if (url.endsWith('/api/tenants/acme/secrets/s-rsa/public-key')) {
        return jsonResponse({
          secretId: 's-rsa',
          secretType: 'rsa',
          jwk: { kty: 'RSA', n: 'bW9k', e: 'AQAB' },
          pem: '-----BEGIN PUBLIC KEY-----\nMIIB\n-----END PUBLIC KEY-----',
        });
      }
      if (url.endsWith('/api/tenants/acme/secrets')) {
        return jsonResponse({
          concurrencyToken: 'c1',
          secrets: [{ ...createdSecret, secretId: 's-rsa', secretType: 'rsa', keySizeBits: 2048 }],
        });
      }
      throw new Error(`Unexpected ${method} ${url}`);
    });

    renderPage();
    fireEvent.click(await screen.findByTestId('secret-export-s-rsa'));

    expect(await screen.findByTestId('export-public-key-pem-value')).toHaveTextContent(
      'BEGIN PUBLIC KEY',
    );
    expect(screen.getByTestId('export-public-key-jwk-value')).toHaveTextContent('"kty": "RSA"');
  });

  it('creates a tenant secret under the tenant path and reveals its material', async () => {
    installFetch((url, method) => {
      if (url.endsWith('/api/capabilities')) {
        return jsonResponse({ secretTypes: [symmetricType] });
      }
      if (url.endsWith('/api/tenants/acme/secrets') && method === 'POST') {
        return jsonResponse(createdSecret, 201);
      }
      if (url.endsWith('/api/tenants/acme/secrets')) {
        return jsonResponse({ concurrencyToken: 'c1', secrets: [] });
      }
      throw new Error(`Unexpected ${method} ${url}`);
    });

    renderPage();
    fireEvent.click(await screen.findByTestId('create-secret'));
    // The form offers the secret types once the capabilities are discovered.
    await waitFor(() => expect(screen.getByTestId('secret-type')).toHaveValue('symmetric'));
    fireEvent.click(screen.getByTestId('secret-form-submit'));

    expect(await screen.findByTestId('reveal-secret-material')).toHaveTextContent(
      'one-time-material',
    );
  });

  it('saves edited tenant settings as a JSON Patch diff on the settings tab', async () => {
    const calls = installFetch((url, method) => {
      if (url.endsWith('/api/tenants/acme/secrets')) {
        return jsonResponse({ concurrencyToken: 'c1', secrets: [] });
      }
      if (url.endsWith('/api/tenants/acme/settings') && method === 'PATCH') {
        return new Response(null, { status: 204 });
      }
      if (url.endsWith('/api/tenants/acme/settings')) {
        return jsonResponse({ concurrencyToken: 'sc9', settings: { a: 1 } });
      }
      if (url.endsWith('/settings/limits')) {
        return jsonResponse({ names: [], canChange: true });
      }
      if (url.endsWith('/api/tenants/acme/effective-settings')) {
        return jsonResponse({ settings: { a: 1, issuer: 'https://op.test' } });
      }
      throw new Error(`Unexpected ${method} ${url}`);
    });

    renderPage('settings');

    const editor = await screen.findByTestId('settings-json');
    fireEvent.change(editor, { target: { value: '{"a": 2}' } });
    fireEvent.click(screen.getByTestId('settings-save'));

    await waitFor(() => {
      const patch = calls.find((c) => c.method === 'PATCH');
      expect(patch?.url).toBe('https://api.test/api/tenants/acme/settings');
      expect(JSON.parse(patch?.body ?? '[]')).toEqual([{ op: 'replace', path: '/a', value: 2 }]);
    });
  });

  it("lists the tenant's administrators and owners on the access surface", async () => {
    installFetch((url, method) => {
      if (url.endsWith('/api/tenants/acme/admins')) {
        return jsonResponse({
          items: [{ assignmentId: 'a-alice', principalId: 'alice', roleName: 'TenantAdmin' }],
        });
      }
      if (url.endsWith('/api/tenants/acme/owners')) {
        return jsonResponse({
          items: [{ assignmentId: 'a-bob', principalId: 'bob', roleName: 'Owner' }],
        });
      }
      throw new Error(`Unexpected ${method} ${url}`);
    });

    renderPage('access');

    expect(await screen.findByTestId('admins-row-alice')).toBeInTheDocument();
    expect(await screen.findByTestId('owners-row-bob')).toBeInTheDocument();
  });

  it('shows the tenant usage ledger on the usage tab and narrows it to a chosen range', async () => {
    const calls = installFetch((url, method) => {
      if (url.endsWith('/api/capabilities')) {
        return jsonResponse({ secretTypes: [] });
      }
      if (url.endsWith('/api/tenants/acme/secrets')) {
        return jsonResponse({ concurrencyToken: 'c1', secrets: [] });
      }
      if (url.includes('/api/tenants/acme/usage?from=2026-10-21&to=2026-10-21')) {
        return jsonResponse({
          tenantId: 'acme',
          from: '2026-10-21',
          to: '2026-10-21',
          entries: [{ day: '2026-10-21', meter: 'requests', count: 3 }],
        });
      }
      if (url.includes('/api/tenants/acme/usage?from=2026-10-21')) {
        return jsonResponse({
          tenantId: 'acme',
          from: '2026-10-21',
          to: '2026-10-21',
          entries: [],
        });
      }
      if (url.endsWith('/api/tenants/acme/usage')) {
        return jsonResponse({
          tenantId: 'acme',
          from: '2026-09-21',
          to: '2026-10-21',
          entries: [
            { day: '2026-10-21', meter: 'requests', count: 42 },
            { day: '2026-10-21', meter: 'token_requests', count: 7 },
          ],
        });
      }
      throw new Error(`Unexpected ${method} ${url}`);
    });

    renderPage('usage');

    expect(await screen.findByTestId('usage-row-2026-10-21')).toHaveTextContent('2026-10-21427');
    expect(screen.getByTestId('usage-range')).toHaveTextContent('2026-09-21 to 2026-10-21');

    fireEvent.change(screen.getByTestId('usage-from'), { target: { value: '2026-10-21' } });

    expect(await screen.findByTestId('data-empty')).toHaveTextContent(
      'No usage recorded in this range.',
    );
    expect(
      calls.some((c) => c.url === 'https://api.test/api/tenants/acme/usage?from=2026-10-21'),
    ).toBe(true);

    fireEvent.change(screen.getByTestId('usage-to'), { target: { value: '2026-10-21' } });

    expect(await screen.findByTestId('usage-row-2026-10-21')).toHaveTextContent('2026-10-213');
    expect(
      calls.some(
        (c) => c.url === 'https://api.test/api/tenants/acme/usage?from=2026-10-21&to=2026-10-21',
      ),
    ).toBe(true);
  });

  it('shows the server refusal of an invalid usage range', async () => {
    installFetch((url, method) => {
      if (url.endsWith('/api/capabilities')) {
        return jsonResponse({ secretTypes: [] });
      }
      if (url.endsWith('/api/tenants/acme/secrets')) {
        return jsonResponse({ concurrencyToken: 'c1', secrets: [] });
      }
      if (url.includes('/api/tenants/acme/usage')) {
        return jsonResponse(
          {
            title: 'One or more validation errors occurred.',
            status: 400,
            errors: {
              from: ["The range must run forward from 'from' to 'to' and span at most 366 days."],
            },
          },
          400,
        );
      }
      throw new Error(`Unexpected ${method} ${url}`);
    });

    renderPage('usage');

    expect(await screen.findByTestId('data-error')).toBeInTheDocument();
  });

  it('shows the tenant encryption keys on the encryption-keys surface', async () => {
    installFetch((url, method) => {
      if (url.endsWith('/api/tenants/acme/encryption-keys')) {
        return jsonResponse({
          recoveryConfigured: true,
          rootKey: null,
          rootKeyProviders: ['aws-kms'],
          keys: [
            {
              keyId: 'k-1',
              version: 1,
              state: 'active',
              createdWhen: '2026-10-01T09:00:00Z',
              retiredWhen: null,
              rootKeyProvider: 'local',
              rootKeyId: 'root-1',
              recoveryKeyId: 'recovery-1',
              rootKeyReference: null,
            },
          ],
        });
      }
      throw new Error(`Unexpected ${method} ${url}`);
    });

    renderPage('encryption-keys');

    expect(await screen.findByTestId('encryption-key-row-k-1')).toBeInTheDocument();
    expect(screen.getByTestId('encryption-keys-rekey')).toBeInTheDocument();
  });

  describe('Move action', () => {
    const cells = [
      { name: 'eu-1', region: 'eu', kind: 'shared', isControlPlane: true },
      { name: 'eu-2', region: 'eu', kind: 'shared', isControlPlane: false },
      { name: 'us-1', region: 'us', kind: 'shared', isControlPlane: false },
    ];

    /**
     * Stubs the console's reads and the four calls of a move; `failing` is the URL suffix of a call that fails;
     * `placedCell` makes the export answer `409` naming the cell the directory places the tenant in.
     */
    function installMoveFetch(options: {
      readonly isPlatformAdministrator?: boolean;
      readonly isDisabled?: boolean;
      readonly failing?: string;
      readonly placedCell?: string;
    }) {
      return installFetch((url, method) => {
        if (url.endsWith('/api/me')) {
          return jsonResponse({
            principalId: 'root',
            isPlatformAdministrator: options.isPlatformAdministrator ?? true,
            assignments: [],
          });
        }
        if (url.endsWith('/api/cells')) {
          return jsonResponse(cells);
        }
        if (url.endsWith('/cells/eu-1/api/tenants/acme') && method === 'GET') {
          return jsonResponse({ tenantId: 'acme', isDisabled: options.isDisabled ?? true });
        }
        if (
          options.placedCell !== undefined &&
          url.endsWith('/cells/eu-1/api/tenants/acme/export')
        ) {
          return jsonResponse(
            {
              title: 'Conflict',
              detail: 'The tenant is placed in another cell.',
              placedCell: options.placedCell,
            },
            409,
          );
        }
        if (options.failing !== undefined && url.endsWith(options.failing)) {
          return jsonResponse({ title: 'Conflict', detail: 'The cell refused it.' }, 409);
        }
        if (url.endsWith('/cells/eu-2/api/tenant-imports/transfer-keys')) {
          return jsonResponse({ kid: 'k-1', jwk: { kty: 'EC', crv: 'P-256' } });
        }
        if (url.endsWith('/cells/eu-1/api/tenants/acme/export')) {
          return jsonResponse({ package: 'sealed' });
        }
        if (url.endsWith('/cells/eu-2/api/tenant-imports')) {
          return jsonResponse({ tenantId: 'acme', sourceCell: 'eu-1' });
        }
        if (url.endsWith('/cells/eu-1/api/tenants/acme/release-moved')) {
          return new Response(null, { status: 204 });
        }
        if (url.endsWith('/api/tenants/acme/secrets')) {
          return jsonResponse({ concurrencyToken: 'c1', secrets: [] });
        }
        if (url.endsWith('/api/capabilities')) {
          return jsonResponse({ secretTypes: [] });
        }
        throw new Error(`Unexpected ${method} ${url}`);
      });
    }

    const moves = (calls: readonly Call[]) =>
      calls.filter((c) => c.method === 'POST').map((c) => `${c.method} ${c.url}`);

    it('moves a disabled tenant through the four calls, each to its cell and in order', async () => {
      const calls = installMoveFetch({});
      renderPage('secrets', 'eu-1');

      await waitFor(() => expect(screen.getByTestId('move-tenant')).toBeEnabled());
      fireEvent.click(screen.getByTestId('move-tenant'));
      await waitFor(() => expect(screen.getByTestId('move-target')).toBeEnabled());
      fireEvent.change(screen.getByTestId('move-target'), { target: { value: 'eu-2' } });
      fireEvent.click(screen.getByTestId('move-confirm'));

      expect(await screen.findByTestId('move-done')).toHaveTextContent('Moved to eu-2.');
      expect(moves(calls)).toEqual([
        'POST https://api.test/cells/eu-2/api/tenant-imports/transfer-keys',
        'POST https://api.test/cells/eu-1/api/tenants/acme/export',
        'POST https://api.test/cells/eu-2/api/tenant-imports',
        'POST https://api.test/cells/eu-1/api/tenants/acme/release-moved',
      ]);
      const posts = calls.filter((c) => c.method === 'POST');
      expect(JSON.parse(posts[1]?.body ?? '{}')).toEqual({
        jwk: { kty: 'EC', crv: 'P-256' },
        targetCell: 'eu-2',
      });
      expect(JSON.parse(posts[2]?.body ?? '{}')).toEqual({ kid: 'k-1', package: 'sealed' });
    });

    it('offers the other cells of the tenant region', async () => {
      installMoveFetch({});
      renderPage('secrets', 'eu-1');

      await waitFor(() => expect(screen.getByTestId('move-tenant')).toBeEnabled());
      fireEvent.click(screen.getByTestId('move-tenant'));
      await waitFor(() => expect(screen.getByTestId('move-target')).toBeEnabled());
      fireEvent.mouseDown(screen.getByRole('combobox', { name: /target cell/i }));

      expect(screen.getAllByRole('option').map((option) => option.textContent)).toEqual(['eu-2']);
    });

    it('closes the move dialog without a call when cancelled', async () => {
      const calls = installMoveFetch({});
      renderPage('secrets', 'eu-1');

      await waitFor(() => expect(screen.getByTestId('move-tenant')).toBeEnabled());
      fireEvent.click(screen.getByTestId('move-tenant'));
      await waitFor(() => expect(screen.getByTestId('move-target')).toBeEnabled());
      fireEvent.click(screen.getByTestId('move-cancel'));

      await waitFor(() => expect(screen.queryByTestId('move-confirm')).not.toBeInTheDocument());
      expect(moves(calls)).toEqual([]);
    });

    it('reports the failed stage and what remains', async () => {
      const calls = installMoveFetch({ failing: '/cells/eu-1/api/tenants/acme/export' });
      renderPage('secrets', 'eu-1');

      await waitFor(() => expect(screen.getByTestId('move-tenant')).toBeEnabled());
      fireEvent.click(screen.getByTestId('move-tenant'));
      await waitFor(() => expect(screen.getByTestId('move-target')).toBeEnabled());
      fireEvent.change(screen.getByTestId('move-target'), { target: { value: 'eu-2' } });
      fireEvent.click(screen.getByTestId('move-confirm'));

      expect(await screen.findByTestId('move-failure')).toHaveTextContent(
        'Export the tenant from eu-1 failed: The cell refused it.',
      );
      expect(screen.getByTestId('move-remaining')).toHaveTextContent(
        'Remaining: Export the tenant from eu-1; Import the tenant into eu-2; Release the tenant at eu-1.',
      );
      expect(moves(calls)).toEqual([
        'POST https://api.test/cells/eu-2/api/tenant-imports/transfer-keys',
        'POST https://api.test/cells/eu-1/api/tenants/acme/export',
      ]);
    });

    it('offers the release, and runs only the release, when the export finds the tenant placed in another cell', async () => {
      const calls = installMoveFetch({ placedCell: 'eu-2' });
      renderPage('secrets', 'eu-1');

      await waitFor(() => expect(screen.getByTestId('move-tenant')).toBeEnabled());
      fireEvent.click(screen.getByTestId('move-tenant'));
      await waitFor(() => expect(screen.getByTestId('move-target')).toBeEnabled());
      fireEvent.change(screen.getByTestId('move-target'), { target: { value: 'eu-2' } });
      fireEvent.click(screen.getByTestId('move-confirm'));

      expect(await screen.findByTestId('move-failure')).toHaveTextContent(
        'The global directory already places the tenant in eu-2; release this copy at eu-1.',
      );
      fireEvent.click(screen.getByTestId('move-release'));

      expect(await screen.findByTestId('move-done')).toHaveTextContent('Moved to eu-2.');
      expect(moves(calls)).toEqual([
        'POST https://api.test/cells/eu-2/api/tenant-imports/transfer-keys',
        'POST https://api.test/cells/eu-1/api/tenants/acme/export',
        'POST https://api.test/cells/eu-1/api/tenants/acme/release-moved',
      ]);
    });

    it('disables the action while the tenant is enabled', async () => {
      const calls = installMoveFetch({ isDisabled: false });
      renderPage('secrets', 'eu-1');

      const action = await screen.findByTestId('move-tenant');
      await waitFor(() =>
        expect(calls.some((c) => c.url.endsWith('/cells/eu-1/api/tenants/acme'))).toBe(true),
      );
      await waitFor(() => expect(action).toBeDisabled());
      fireEvent.click(action);

      expect(screen.queryByTestId('move-confirm')).not.toBeInTheDocument();
    });

    it('is not shown to a caller who is not a platform administrator', async () => {
      const calls = installMoveFetch({ isPlatformAdministrator: false });
      renderPage('secrets', 'eu-1');

      await waitFor(() => expect(calls.some((c) => c.url.endsWith('/api/me'))).toBe(true));
      expect(await screen.findByTestId('data-empty')).toBeInTheDocument();

      expect(screen.queryByTestId('move-tenant')).not.toBeInTheDocument();
      expect(calls.some((c) => c.url.endsWith('/api/cells'))).toBe(false);
    });

    it('is not shown for a tenant that lives in no named cell', async () => {
      const calls = installMoveFetch({});
      renderPage('secrets');

      expect(await screen.findByTestId('data-empty')).toBeInTheDocument();
      await waitFor(() => expect(calls.some((c) => c.url.endsWith('/api/me'))).toBe(true));

      expect(screen.queryByTestId('move-tenant')).not.toBeInTheDocument();
    });
  });
});
