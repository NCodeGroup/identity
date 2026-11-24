import { afterEach, describe, expect, it, beforeEach, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { Provider } from 'react-redux';
import { createAppStore } from '@ncodegroup/identity-store';
import type { BffRuntimeConfig } from '@ncodegroup/identity-config';
import { UsersPage, type UsersPageProps } from './UsersPage.js';

const runtime: BffRuntimeConfig = {
  kind: 'bff',
  apiBaseUrl: 'https://api.test',
  bff: { basePath: '/bff', antiforgeryHeader: { name: 'X-CSRF', value: '1' } },
  features: {},
};

const platform: UsersPageProps['owner'] = { kind: 'platform' };
const tenant: UsersPageProps['owner'] = { kind: 'tenant', tenantId: 'acme' };

function renderPage(owner: UsersPageProps['owner'] = tenant) {
  const store = createAppStore({ config: { runtime } });
  return render(
    <Provider store={store}>
      <UsersPage owner={owner} />
    </Provider>,
  );
}

const localConnection = {
  tenantId: 'acme',
  connectionId: 'conn-1',
  concurrencyToken: 'k1',
  name: 'local',
  displayName: 'Username and password',
  strategy: 'local',
  isEnabled: true,
  options: { store: 'default' },
};

const ada = {
  tenantId: 'acme',
  connectionId: 'conn-1',
  localAccountId: 'a1',
  principalId: 'p1',
  userName: 'ada',
  displayName: 'Ada Lovelace',
  email: 'ada@example.test',
  emailVerified: true,
  isEnabled: true,
  concurrencyToken: 'c1',
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

function installFetch(
  handler: (url: string, method: string) => Response,
  connections: readonly (typeof localConnection)[] = [localConnection],
) {
  const calls: Call[] = [];
  const inner = handler;
  handler = (url, method) =>
    url.endsWith(connectionsUrl) ? jsonResponse({ items: connections }) : inner(url, method);
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

const connectionsUrl = '/api/tenants/acme/connections';
const listUrl = '/api/tenants/acme/connections/conn-1/local-accounts';
const itemUrl = '/api/tenants/acme/connections/conn-1/local-accounts/a1';

function typeInto(testId: string, value: string) {
  fireEvent.change(screen.getByTestId(testId), { target: { value } });
}

describe('UsersPage (list states)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('shows a loading indicator, then the empty state', async () => {
    installFetch(() => jsonResponse({ items: [] }));

    renderPage();

    expect(screen.getByTestId('data-loading')).toBeInTheDocument();
    expect(await screen.findByTestId('data-empty')).toHaveTextContent('No users yet.');
    expect(screen.getByRole('heading', { name: 'Users' })).toBeInTheDocument();
    expect(screen.getByText(/Local accounts of this tenant\./)).toBeInTheDocument();
  });

  it('describes the platform owner', async () => {
    installFetch(() => jsonResponse({ items: [] }));

    renderPage(platform);

    expect(await screen.findByTestId('data-empty')).toBeInTheDocument();
    expect(screen.getByText(/Local accounts of the platform\./)).toBeInTheDocument();
  });

  it('reports a failure to load the users', async () => {
    installFetch(() => jsonResponse({ title: 'Forbidden' }, 403));

    renderPage();

    expect(await screen.findByTestId('data-error')).toHaveTextContent('Failed to load users.');
  });

  it('lists the accounts as rows', async () => {
    installFetch(() => jsonResponse({ items: [ada] }));

    renderPage();

    expect(await screen.findByTestId('local-account-row-a1')).toHaveTextContent('Ada Lovelace');
    expect(screen.getByTestId('local-account-row-a1')).toHaveTextContent('ada@example.test');
  });

  it('lists the accounts of the connection picked in the connection select', async () => {
    const staff = {
      ...localConnection,
      connectionId: 'conn-2',
      name: 'staff',
      displayName: 'Staff directory',
    };
    const grace = { ...ada, connectionId: 'conn-2', localAccountId: 'a2', displayName: 'Grace' };
    const calls = installFetch(
      (url) =>
        url.endsWith('/connections/conn-2/local-accounts')
          ? jsonResponse({ items: [grace] })
          : jsonResponse({ items: [ada] }),
      [localConnection, staff],
    );

    renderPage();
    expect(await screen.findByTestId('local-account-row-a1')).toBeInTheDocument();
    expect(screen.getByTestId('users-connection')).toHaveValue('conn-1');

    typeInto('users-connection', 'conn-2');

    expect(await screen.findByTestId('local-account-row-a2')).toHaveTextContent('Grace');
    expect(calls.some((c) => c.url.endsWith('/connections/conn-2/local-accounts'))).toBe(true);
  });
});

