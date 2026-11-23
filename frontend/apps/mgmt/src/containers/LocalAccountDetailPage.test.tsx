import { describe, expect, it, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { Provider } from 'react-redux';
import { createAppStore } from '@ncodegroup/identity-store';
import type { LocalAccountResource } from '@ncodegroup/identity-api';
import type { BffRuntimeConfig } from '@ncodegroup/identity-config';
import { LocalAccountDetailPage } from './LocalAccountDetailPage.js';

const runtime: BffRuntimeConfig = {
  kind: 'bff',
  apiBaseUrl: 'https://api.test',
  bff: { basePath: '/bff', antiforgeryHeader: { name: 'X-CSRF', value: '1' } },
  features: {},
};

const account: LocalAccountResource = {
  tenantId: 'acme',
  localAccountId: 'u1',
  principalId: 'principal-u1',
  userName: 'ada',
  displayName: 'Ada Lovelace',
  email: 'ada@example.com',
  emailVerified: true,
  isEnabled: true,
  concurrencyToken: 'a1',
};

function renderPage(overrides: Partial<LocalAccountResource> = {}) {
  const store = createAppStore({ config: { runtime } });
  render(
    <Provider store={store}>
      <LocalAccountDetailPage
        account={{ ...account, ...overrides }}
        cell={undefined}
        onBack={vi.fn()}
      />
    </Provider>,
  );
}

type Call = {
  readonly url: string;
  readonly method: string;
  readonly body: string;
  readonly ifMatch: string | null;
};

function installFetch(handler: (url: string, method: string) => Response) {
  const calls: Call[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const request = input as Request;
      calls.push({
        url: request.url,
        method: request.method,
        body: request.method === 'GET' ? '' : await request.clone().text(),
        ifMatch: request.headers.get('If-Match'),
      });
      return handler(request.url, request.method);
    }),
  );
  return calls;
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  });
}

const claimsResource = {
  tenantId: 'acme',
  localAccountId: 'u1',
  claims: [{ type: 'given_name', value: 'Ada' }],
  concurrencyToken: 'cl1',
};

const metadataResource = {
  tenantId: 'acme',
  localAccountId: 'u1',
  principalId: 'principal-u1',
  profileMetadata: { theme: 'dark' },
  systemMetadata: null,
  concurrencyToken: 'm1',
};

const claimsUrl = '/claims';
const metadataUrl = '/metadata';

