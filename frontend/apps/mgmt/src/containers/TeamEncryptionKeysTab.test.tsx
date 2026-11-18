import { describe, expect, it, beforeEach, vi } from 'vitest';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import { Provider } from 'react-redux';
import { createAppStore } from '@ncodegroup/identity-store';
import type {
  CellResource,
  EncryptionRootKeyResource,
  TeamRootKeyResource,
  TeamRootKeysResource,
} from '@ncodegroup/identity-api';
import type { BffRuntimeConfig } from '@ncodegroup/identity-config';
import { TeamEncryptionKeysTab } from './TeamEncryptionKeysTab.js';

const runtime: BffRuntimeConfig = {
  kind: 'bff',
  apiBaseUrl: 'https://api.test',
  bff: { basePath: '/bff', antiforgeryHeader: { name: 'X-CSRF', value: '1' } },
  features: {},
};

function renderTab() {
  const store = createAppStore({
    config: { runtime },
  });
  return render(
    <Provider store={store}>
      <TeamEncryptionKeysTab teamId="acme" />
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
      body: await request.clone().text(),
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

const cellsUrl = 'https://api.test/api/cells';
const rootKeysUrl = 'https://api.test/api/teams/acme/encryption-keys/root-keys';
const providers = ['aws-kms', 'google-cloud-kms'];

const cells: CellResource[] = [
  { name: 'us-2', region: 'us-east', kind: 'shared', isControlPlane: false },
  { name: 'eu-1', region: 'eu-west', kind: 'shared', isControlPlane: true },
  { name: 'eu-2', region: 'eu-west', kind: 'dedicated', isControlPlane: false },
];

const euKey: TeamRootKeyResource = {
  region: 'eu-west',
  provider: 'aws-kms',
  keyReference: 'arn:aws:kms:eu-west-1:111122223333:key/abc',
  accessIdentity: 'arn:aws:iam::111122223333:role/team',
};

const listing: TeamRootKeysResource = {
  rootKeys: [euKey],
  rootKeyProviders: providers,
  rootKeyAccessBinding: 'team:acme',
};

const removingKey: EncryptionRootKeyResource = {
  provider: 'aws-kms',
  keyReference: euKey.keyReference,
  accessIdentity: euKey.accessIdentity ?? null,
  state: 'removing',
  createdWhen: '2026-10-01T09:00:00Z',
  stateChangedWhen: '2026-10-01T09:00:00Z',
  validatedWhen: null,
  failedWhen: null,
  failure: null,
};

/** Answers the cells and the listing, and every other request through `other`. */
function serve(
  current: () => TeamRootKeysResource,
  other: (url: string, method: string) => Response | undefined = () => undefined,
) {
  return (url: string, method: string) => {
    const answered = other(url, method);
    if (answered) {
      return answered;
    }
    if (url === cellsUrl) {
      return jsonResponse(cells);
    }
    if (url === rootKeysUrl && method === 'GET') {
      return jsonResponse(current());
    }
    throw new Error(`Unexpected ${method} ${url}`);
  };
}

describe('TeamEncryptionKeysTab', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('shows one row per distinct region, sorted by region', async () => {
    installFetch(serve(() => listing));

    renderTab();

    expect(await screen.findByTestId('team-root-key-region-eu-west')).toBeInTheDocument();
    expect(
      screen.getAllByTestId('team-root-key-region').map((heading) => heading.textContent),
    ).toEqual(['eu-west', 'us-east']);
  });

  it("shows a region's key, and says the deployment's root key wraps a region without one", async () => {
    installFetch(serve(() => listing));

    renderTab();

    const eu = within(await screen.findByTestId('team-root-key-region-eu-west'));
    const us = within(await screen.findByTestId('team-root-key-region-us-east'));
    expect(eu.getByTestId('root-key-provider')).toHaveTextContent('aws-kms');
    expect(eu.getByTestId('root-key-reference')).toHaveTextContent(euKey.keyReference);
    expect(eu.getByTestId('root-key-access-identity')).toHaveTextContent(
      'arn:aws:iam::111122223333:role/team',
    );
    expect(eu.queryByTestId('root-key-state')).not.toBeInTheDocument();
    expect(us.getByTestId('root-key-none')).toHaveTextContent(
      "This team's keys are wrapped under the deployment's root key.",
    );
  });

  it('offers no action to a team that may choose no provider', async () => {
    installFetch(serve(() => ({ ...listing, rootKeys: [], rootKeyProviders: [] })));

    renderTab();

    const eu = within(await screen.findByTestId('team-root-key-region-eu-west'));
    expect(eu.getByTestId('root-key-none')).toHaveTextContent(
      'No root key provider is available to this team.',
    );
    expect(screen.queryByTestId('root-key-set')).not.toBeInTheDocument();
  });

  it('says so when the platform has no regions', async () => {
    installFetch((url, method) =>
      url === cellsUrl ? jsonResponse([]) : serve(() => listing)(url, method),
    );

    renderTab();

    expect(await screen.findByTestId('data-empty')).toHaveTextContent(
      'The platform has no regions, so this team has none to set a root key in.',
    );
  });

  it('shows an error when the root keys cannot be loaded', async () => {
    installFetch((url, method) =>
      url === rootKeysUrl
        ? jsonResponse({ title: 'Forbidden', status: 403 }, 403)
        : serve(() => listing)(url, method),
    );

    renderTab();

    expect(await screen.findByTestId('data-error')).toHaveTextContent(
      'Failed to load the encryption keys of this team.',
    );
  });

  it('sets the key of a region from the dialog, seeded from its key, and reloads the keys', async () => {
    let current = listing;
    const calls = installFetch(
      serve(
        () => current,
        (url, method) => {
          if (url === `${rootKeysUrl}/us-east` && method === 'PUT') {
            const key = {
              region: 'us-east',
              provider: 'google-cloud-kms',
              keyReference: 'projects/p/locations/l/keyRings/r/cryptoKeys/k',
            };
            current = { ...listing, rootKeys: [euKey, key] };
            return jsonResponse(key);
          }
          return undefined;
        },
      ),
    );

    renderTab();
    const us = within(await screen.findByTestId('team-root-key-region-us-east'));
    fireEvent.click(await us.findByTestId('root-key-set'));
    expect(screen.getByTestId('root-key-form-reference')).toHaveValue('');
    fireEvent.change(screen.getByTestId('root-key-form-provider'), {
      target: { value: 'google-cloud-kms' },
    });
    fireEvent.change(screen.getByTestId('root-key-form-reference'), {
      target: { value: ' projects/p/locations/l/keyRings/r/cryptoKeys/k ' },
    });
    fireEvent.click(screen.getByTestId('root-key-form-submit'));

    expect(await us.findByTestId('root-key-provider')).toHaveTextContent('google-cloud-kms');
    await waitFor(() =>
      expect(screen.queryByTestId('root-key-form-submit')).not.toBeInTheDocument(),
    );
    const put = calls.find((c) => c.method === 'PUT');
    expect(put?.url).toBe(`${rootKeysUrl}/us-east`);
    expect(JSON.parse(put?.body ?? '{}')).toEqual({
      provider: 'google-cloud-kms',
      keyReference: 'projects/p/locations/l/keyRings/r/cryptoKeys/k',
      accessIdentity: null,
    });
  });

  it("seeds the dialog from the region's key and names the access binding", async () => {
    installFetch(serve(() => listing));

    renderTab();
    const eu = within(await screen.findByTestId('team-root-key-region-eu-west'));
    fireEvent.click(await eu.findByTestId('root-key-set'));

    expect(screen.getByTestId('root-key-form-reference')).toHaveValue(euKey.keyReference);
    expect(screen.getByTestId('root-key-form-access-identity')).toHaveValue(
      'arn:aws:iam::111122223333:role/team',
    );
    expect(screen.getByText(/the external ID team:acme/)).toBeInTheDocument();
    expect(screen.getByText(/Every encryption key of this team is wrapped/)).toBeInTheDocument();
  });

  it('shows the server error in the dialog when setting the key fails', async () => {
    installFetch(
      serve(
        () => listing,
        (_url, method) =>
          method === 'PUT'
            ? jsonResponse(
                {
                  title: 'One or more validation errors occurred.',
                  status: 400,
                  errors: { region: ['No cell serves the region.'] },
                },
                400,
              )
            : undefined,
      ),
    );

    renderTab();
    const us = within(await screen.findByTestId('team-root-key-region-us-east'));
    fireEvent.click(await us.findByTestId('root-key-set'));
    fireEvent.change(screen.getByTestId('root-key-form-reference'), { target: { value: 'arn' } });
    fireEvent.click(screen.getByTestId('root-key-form-submit'));

    expect(await screen.findByTestId('root-key-form-error')).toHaveTextContent(
      'No cell serves the region.',
    );
    expect(screen.getByTestId('root-key-form-submit')).toBeInTheDocument();
  });

  it('validates the key of a region and shows the returned state', async () => {
    const calls = installFetch(
      serve(
        () => listing,
        (url, method) =>
          url === `${rootKeysUrl}/eu-west/validate` && method === 'POST'
            ? jsonResponse({
                ...removingKey,
                state: 'unavailable',
                failure: 'The key was revoked.',
              })
            : undefined,
      ),
    );

    renderTab();
    const eu = within(await screen.findByTestId('team-root-key-region-eu-west'));
    fireEvent.click(await eu.findByTestId('root-key-validate'));

    expect(await eu.findByTestId('root-key-validate-result')).toHaveTextContent(
      'The root key in eu-west is unavailable: The key was revoked.',
    );
    expect(
      calls.some((c) => c.method === 'POST' && c.url === `${rootKeysUrl}/eu-west/validate`),
    ).toBe(true);
  });

  it('shows the active state of a validated key', async () => {
    installFetch(
      serve(
        () => listing,
        (_url, method) =>
          method === 'POST' ? jsonResponse({ ...removingKey, state: 'active' }) : undefined,
      ),
    );

    renderTab();
    const eu = within(await screen.findByTestId('team-root-key-region-eu-west'));
    fireEvent.click(await eu.findByTestId('root-key-validate'));

    expect(await eu.findByTestId('root-key-validate-result')).toHaveTextContent(
      'The root key in eu-west is active.',
    );
  });

  it('reports a failed validation, such as a region another cell validates', async () => {
    installFetch(
      serve(
        () => listing,
        (_url, method) =>
          method === 'POST'
            ? jsonResponse(
                { title: 'Conflict', status: 409, detail: 'The cell in us-east validates it.' },
                409,
              )
            : undefined,
      ),
    );

    renderTab();
    const eu = within(await screen.findByTestId('team-root-key-region-eu-west'));
    fireEvent.click(await eu.findByTestId('root-key-validate'));

    expect(await eu.findByTestId('root-key-validate-error')).toHaveTextContent(
      'The cell in us-east validates it.',
    );
  });

  it('removes the key of a region after confirmation and says the keys are back under the deployment key', async () => {
    let current = listing;
    const calls = installFetch(
      serve(
        () => current,
        (url, method) => {
          if (url === `${rootKeysUrl}/eu-west` && method === 'DELETE') {
            current = { ...listing, rootKeys: [] };
            return new Response(null, { status: 204 });
          }
          return undefined;
        },
      ),
    );

    renderTab();
    const eu = within(await screen.findByTestId('team-root-key-region-eu-west'));
    fireEvent.click(await eu.findByTestId('root-key-remove'));
    fireEvent.click(await screen.findByTestId('confirm-accept'));

    expect(await eu.findByTestId('root-key-none')).toBeInTheDocument();
    expect(eu.getByTestId('root-key-remove-result')).toHaveTextContent(
      "The keys in eu-west are back under the deployment's root key.",
    );
    await waitFor(() => expect(screen.queryByTestId('confirm-accept')).not.toBeInTheDocument());
    expect(calls.some((c) => c.method === 'DELETE' && c.url === `${rootKeysUrl}/eu-west`)).toBe(
      true,
    );
  });

  it('says the keys are still being rewrapped when the removal answers with the key', async () => {
    installFetch(
      serve(
        () => listing,
        (_url, method) => (method === 'DELETE' ? jsonResponse(removingKey, 202) : undefined),
      ),
    );

    renderTab();
    const eu = within(await screen.findByTestId('team-root-key-region-eu-west'));
    fireEvent.click(await eu.findByTestId('root-key-remove'));
    fireEvent.click(await screen.findByTestId('confirm-accept'));

    expect(await eu.findByTestId('root-key-remove-result')).toHaveTextContent(
      'The keys in eu-west are still being rewrapped',
    );
  });

  it('does not remove a key when the confirmation is cancelled', async () => {
    const calls = installFetch(serve(() => listing));

    renderTab();
    const eu = within(await screen.findByTestId('team-root-key-region-eu-west'));
    fireEvent.click(await eu.findByTestId('root-key-remove'));
    fireEvent.click(await screen.findByTestId('confirm-cancel'));

    await waitFor(() => expect(screen.queryByTestId('confirm-accept')).not.toBeInTheDocument());
    expect(calls.every((c) => c.method === 'GET')).toBe(true);
  });

  it('shows the explanation of a failed removal and keeps the dialog open', async () => {
    installFetch(
      serve(
        () => listing,
        (_url, method) =>
          method === 'DELETE'
            ? jsonResponse(
                { title: 'Conflict', status: 409, detail: 'The removal is in progress.' },
                409,
              )
            : undefined,
      ),
    );

    renderTab();
    const eu = within(await screen.findByTestId('team-root-key-region-eu-west'));
    fireEvent.click(await eu.findByTestId('root-key-remove'));
    fireEvent.click(await screen.findByTestId('confirm-accept'));

    expect(await screen.findByTestId('confirm-error')).toHaveTextContent(
      'The removal is in progress.',
    );
  });
});