describe('UsersPage (drill-in)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('opens a user', async () => {
    installFetch((url) => {
      if (url.endsWith(listUrl)) {
        return jsonResponse({ items: [ada] });
      }
      if (url.endsWith('/claims')) {
        return jsonResponse({
          tenantId: 'acme',
          localAccountId: 'a1',
          claims: [],
          concurrencyToken: 'cl1',
        });
      }
      if (url.endsWith('/metadata')) {
        return jsonResponse({
          tenantId: 'acme',
          localAccountId: 'a1',
          principalId: 'p1',
          profileMetadata: null,
          systemMetadata: null,
          concurrencyToken: 'm1',
        });
      }
      throw new Error(`Unexpected ${url}`);
    });

    renderPage();
    fireEvent.click(await screen.findByTestId('local-account-row-a1'));

    expect(await screen.findByTestId('local-account-detail-back')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Ada Lovelace' })).toBeInTheDocument();
    expect(screen.queryByTestId('create-local-account')).not.toBeInTheDocument();

    fireEvent.click(screen.getByTestId('local-account-detail-back'));

    expect(await screen.findByTestId('local-account-row-a1')).toBeInTheDocument();
    expect(screen.getByTestId('create-local-account')).toBeInTheDocument();
  });
});

describe('UsersPage (paging)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('loads the next page', async () => {
    const grace = { ...ada, localAccountId: 'a2', userName: 'grace', displayName: 'Grace Hopper' };
    const calls = installFetch((url, method) => {
      if (url.includes(listUrl) && method === 'GET') {
        return url.includes('cursor=next')
          ? jsonResponse({ items: [grace] })
          : jsonResponse({ items: [ada], continuationToken: 'next' });
      }
      throw new Error(`Unexpected ${method} ${url}`);
    });

    renderPage();
    fireEvent.click(await screen.findByTestId('load-more'));

    expect(await screen.findByTestId('local-account-row-a2')).toBeInTheDocument();
    expect(screen.getByTestId('local-account-row-a1')).toBeInTheDocument();
    expect(calls.some((c) => c.url.includes('cursor=next'))).toBe(true);
    await waitFor(() => expect(screen.queryByTestId('load-more')).not.toBeInTheDocument());
  });
});

