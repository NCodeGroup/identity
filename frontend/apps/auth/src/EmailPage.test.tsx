import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, render, screen, waitFor } from '@testing-library/react';
import { AppThemeProvider } from '@ncodegroup/identity-ui';
import { EmailPage, type AccountEmail } from './EmailPage.js';
import { en } from './i18n/en.js';
import { LocaleProvider } from './i18n/LocaleContext.js';

function renderPage(props: Parameters<typeof EmailPage>[0]) {
  return render(
    <AppThemeProvider>
      <EmailPage {...props} />
    </AppThemeProvider>,
  );
}

function deferred<T>() {
  let resolve: (value: T) => void = () => {};
  const promise = new Promise<T>((settle) => {
    resolve = settle;
  });
  return { promise, resolve };
}

const unverified: AccountEmail = { email: 'ada@example.test', verified: false };
const verified: AccountEmail = { email: 'ada@example.test', verified: true };

describe('EmailPage', () => {
  it('shows a loading state until the address resolves, and keeps the way back', () => {
    renderPage({
      antiforgeryToken: 'tok',
      loadEmail: () => new Promise<AccountEmail | null>(() => {}),
    });

    expect(screen.getByTestId('email-loading')).toHaveTextContent(en.email.loading);
    expect(screen.getByRole('link', { name: en.email.back })).toHaveAttribute('href', '/account/');
    expect(screen.queryByTestId('email-details')).not.toBeInTheDocument();
  });

  it('stays in the loading state when the loader fails', async () => {
    const loadEmail = vi.fn(() => Promise.reject(new Error('network down')));
    renderPage({ antiforgeryToken: 'tok', loadEmail });

    await waitFor(() => expect(loadEmail).toHaveBeenCalledTimes(1));
    await act(async () => {});
    expect(screen.getByTestId('email-loading')).toBeInTheDocument();
  });

  it('shows that verification is unavailable when there is no address to verify', async () => {
    renderPage({ antiforgeryToken: 'tok', loadEmail: () => Promise.resolve(null) });

    expect(await screen.findByTestId('email-unavailable')).toHaveTextContent(en.email.unavailable);
    expect(screen.queryByTestId('email-loading')).not.toBeInTheDocument();
    expect(screen.queryByTestId('email-send')).not.toBeInTheDocument();
    expect(screen.queryByTestId('email-verify')).not.toBeInTheDocument();
  });

  it('shows an unverified address with a form that mails a code', async () => {
    const { container } = renderPage({
      antiforgeryToken: 'tok',
      loadEmail: () => Promise.resolve(unverified),
    });

    expect(await screen.findByTestId('email-details')).toBeInTheDocument();
    expect(screen.getByTestId('email-address')).toHaveTextContent('ada@example.test');
    expect(screen.getByTestId('email-state')).toHaveTextContent(en.email.unverified);

    const form = screen.getByTestId('email-send');
    expect(form.getAttribute('action')).toBe('/account/email/send');
    expect(form.getAttribute('method')).toBe('post');
    const token = form.querySelector(
      'input[name="__RequestVerificationToken"]',
    ) as HTMLInputElement | null;
    expect(token?.type).toBe('hidden');
    expect(token?.value).toBe('tok');
    expect(screen.getByRole('button', { name: en.email.sendCode })).toHaveAttribute(
      'id',
      'send-email-code',
    );
    expect(screen.queryByTestId('email-verify')).not.toBeInTheDocument();
    expect(container.querySelector('#code')).toBeNull();
  });

  it('posts the page language as ui_locales from the send form', async () => {
    render(
      <LocaleProvider pageLanguage="fr">
        <AppThemeProvider>
          <EmailPage antiforgeryToken="tok" loadEmail={() => Promise.resolve(unverified)} />
        </AppThemeProvider>
      </LocaleProvider>,
    );

    const form = await screen.findByTestId('email-send');
    const carried = form.querySelector('input[name="ui_locales"]') as HTMLInputElement | null;
    expect(carried?.type).toBe('hidden');
    expect(carried?.value).toBe('fr');
  });

  it('posts no ui_locales from the send form when the host selected no language', async () => {
    renderPage({ antiforgeryToken: 'tok', loadEmail: () => Promise.resolve(unverified) });

    const form = await screen.findByTestId('email-send');
    expect(form.querySelector('input[name="ui_locales"]')).toBeNull();
  });

  it('shows the verify form, carrying the flow, once a code was mailed', async () => {
    renderPage({
      antiforgeryToken: 'tok',
      flow: 'handle-1',
      loadEmail: () => Promise.resolve(unverified),
    });

    const form = await screen.findByTestId('email-verify');
    expect(form.getAttribute('action')).toBe('/account/email/verify');
    expect(form.getAttribute('method')).toBe('post');
    const hidden = (name: string) =>
      form.querySelector(`input[name="${name}"]`) as HTMLInputElement | null;
    expect(hidden('__RequestVerificationToken')?.value).toBe('tok');
    expect(hidden('flow')?.type).toBe('hidden');
    expect(hidden('flow')?.value).toBe('handle-1');

    expect(screen.getByText(en.email.codePrompt)).toBeInTheDocument();
    const code = screen.getByLabelText(/^Code/);
    expect(code).toHaveAttribute('id', 'code');
    expect(code).toHaveAttribute('name', 'code');
    expect(code).toHaveAttribute('inputmode', 'numeric');
    expect(code).toHaveAttribute('autocomplete', 'one-time-code');
    expect(code).toBeRequired();
    expect(screen.getByRole('button', { name: en.email.verify })).toHaveAttribute(
      'id',
      'verify-email',
    );
    expect(screen.queryByTestId('email-send')).not.toBeInTheDocument();
  });

  it('shows a verified address with no form', async () => {
    renderPage({
      antiforgeryToken: 'tok',
      flow: 'handle-1',
      loadEmail: () => Promise.resolve(verified),
    });

    expect(await screen.findByTestId('email-state')).toHaveTextContent(en.email.verified);
    expect(screen.queryByTestId('email-send')).not.toBeInTheDocument();
    expect(screen.queryByTestId('email-verify')).not.toBeInTheDocument();
    expect(screen.queryByTestId('email-verified-status')).not.toBeInTheDocument();
  });

  it('confirms a completed verification', async () => {
    renderPage({
      antiforgeryToken: 'tok',
      status: 'verified',
      loadEmail: () => Promise.resolve(verified),
    });

    expect(await screen.findByTestId('email-state')).toBeInTheDocument();
    expect(screen.getByTestId('email-verified-status')).toHaveTextContent(en.email.verifiedStatus);
  });

  it('shows no confirmation for any other status', async () => {
    renderPage({
      antiforgeryToken: 'tok',
      status: 'other',
      loadEmail: () => Promise.resolve(unverified),
    });

    expect(await screen.findByTestId('email-details')).toBeInTheDocument();
    expect(screen.queryByTestId('email-verified-status')).not.toBeInTheDocument();
  });

  it('shows the error message in an alert when one is provided', async () => {
    renderPage({
      antiforgeryToken: 'tok',
      flow: 'handle-1',
      errorMessage: en.emailCodeErrors.invalidCode,
      loadEmail: () => Promise.resolve(unverified),
    });

    expect(await screen.findByTestId('email-verify')).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent(en.emailCodeErrors.invalidCode);
  });

  it.each([
    ['null', { errorMessage: null }],
    ['empty', { errorMessage: '' }],
  ] as const)('shows no alert when the error message is %s', async (_label, extra) => {
    renderPage({ antiforgeryToken: 'tok', loadEmail: () => Promise.resolve(unverified), ...extra });

    expect(await screen.findByTestId('email-details')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('titles the page with the product name when one is provided', async () => {
    renderPage({
      antiforgeryToken: 'tok',
      productName: 'Contoso',
      loadEmail: () => Promise.resolve(verified),
    });

    expect(await screen.findByTestId('email-details')).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(
      'Email address for Contoso',
    );
  });

  it('titles the page generically without a product name', async () => {
    renderPage({ antiforgeryToken: 'tok', loadEmail: () => Promise.resolve(verified) });

    expect(await screen.findByTestId('email-details')).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(en.email.title);
  });

  it('ignores a stale load when the loader changes before it resolves', async () => {
    const stale = deferred<AccountEmail | null>();
    const { rerender } = renderPage({ antiforgeryToken: 'tok', loadEmail: () => stale.promise });
    rerender(
      <AppThemeProvider>
        <EmailPage
          antiforgeryToken="tok"
          loadEmail={() => Promise.resolve({ email: 'fresh@example.test', verified: true })}
        />
      </AppThemeProvider>,
    );

    expect(await screen.findByTestId('email-address')).toHaveTextContent('fresh@example.test');
    await act(async () => {
      stale.resolve({ email: 'stale@example.test', verified: true });
    });

    expect(screen.getByTestId('email-address')).toHaveTextContent('fresh@example.test');
    expect(screen.queryByText('stale@example.test')).not.toBeInTheDocument();
  });

  describe('default loader', () => {
    const originalLocation = window.location;
    let assignMock: ReturnType<typeof vi.fn>;

    beforeEach(() => {
      assignMock = vi.fn();
      Object.defineProperty(window, 'location', {
        configurable: true,
        value: { origin: 'https://op.test', href: 'https://op.test/account', assign: assignMock },
      });
    });

    afterEach(() => {
      vi.unstubAllGlobals();
      Object.defineProperty(window, 'location', {
        configurable: true,
        value: originalLocation,
      });
    });

    it('fetches the email status endpoint and renders the returned address', async () => {
      const fetchMock = vi.fn(() =>
        Promise.resolve(
          new Response(JSON.stringify(unverified), {
            status: 200,
            headers: { 'content-type': 'application/json' },
          }),
        ),
      );
      vi.stubGlobal('fetch', fetchMock);

      renderPage({ antiforgeryToken: 'tok' });

      expect(await screen.findByTestId('email-address')).toHaveTextContent('ada@example.test');
      expect(fetchMock).toHaveBeenCalledWith('/account/email/status', {
        headers: { accept: 'application/json' },
      });
      expect(assignMock).not.toHaveBeenCalled();
    });

    it('shows that verification is unavailable on a 404', async () => {
      const fetchMock = vi.fn(() => Promise.resolve(new Response(null, { status: 404 })));
      vi.stubGlobal('fetch', fetchMock);

      renderPage({ antiforgeryToken: 'tok' });

      expect(await screen.findByTestId('email-unavailable')).toBeInTheDocument();
      expect(assignMock).not.toHaveBeenCalled();
    });

    it('redirects to login on 401', async () => {
      const fetchMock = vi.fn(() => Promise.resolve(new Response(null, { status: 401 })));
      vi.stubGlobal('fetch', fetchMock);

      renderPage({ antiforgeryToken: 'tok' });

      await waitFor(() => expect(assignMock).toHaveBeenCalledTimes(1));
      expect(assignMock.mock.calls[0]![0]).toBe('/account/login?ReturnUrl=%2Faccount%2Femail');
    });
  });
});