describe('LocalAccountDetailPage', () => {
  it('loads and shows the profile claims', async () => {
    installFetch((url) => {
      if (url.endsWith(claimsUrl)) {
        return jsonResponse(claimsResource);
      }
      throw new Error(`Unexpected ${url}`);
    });

    renderPage();

    expect(screen.getByRole('heading', { name: 'Ada Lovelace' })).toBeInTheDocument();
    expect(screen.getByText('ada · ada@example.com')).toBeInTheDocument();
    expect(screen.getByTestId('local-account-principal')).toHaveTextContent(
      'Principal principal-u1',
    );
    expect(await screen.findByTestId('claim-type-0')).toHaveValue('given_name');
    expect(screen.getByTestId('claim-value-0')).toHaveValue('Ada');
  });

  it('saves the claims with If-Match', async () => {
    const calls = installFetch((url, method) => {
      if (url.endsWith(claimsUrl) && method === 'PUT') {
        return jsonResponse({ ...claimsResource, concurrencyToken: 'cl2' });
      }
      if (url.endsWith(claimsUrl)) {
        return jsonResponse(claimsResource);
      }
      throw new Error(`Unexpected ${method} ${url}`);
    });

    renderPage();
    fireEvent.change(await screen.findByTestId('claim-value-0'), { target: { value: 'Augusta' } });
    fireEvent.click(screen.getByTestId('claims-save'));

    await waitFor(() => expect(calls.some((c) => c.method === 'PUT')).toBe(true));
    const put = calls.find((c) => c.method === 'PUT');
    expect(put?.ifMatch).toBe('cl1');
    expect(JSON.parse(put?.body ?? '')).toEqual({
      claims: [{ type: 'given_name', value: 'Augusta' }],
    });
  });

  it('shows the server problem when the save is rejected with 412', async () => {
    installFetch((url, method) => {
      if (url.endsWith(claimsUrl) && method === 'PUT') {
        return jsonResponse({ detail: 'The profile changed since it was loaded.' }, 412);
      }
      if (url.endsWith(claimsUrl)) {
        return jsonResponse(claimsResource);
      }
      throw new Error(`Unexpected ${method} ${url}`);
    });

    renderPage();
    fireEvent.change(await screen.findByTestId('claim-value-0'), { target: { value: 'Augusta' } });
    fireEvent.click(screen.getByTestId('claims-save'));

    expect(await screen.findByTestId('claims-error')).toHaveTextContent(
      'The profile changed since it was loaded.',
    );
  });

  it('hides the metadata tab and makes no metadata request without a principal', async () => {
    const calls = installFetch((url) => {
      if (url.endsWith(claimsUrl)) {
        return jsonResponse(claimsResource);
      }
      throw new Error(`Unexpected ${url}`);
    });

    renderPage({ principalId: null });
    await screen.findByTestId('claim-type-0');

    expect(screen.queryByTestId('local-account-tab-metadata')).not.toBeInTheDocument();
    expect(calls.some((c) => c.url.endsWith(metadataUrl))).toBe(false);
  });

  it('saves both metadata bags with If-Match', async () => {
    const calls = installFetch((url, method) => {
      if (url.endsWith(metadataUrl) && method === 'PATCH') {
        return jsonResponse({ ...metadataResource, concurrencyToken: 'm2' });
      }
      if (url.endsWith(metadataUrl)) {
        return jsonResponse(metadataResource);
      }
      if (url.endsWith(claimsUrl)) {
        return jsonResponse(claimsResource);
      }
      throw new Error(`Unexpected ${method} ${url}`);
    });

    renderPage();
    fireEvent.click(await screen.findByTestId('local-account-tab-metadata'));
    const profile = await screen.findByTestId('metadata-profile');
    expect(profile).toHaveValue(JSON.stringify({ theme: 'dark' }, null, 2));
    expect(screen.getByTestId('metadata-system')).toHaveValue('{}');
    fireEvent.change(profile, { target: { value: '{"theme":"light"}' } });
    fireEvent.click(screen.getByTestId('metadata-save'));

    await waitFor(() => expect(calls.some((c) => c.method === 'PATCH')).toBe(true));
    const patch = calls.find((c) => c.method === 'PATCH');
    expect(patch?.ifMatch).toBe('m1');
    expect(JSON.parse(patch?.body ?? '')).toEqual({
      profileMetadata: { theme: 'light' },
      systemMetadata: {},
    });
  });

  it('shows the server problem when the metadata save fails', async () => {
    installFetch((url, method) => {
      if (url.endsWith(metadataUrl) && method === 'PATCH') {
        return jsonResponse({ detail: 'The metadata changed since it was loaded.' }, 412);
      }
      if (url.endsWith(metadataUrl)) {
        return jsonResponse(metadataResource);
      }
      if (url.endsWith(claimsUrl)) {
        return jsonResponse(claimsResource);
      }
      throw new Error(`Unexpected ${method} ${url}`);
    });

    renderPage();
    fireEvent.click(await screen.findByTestId('local-account-tab-metadata'));
    fireEvent.change(await screen.findByTestId('metadata-profile'), {
      target: { value: '{"theme":"light"}' },
    });
    fireEvent.click(screen.getByTestId('metadata-save'));

    expect(await screen.findByTestId('metadata-error')).toHaveTextContent(
      'The metadata changed since it was loaded.',
    );
  });

  it('shows the username alone when the account has no email', async () => {
    installFetch((url) => {
      if (url.endsWith(claimsUrl)) {
        return jsonResponse(claimsResource);
      }
      throw new Error(`Unexpected ${url}`);
    });

    renderPage({ email: null });
    await screen.findByTestId('claim-type-0');

    expect(screen.getByText('ada')).toBeInTheDocument();
    expect(screen.queryByText(/·/)).not.toBeInTheDocument();
  });

  it('shows an error when the claims fail to load', async () => {
    installFetch(() => jsonResponse({ detail: 'boom' }, 500));

    renderPage();

    expect(await screen.findByText('Failed to load profile claims.')).toBeInTheDocument();
  });

  it('shows an error when the metadata fails to load', async () => {
    installFetch((url) => {
      if (url.endsWith(metadataUrl)) {
        return jsonResponse({ detail: 'boom' }, 500);
      }
      if (url.endsWith(claimsUrl)) {
        return jsonResponse(claimsResource);
      }
      throw new Error(`Unexpected ${url}`);
    });

    renderPage();
    fireEvent.click(await screen.findByTestId('local-account-tab-metadata'));

    expect(await screen.findByText('Failed to load metadata.')).toBeInTheDocument();
  });

  it('seeds both metadata editors with an empty object when the bags are null', async () => {
    installFetch((url) => {
      if (url.endsWith(metadataUrl)) {
        return jsonResponse({ ...metadataResource, profileMetadata: null });
      }
      if (url.endsWith(claimsUrl)) {
        return jsonResponse(claimsResource);
      }
      throw new Error(`Unexpected ${url}`);
    });

    renderPage();
    fireEvent.click(await screen.findByTestId('local-account-tab-metadata'));

    expect(await screen.findByTestId('metadata-profile')).toHaveValue('{}');
    expect(screen.getByTestId('metadata-system')).toHaveValue('{}');
  });
});
