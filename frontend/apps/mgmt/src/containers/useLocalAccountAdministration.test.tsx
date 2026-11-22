import { afterEach, describe, expect, it, beforeEach, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { Provider } from 'react-redux';
import { createAppStore, type LocalAccountOwner } from '@ncodegroup/identity-store';
import type { BffRuntimeConfig } from '@ncodegroup/identity-config';
import type { LocalAccountResource } from '@ncodegroup/identity-api';
import type { LocalAccountFormValues } from '@ncodegroup/identity-ui';
import { useLocalAccountAdministration } from './useLocalAccountAdministration.js';

const runtime: BffRuntimeConfig = {
  kind: 'bff',
  apiBaseUrl: 'https://api.test',
  bff: { basePath: '/bff', antiforgeryHeader: { name: 'X-CSRF', value: '1' } },
  features: {},
};

const ada: LocalAccountResource = {
  tenantId: 'control',
  localAccountId: 'ada',
  principalId: 'principal-ada',
  userName: 'ada',
  displayName: 'Ada',
  email: 'ada@example.test',
  emailVerified: true,
  isEnabled: true,
  concurrencyToken: 'c1',
};

const grace: LocalAccountResource = {
  ...ada,
  localAccountId: 'grace',
  userName: 'grace',
  email: null,
  isEnabled: false,
};

const accounts: readonly LocalAccountResource[] = [ada, grace];

const values: LocalAccountFormValues = {
  displayName: 'New',
  userName: 'new',
  email: '',
  emailVerified: false,
  password: 'pw',
  confirmPassword: 'pw',
};

function HookHarness({ owner }: { readonly owner: LocalAccountOwner }) {
  const admin = useLocalAccountAdministration({ owner, accounts });
  return (
    <div>
      <button onClick={admin.openCreate}>open-create</button>
      <button onClick={() => admin.openEdit('ada')}>open-edit</button>
      <button onClick={() => admin.openEdit('missing')}>open-edit-missing</button>
      <button onClick={() => admin.openPassword('ada')}>open-password</button>
      <button onClick={() => admin.openPassword('missing')}>open-password-missing</button>
      <button onClick={() => admin.openDelete('ada')}>open-delete</button>
      <button onClick={() => admin.openDelete('missing')}>open-delete-missing</button>
      <button onClick={() => void admin.toggleEnabled('ada')}>toggle-ada</button>
      <button onClick={() => void admin.toggleEnabled('grace')}>toggle-grace</button>
      <button onClick={() => void admin.toggleEnabled('missing')}>toggle-missing</button>
      <button onClick={admin.close}>close</button>
      <button onClick={() => void admin.handleSubmit(values)}>submit</button>
      <button onClick={() => void admin.handleSubmit({ ...values, email: 'a@b.test' })}>
        submit-email
      </button>
      <button onClick={() => void admin.handlePassword('secret')}>reset</button>
      <button onClick={() => void admin.handleDelete()}>delete</button>
      <output data-testid="dialog">{admin.dialog.kind}</output>
      <output data-testid="form-error">{admin.formError ?? ''}</output>
      <output data-testid="password-error">{admin.passwordError ?? ''}</output>
      <output data-testid="delete-error">{admin.deleteError ?? ''}</output>
      <output data-testid="toggle-error">{admin.toggleError ?? ''}</output>
      <output data-testid="submitting">{String(admin.isSubmitting)}</output>
    </div>
  );
}

const platform: LocalAccountOwner = { kind: 'platform' };

function renderHarness(owner: LocalAccountOwner = platform) {
  const store = createAppStore({ config: { runtime } });
  return render(
    <Provider store={store}>
      <HookHarness owner={owner} />
    </Provider>,
  );
}

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

function click(name: string) {
  fireEvent.click(screen.getByRole('button', { name }));
}

const dialogIs = (kind: string) => expect(screen.getByTestId('dialog')).toHaveTextContent(kind);

describe('useLocalAccountAdministration', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('creates a platform account, omitting a blank email, and closes the dialog', async () => {
    const calls = installFetch(() => jsonResponse(ada, 201));
    renderHarness();

    click('open-create');
    dialogIs('create');
    click('submit');

    await waitFor(() => dialogIs('none'));
    expect(calls).toHaveLength(1);
    expect(calls[0]).toMatchObject({
      url: 'https://api.test/api/platform/local-accounts',
      method: 'POST',
    });
    expect(JSON.parse(calls[0]!.body)).toEqual({
      displayName: 'New',
      userName: 'new',
      password: 'pw',
      emailVerified: false,
    });
  });

  it('sends a given email on create', async () => {
    const calls = installFetch(() => jsonResponse(ada, 201));
    renderHarness();

    click('open-create');
    click('submit-email');

    await waitFor(() => dialogIs('none'));
    expect(JSON.parse(calls[0]!.body)).toMatchObject({ email: 'a@b.test' });
  });

  it('keeps the form open on a refused create and clears the error when it reopens', async () => {
    installFetch(() => jsonResponse({ title: 'Conflict' }, 409));
    renderHarness();

    click('open-create');
    click('submit');

    await waitFor(() => expect(screen.getByTestId('form-error')).not.toBeEmptyDOMElement());
    dialogIs('create');
    expect(screen.getByTestId('submitting')).toHaveTextContent('false');

    click('close');
    click('open-create');
    expect(screen.getByTestId('form-error')).toBeEmptyDOMElement();
  });

  it('edits an account through its own tenant with its concurrency token', async () => {
    const calls = installFetch(() => new Response(null, { status: 204 }));
    renderHarness({ kind: 'tenant', tenantId: 'acme', cell: 'eu' });

    click('open-edit');
    dialogIs('edit');
    click('submit');

    await waitFor(() => dialogIs('none'));
    expect(calls).toHaveLength(1);
    expect(calls[0]).toMatchObject({ method: 'PATCH', ifMatch: 'c1' });
    expect(calls[0]!.url).toContain('/api/tenants/control/local-accounts/ada');
    expect(JSON.parse(calls[0]!.body)).toEqual([
      { op: 'replace', path: '/displayName', value: 'New' },
      { op: 'replace', path: '/userName', value: 'new' },
      { op: 'replace', path: '/email', value: null },
      { op: 'replace', path: '/emailVerified', value: false },
    ]);
  });

  it('reports a refused edit and keeps the form open', async () => {
    installFetch(() => jsonResponse({ title: 'Precondition failed' }, 412));
    renderHarness();

    click('open-edit');
    click('submit-email');

    await waitFor(() => expect(screen.getByTestId('form-error')).not.toBeEmptyDOMElement());
    dialogIs('edit');
  });

  it('submits nothing when no form is open', async () => {
    const calls = installFetch(() => {
      throw new Error('Unexpected request');
    });
    renderHarness();

    click('submit');

    await waitFor(() => expect(screen.getByTestId('submitting')).toHaveTextContent('false'));
    expect(calls).toHaveLength(0);
  });

  it('ignores an action on an account that is not listed', () => {
    const calls = installFetch(() => {
      throw new Error('Unexpected request');
    });
    renderHarness();

    click('open-edit-missing');
    dialogIs('none');
    click('open-password-missing');
    dialogIs('none');
    click('open-delete-missing');
    dialogIs('none');
    click('toggle-missing');

    expect(calls).toHaveLength(0);
  });

  it('resets a password and closes the dialog', async () => {
    const calls = installFetch(() => new Response(null, { status: 204 }));
    renderHarness();

    click('open-password');
    dialogIs('password');
    click('reset');

    await waitFor(() => dialogIs('none'));
    expect(calls[0]).toMatchObject({ method: 'POST' });
    expect(calls[0]!.url).toContain('/api/tenants/control/local-accounts/ada/password');
    expect(JSON.parse(calls[0]!.body)).toEqual({ password: 'secret' });
  });

  it('reports a refused password reset and clears the error when it reopens', async () => {
    installFetch(() => jsonResponse({ title: 'Weak' }, 400));
    renderHarness();

    click('open-password');
    click('reset');

    await waitFor(() => expect(screen.getByTestId('password-error')).not.toBeEmptyDOMElement());
    dialogIs('password');

    click('close');
    click('open-password');
    expect(screen.getByTestId('password-error')).toBeEmptyDOMElement();
  });

  it('does nothing when asked to reset or delete with no such dialog open', () => {
    const calls = installFetch(() => {
      throw new Error('Unexpected request');
    });
    renderHarness();

    click('reset');
    click('delete');

    expect(calls).toHaveLength(0);
  });

  it('deletes an account through its own tenant', async () => {
    const calls = installFetch(() => new Response(null, { status: 204 }));
    renderHarness();

    click('open-delete');
    dialogIs('delete');
    click('delete');

    await waitFor(() => dialogIs('none'));
    expect(calls[0]).toMatchObject({ method: 'DELETE' });
    expect(calls[0]!.url).toContain('/api/tenants/control/local-accounts/ada');
  });

  it('reports a refused delete and clears the error when it reopens', async () => {
    installFetch(() => jsonResponse({ title: 'Forbidden' }, 403));
    renderHarness();

    click('open-delete');
    click('delete');

    await waitFor(() => expect(screen.getByTestId('delete-error')).not.toBeEmptyDOMElement());
    dialogIs('delete');

    click('close');
    click('open-delete');
    expect(screen.getByTestId('delete-error')).toBeEmptyDOMElement();
  });

  it('disables an enabled account and enables a disabled one', async () => {
    const calls = installFetch(() => new Response(null, { status: 204 }));
    renderHarness();

    click('toggle-ada');
    await waitFor(() => expect(calls).toHaveLength(1));
    expect(calls[0]!.url).toContain('/local-accounts/ada/disable');

    click('toggle-grace');
    await waitFor(() => expect(calls).toHaveLength(2));
    expect(calls[1]!.url).toContain('/local-accounts/grace/enable');
  });

  it('reports a refused toggle', async () => {
    installFetch(() => jsonResponse({ title: 'Forbidden' }, 403));
    renderHarness();

    click('toggle-ada');

    await waitFor(() => expect(screen.getByTestId('toggle-error')).not.toBeEmptyDOMElement());
  });
});
