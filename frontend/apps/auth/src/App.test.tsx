import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render } from '@testing-library/react';
import { AppThemeProvider } from '@ncodegroup/identity-ui';
import { App } from './App.js';
import { EMPTY_SERVER_PAGE } from './serverPage.js';

function renderApp(props: Parameters<typeof App>[0]) {
  return render(
    <AppThemeProvider>
      <App {...props} />
    </AppThemeProvider>,
  );
}

// The consent and account surfaces load through their default same-origin fetch; a request that never settles keeps the
// tests hermetic and the surfaces in their initial state.
beforeEach(() => {
  vi.stubGlobal(
    'fetch',
    vi.fn(() => new Promise<Response>(() => {})),
  );
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('App login surface', () => {
  it('renders the login form with the stable locators and hidden fields', () => {
    const { container } = renderApp({
      pathname: '/account/login',
      search: '?ReturnUrl=%2Fauthorize%3Fx%3D1',
      cookie: '__Host-XSRF-TOKEN=the-token',
    });

    expect(container.querySelector('#username')).not.toBeNull();
    expect(container.querySelector('#password')).not.toBeNull();
    expect(container.querySelector('#login')).not.toBeNull();

    const form = container.querySelector('form');
    expect(form?.getAttribute('action')).toBe('/account/login');
    expect(form?.getAttribute('method')).toBe('post');

    const returnUrl = container.querySelector('input[name="ReturnUrl"]') as HTMLInputElement | null;
    expect(returnUrl?.value).toBe('/authorize?x=1');
    const token = container.querySelector(
      'input[name="__RequestVerificationToken"]',
    ) as HTMLInputElement | null;
    expect(token?.value).toBe('the-token');
  });

  it('shows an error message when the error query is present', () => {
    const { getByText } = renderApp({
      pathname: '/account/login',
      search: '?error=invalid_credentials',
      cookie: '',
    });
    expect(getByText(/incorrect/i)).toBeInTheDocument();
  });
});

describe('App consent surface', () => {
  it('renders the consent form with the stable locators', () => {
    const { container } = renderApp({
      pathname: '/account/consent',
      search: '?ReturnUrl=%2Fauthorize',
      cookie: '__Host-XSRF-TOKEN=tok',
    });

    expect(container.querySelector('#consent-allow')).not.toBeNull();
    expect(container.querySelector('#consent-deny')).not.toBeNull();
    expect(container.querySelector('form')?.getAttribute('action')).toBe('/account/consent');
  });
});

describe('App account surface', () => {
  it('renders the account page with a sign-out form posting to the logout endpoint', () => {
    const { container } = renderApp({
      pathname: '/account',
      search: '',
      cookie: '__Host-XSRF-TOKEN=the-token',
    });

    const logout = container.querySelector('#logout');
    expect(logout).not.toBeNull();

    const form = container.querySelector('form');
    expect(form?.getAttribute('action')).toBe('/account/logout');
    expect(form?.getAttribute('method')).toBe('post');

    const token = container.querySelector(
      'input[name="__RequestVerificationToken"]',
    ) as HTMLInputElement | null;
    expect(token?.value).toBe('the-token');
  });
});

describe('App email-code surfaces', () => {
  it('reads the flow query parameter into the reset-password page', () => {
    const { container } = renderApp({
      pathname: '/account/password/reset',
      search: '?flow=handle-1&ReturnUrl=%2Fauthorize&error=invalid_code',
      cookie: '__Host-XSRF-TOKEN=the-token',
    });

    expect(container.querySelector('form')?.getAttribute('action')).toBe('/account/password/reset');
    expect((container.querySelector('input[name="flow"]') as HTMLInputElement).value).toBe(
      'handle-1',
    );
    expect((container.querySelector('input[name="ReturnUrl"]') as HTMLInputElement).value).toBe(
      '/authorize',
    );
    expect(container.textContent).toContain('The code is incorrect or has expired.');
  });

  it('renders the forgot-password page', () => {
    const { container } = renderApp({
      pathname: '/account/password/forgot',
      search: '',
      cookie: '__Host-XSRF-TOKEN=the-token',
    });

    expect(container.querySelector('#send-code')).not.toBeNull();
  });

  it('confirms the reset on the login page the host redirects to', () => {
    const { container } = renderApp({
      pathname: '/account/login',
      search: '?status=password_reset',
      cookie: '',
    });

    expect(container.textContent).toContain('Your password has been reset.');
  });
});

describe('App branding', () => {
  it('shows the tenant logo and titles the surface with its display name', () => {
    const { container } = renderApp({
      pathname: '/account/login',
      search: '',
      cookie: '',
      branding: { displayName: 'Acme', logoUrl: 'https://cdn.acme.example/logo.svg' },
    });

    expect(container.querySelector('header img')?.getAttribute('src')).toBe(
      'https://cdn.acme.example/logo.svg',
    );
    expect(container.textContent).toContain('Sign in to Acme');
  });

  it('draws no header without branding', () => {
    const { container } = renderApp({ pathname: '/account/login', search: '', cookie: '' });

    expect(container.querySelector('header')).toBeNull();
  });
});

describe('App server page', () => {
  it('links the login page to the forgot-password page the host rendered', () => {
    const { container } = renderApp({
      pathname: '/account/login',
      search: '',
      cookie: '',
      serverPage: { ...EMPTY_SERVER_PAGE, forgotPasswordHref: 'password/forgot' },
    });

    expect(container.querySelector('#forgot-password-link')?.getAttribute('href')).toBe(
      'password/forgot',
    );
  });

  it('redraws the authorization error the host rendered at the protocol endpoint', () => {
    const { container } = renderApp({
      pathname: '/oauth2/authorize',
      search: '',
      cookie: '',
      serverPage: {
        ...EMPTY_SERVER_PAGE,
        authorizationError: { code: 'invalid_client', description: null },
      },
    });

    expect(container.querySelector('#authorization-error')?.getAttribute('data-error-code')).toBe(
      'invalid_client',
    );
  });
});