describe('UsersPage (administration wiring)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  function fillCreateForm() {
    typeInto('local-account-display-name', 'Grace Hopper');
    typeInto('local-account-user-name', 'grace');
    typeInto('local-account-password', 'pw-1');
    typeInto('local-account-confirm-password', 'pw-1');
    fireEvent.click(screen.getByTestId('local-account-form-submit'));
  }

  it('creates a tenant user under the tenant path', async () => {
    const calls = installFetch((url, method) => {
      if (url.endsWith(listUrl) && method === 'POST') {
        return jsonResponse({ ...ada, localAccountId: 'a2' }, 201);
      }
      if (url.endsWith(listUrl)) {
        return jsonResponse({ items: [ada] });
      }
      throw new Error(`Unexpected ${method} ${url}`);
    });

    renderPage();
    await screen.findByTestId('local-account-row-a1');
    fireEvent.click(screen.getByTestId('create-local-account'));
    fillCreateForm();

    await waitFor(() =>
      expect(screen.queryByTestId('local-account-form-submit')).not.toBeInTheDocument(),
    );
    const post = calls.find((c) => c.method === 'POST');
    expect(post?.url).toBe(`https://api.test${listUrl}`);
    expect(JSON.parse(post?.body ?? '{}')).toEqual({
      displayName: 'Grace Hopper',
      userName: 'grace',
      password: 'pw-1',
      emailVerified: false,
    });
  });

  it('creates a platform user under the platform path', async () => {
    const calls = installFetch((url, method) => {
      if (url.endsWith('/api/platform/local-accounts') && method === 'POST') {
        return jsonResponse(ada, 201);
      }
      if (url.endsWith('/api/platform/local-accounts')) {
        return jsonResponse({ items: [] });
      }
      throw new Error(`Unexpected ${method} ${url}`);
    });

    renderPage(platform);
    fireEvent.click(await screen.findByTestId('create-local-account'));
    fillCreateForm();

    await waitFor(() =>
      expect(screen.queryByTestId('local-account-form-submit')).not.toBeInTheDocument(),
    );
    expect(calls.find((c) => c.method === 'POST')?.url).toBe(
      'https://api.test/api/platform/local-accounts',
    );
  });

  it('edits a user with an If-Match JSON Patch and prefills the form without a password', async () => {
    const calls = installFetch((url, method) => {
      if (url.endsWith(itemUrl) && method === 'PATCH') {
        return new Response(null, { status: 204 });
      }
      if (url.endsWith(listUrl)) {
        return jsonResponse({ items: [ada] });
      }
      throw new Error(`Unexpected ${method} ${url}`);
    });

    renderPage();
    fireEvent.click(await screen.findByTestId('local-account-edit-a1'));

    expect(screen.getByRole('dialog', { name: 'Edit user' })).toBeInTheDocument();
    expect(screen.getByTestId('local-account-display-name')).toHaveValue('Ada Lovelace');
    expect(screen.queryByTestId('local-account-password')).not.toBeInTheDocument();

    typeInto('local-account-display-name', 'Ada King');
    fireEvent.click(screen.getByTestId('local-account-form-submit'));

    await waitFor(() =>
      expect(screen.queryByTestId('local-account-form-submit')).not.toBeInTheDocument(),
    );
    const patch = calls.find((c) => c.method === 'PATCH');
    expect(patch?.url).toBe(`https://api.test${itemUrl}`);
    expect(patch?.ifMatch).toBe('c1');
    expect(JSON.parse(patch?.body ?? '[]')).toEqual([
      { op: 'replace', path: '/displayName', value: 'Ada King' },
      { op: 'replace', path: '/userName', value: 'ada' },
      { op: 'replace', path: '/email', value: 'ada@example.test' },
      { op: 'replace', path: '/emailVerified', value: true },
    ]);
  });

  it('disables an enabled user', async () => {
    const calls = installFetch((url, method) => {
      if (url.endsWith(`${itemUrl}/disable`) && method === 'POST') {
        return new Response(null, { status: 204 });
      }
      if (url.endsWith(listUrl)) {
        return jsonResponse({ items: [ada] });
      }
      throw new Error(`Unexpected ${method} ${url}`);
    });

    renderPage();
    fireEvent.click(await screen.findByTestId('local-account-toggle-a1'));

    await waitFor(() =>
      expect(calls.some((c) => c.method === 'POST' && c.url.endsWith(`${itemUrl}/disable`))).toBe(
        true,
      ),
    );
  });

  it('shows the problem when a toggle is rejected', async () => {
    installFetch((url, method) => {
      if (url.endsWith(`${itemUrl}/disable`) && method === 'POST') {
        return jsonResponse({ detail: 'The last administrator cannot be disabled.' }, 409);
      }
      if (url.endsWith(listUrl)) {
        return jsonResponse({ items: [ada] });
      }
      throw new Error(`Unexpected ${method} ${url}`);
    });

    renderPage();
    fireEvent.click(await screen.findByTestId('local-account-toggle-a1'));

    expect(await screen.findByTestId('local-account-toggle-error')).toHaveTextContent(
      'The last administrator cannot be disabled.',
    );
  });

  it('resets a password for the named user', async () => {
    const calls = installFetch((url, method) => {
      if (url.endsWith(`${itemUrl}/password`) && method === 'POST') {
        return new Response(null, { status: 204 });
      }
      if (url.endsWith(listUrl)) {
        return jsonResponse({ items: [ada] });
      }
      throw new Error(`Unexpected ${method} ${url}`);
    });

    renderPage();
    fireEvent.click(await screen.findByTestId('local-account-password-a1'));

    expect(screen.getByRole('dialog', { name: 'Reset password' })).toHaveTextContent(
      'Resetting the password of Ada Lovelace',
    );
    typeInto('local-account-password-input', 'new-pw');
    typeInto('local-account-password-confirm', 'new-pw');
    fireEvent.click(screen.getByTestId('local-account-password-submit'));

    await waitFor(() =>
      expect(screen.queryByTestId('local-account-password-submit')).not.toBeInTheDocument(),
    );
    const post = calls.find((c) => c.url.endsWith('/password'));
    expect(JSON.parse(post?.body ?? '{}')).toEqual({ password: 'new-pw' });
  });

  it('deletes a user after confirmation', async () => {
    const calls = installFetch((url, method) => {
      if (url.endsWith(itemUrl) && method === 'DELETE') {
        return new Response(null, { status: 204 });
      }
      if (url.endsWith(listUrl)) {
        return jsonResponse({ items: [ada] });
      }
      throw new Error(`Unexpected ${method} ${url}`);
    });

    renderPage();
    fireEvent.click(await screen.findByTestId('local-account-delete-a1'));

    expect(screen.getByRole('dialog', { name: 'Delete user' })).toHaveTextContent(
      'Delete Ada Lovelace? This ends their sessions.',
    );
    fireEvent.click(screen.getByTestId('confirm-accept'));

    await waitFor(() =>
      expect(calls.some((c) => c.method === 'DELETE' && c.url.endsWith(itemUrl))).toBe(true),
    );
  });

  it('surfaces a server error in the form and keeps it open', async () => {
    installFetch((url, method) => {
      if (url.endsWith(listUrl) && method === 'POST') {
        return jsonResponse({ detail: 'That username is taken.' }, 409);
      }
      if (url.endsWith(listUrl)) {
        return jsonResponse({ items: [ada] });
      }
      throw new Error(`Unexpected ${method} ${url}`);
    });

    renderPage();
    await screen.findByTestId('local-account-row-a1');
    fireEvent.click(screen.getByTestId('create-local-account'));
    fillCreateForm();

    expect(await screen.findByTestId('local-account-form-error')).toHaveTextContent(
      'That username is taken.',
    );
    expect(screen.getByTestId('local-account-form-submit')).toBeInTheDocument();
  });
});
