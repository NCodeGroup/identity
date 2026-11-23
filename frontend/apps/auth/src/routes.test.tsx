import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { AppThemeProvider } from '@ncodegroup/identity-ui';
import { resolveRoute, type RouteContext } from './routes.js';
import { EMPTY_SERVER_PAGE } from './serverPage.js';
import { en } from './i18n/en.js';

const baseContext: RouteContext = {
  pathname: '/account/login',
  returnUrl: '/authorize',
  antiforgeryToken: 'tok',
  messages: en,
};

function renderRoute(context: RouteContext) {
  return render(<AppThemeProvider>{resolveRoute(context)}</AppThemeProvider>);
}

describe('resolveRoute', () => {
  // The data-loading surfaces use their default same-origin loaders; a request that never settles keeps them in their
  // initial state and the test hermetic, while still recording which endpoint each surface reads.
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    fetchMock = vi.fn(() => new Promise<Response>(() => {}));
    vi.stubGlobal('fetch', fetchMock);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renders the account surface for the account path', async () => {
    const { container } = renderRoute({ ...baseContext, pathname: '/account' });
    expect(container.querySelector('#logout')).not.toBeNull();
    expect(screen.getByTestId('account-loading')).toBeInTheDocument();
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/account/me', expect.anything()));
  });

  it('renders the consent surface for the consent path', async () => {
    const { container } = renderRoute({ ...baseContext, pathname: '/account/consent' });
    expect(container.querySelector('#consent-allow')).not.toBeNull();
    expect(
      (container.querySelector('input[name="ReturnUrl"]') as HTMLInputElement | null)?.value,
    ).toBe('/authorize');
    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        '/account/consent/details?ReturnUrl=%2Fauthorize',
        expect.anything(),
      ),
    );
  });

  it('renders the authorized-applications surface for the applications path', async () => {
    const { container } = renderRoute({ ...baseContext, pathname: '/account/applications' });
    expect(container.querySelector('[data-testid="applications-loading"]')).not.toBeNull();
    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith('/account/applications/list', expect.anything()),
    );
  });

  it('renders the active-sessions surface for the sessions path', async () => {
    renderRoute({ ...baseContext, pathname: '/account/sessions' });
    expect(screen.getByTestId('sessions-loading')).toBeInTheDocument();
    expect(screen.getByTestId('sessions-revoke-all')).toBeInTheDocument();
    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith('/account/sessions/list', expect.anything()),
    );
  });

  it('renders the logout confirmation surface for the logout confirmation path', () => {
    const { container } = renderRoute({ ...baseContext, pathname: '/account/logout/confirm' });
    expect(container.querySelector('#logout-confirm')).not.toBeNull();
    expect(container.querySelector('#logout-cancel')).not.toBeNull();
    expect(
      (container.querySelector('input[name="ReturnUrl"]') as HTMLInputElement | null)?.value,
    ).toBe('/authorize');
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('renders the change-password surface for the password path', () => {
    const { container } = renderRoute({ ...baseContext, pathname: '/account/password' });
    expect(container.querySelector('#change-password')).not.toBeNull();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.queryByTestId('password-changed')).not.toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('maps the password error code and status', () => {
    renderRoute({
      ...baseContext,
      pathname: '/account/password',
      errorCode: 'invalid_current_password',
      status: 'changed',
    });
    expect(screen.getByText('The current password is incorrect.')).toBeInTheDocument();
    expect(screen.getByTestId('password-changed')).toBeInTheDocument();
  });

  it('renders the forgot-password surface for the forgot-password path', () => {
    const { container } = renderRoute({ ...baseContext, pathname: '/account/password/forgot' });
    expect(container.querySelector('form')?.getAttribute('action')).toBe(
      '/account/password/forgot',
    );
    expect(container.querySelector('#send-code')).not.toBeNull();
    expect(
      (container.querySelector('input[name="ReturnUrl"]') as HTMLInputElement | null)?.value,
    ).toBe('/authorize');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('maps the email-code error code on the forgot-password surface', () => {
    renderRoute({
      ...baseContext,
      pathname: '/account/password/forgot',
      errorCode: 'invalid_request',
    });
    expect(screen.getByRole('alert')).toHaveTextContent(en.emailCodeErrors.missingFields);
  });

  it('renders the reset-password surface for the reset-password path, carrying the flow', () => {
    const { container } = renderRoute({
      ...baseContext,
      pathname: '/account/password/reset',
      flow: 'handle-1',
    });
    expect(container.querySelector('form')?.getAttribute('action')).toBe('/account/password/reset');
    expect(container.querySelector('#reset-password')).not.toBeNull();
    expect((container.querySelector('input[name="flow"]') as HTMLInputElement | null)?.value).toBe(
      'handle-1',
    );
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('carries no flow to the reset-password surface when the query has none', () => {
    const { container } = renderRoute({
      ...baseContext,
      pathname: '/account/password/reset',
      flow: null,
    });
    expect((container.querySelector('input[name="flow"]') as HTMLInputElement | null)?.value).toBe(
      '',
    );
  });

  it('maps the email-code error code on the reset-password surface', () => {
    renderRoute({
      ...baseContext,
      pathname: '/account/password/reset',
      flow: 'handle-1',
      errorCode: 'invalid_code',
    });
    expect(screen.getByRole('alert')).toHaveTextContent(en.emailCodeErrors.invalidCode);
  });

  it('renders the sign-in code surface for the sign-in code path', () => {
    const { container } = renderRoute({ ...baseContext, pathname: '/account/login/code' });
    expect(container.querySelector('form')?.getAttribute('action')).toBe('/account/login/code');
    expect(container.querySelector('#verify-code')).not.toBeNull();
    expect(container.querySelector('#resend-code')).not.toBeNull();
    expect(container.querySelector('#login')).toBeNull();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('confirms a sent code on the sign-in code surface', () => {
    renderRoute({ ...baseContext, pathname: '/account/login/code', status: 'sent' });
    expect(screen.getByRole('alert')).toHaveTextContent(en.signInCode.sent);
  });

  it('shows no confirmation on the sign-in code surface for any other status', () => {
    renderRoute({ ...baseContext, pathname: '/account/login/code', status: 'other' });
    expect(screen.queryByText(en.signInCode.sent)).not.toBeInTheDocument();
  });

  it('maps the email-code error code on the sign-in code surface', () => {
    renderRoute({
      ...baseContext,
      pathname: '/account/login/code',
      errorCode: 'too_many_codes',
    });
    expect(screen.getByRole('alert')).toHaveTextContent(en.emailCodeErrors.tooManyCodes);
  });

  it('renders the email surface for the email path', async () => {
    renderRoute({ ...baseContext, pathname: '/account/email' });
    expect(screen.getByTestId('email-loading')).toBeInTheDocument();
    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith('/account/email/status', expect.anything()),
    );
  });

  it('carries the flow, the status and the error code to the email surface', () => {
    renderRoute({
      ...baseContext,
      pathname: '/account/email',
      flow: 'handle-1',
      status: 'verified',
      errorCode: 'invalid_code',
    });
    expect(screen.getByTestId('email-verified-status')).toBeInTheDocument();
    expect(screen.getByText(en.emailCodeErrors.invalidCode)).toBeInTheDocument();
  });

  it('links the login surface to the forgot-password page the server page offers', () => {
    renderRoute({
      ...baseContext,
      serverPage: { ...EMPTY_SERVER_PAGE, forgotPasswordHref: 'password/forgot?ReturnUrl=%2F' },
    });
    expect(screen.getByRole('link', { name: en.login.forgotPassword })).toHaveAttribute(
      'href',
      'password/forgot?ReturnUrl=%2F',
    );
  });

  it('offers no forgot-password link when the server page has none', () => {
    renderRoute(baseContext);
    expect(screen.queryByRole('link', { name: en.login.forgotPassword })).not.toBeInTheDocument();
  });

  it('confirms a password reset on the login surface', () => {
    renderRoute({ ...baseContext, status: 'password_reset' });
    expect(screen.getByRole('alert')).toHaveTextContent(en.login.passwordReset);
  });

  it('shows no status on the login surface for any other status', () => {
    renderRoute({ ...baseContext, status: 'changed' });
    expect(screen.queryByText(en.login.passwordReset)).not.toBeInTheDocument();
  });

  it.each([
    ['omitted', {}],
    ['null', { status: null }],
  ] as const)('shows no status on the login surface when the status is %s', (_label, extra) => {
    renderRoute({ ...baseContext, ...extra });
    expect(screen.queryByText(en.login.passwordReset)).not.toBeInTheDocument();
  });

  it.each([
    ['/account/', 'account-loading'],
    ['/account/applications/', 'applications-loading'],
    ['/account/sessions//', 'sessions-loading'],
  ])('tolerates a trailing slash on %s', (pathname, testId) => {
    renderRoute({ ...baseContext, pathname });
    expect(screen.getByTestId(testId)).toBeInTheDocument();
  });

  it('renders the login surface for the login path, carrying the return URL and antiforgery token', () => {
    const { container } = renderRoute(baseContext);
    expect(container.querySelector('#login')).not.toBeNull();
    expect(
      (container.querySelector('input[name="ReturnUrl"]') as HTMLInputElement | null)?.value,
    ).toBe('/authorize');
    expect(
      (
        container.querySelector(
          'input[name="__RequestVerificationToken"]',
        ) as HTMLInputElement | null
      )?.value,
    ).toBe('tok');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('passes the login error message through to the login surface', () => {
    renderRoute({ ...baseContext, errorMessage: 'The username or password is incorrect.' });
    expect(screen.getByRole('alert')).toHaveTextContent('The username or password is incorrect.');
  });

  it('omits the login error alert when the error message is null', () => {
    const { container } = renderRoute({ ...baseContext, errorMessage: null });
    expect(container.querySelector('#login')).not.toBeNull();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('falls back to the login surface for any unmatched path', () => {
    const { container } = renderRoute({ ...baseContext, pathname: '/anything/else' });
    expect(container.querySelector('#login')).not.toBeNull();
  });

  it('does not treat a sub-path of a surface as that surface', () => {
    const { container } = renderRoute({ ...baseContext, pathname: '/account/sessions/other' });
    expect(container.querySelector('#login')).not.toBeNull();
    expect(screen.queryByTestId('sessions-loading')).not.toBeInTheDocument();
  });

  it.each([
    ['/account/login', 'Sign in to Acme'],
    ['/account', 'Acme account'],
    ['/account/applications', 'Acme — authorized applications'],
    ['/account/sessions', 'Acme — active sessions'],
    ['/account/password', 'Acme — change password'],
    ['/account/email', 'Email address for Acme'],
  ])('titles %s with the tenant display name', (pathname, title) => {
    renderRoute({ ...baseContext, pathname, branding: { displayName: 'Acme' } });
    expect(screen.getByText(title)).toBeInTheDocument();
  });

  it('names the requesting application, not the tenant, on the consent surface', () => {
    renderRoute({
      ...baseContext,
      pathname: '/account/consent',
      branding: { displayName: 'Acme' },
    });
    expect(screen.queryByText(/Acme/)).toBeNull();
  });

  it('keeps the default titles when the branding has no display name', () => {
    renderRoute({ ...baseContext, branding: { primaryColor: '#1565c0' } });
    expect(screen.getByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
  });

  it('redraws the authorization error the server page reports, whatever the path', () => {
    const { container } = renderRoute({
      ...baseContext,
      pathname: '/oauth2/authorize',
      serverPage: {
        ...EMPTY_SERVER_PAGE,
        authorizationError: { code: 'invalid_client', description: null },
      },
    });

    expect(container.querySelector('#authorization-error')).not.toBeNull();
    expect(container.querySelector('#username')).toBeNull();
  });

  it("carries the server page's typed username into the login surface", () => {
    const { container } = renderRoute({
      ...baseContext,
      serverPage: { ...EMPTY_SERVER_PAGE, fieldValues: { username: 'alice' } },
    });

    expect(container.querySelector<HTMLInputElement>('#username')?.value).toBe('alice');
  });
});
