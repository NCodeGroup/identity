import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { AppThemeProvider } from '@ncodegroup/identity-ui';
import { ForgotPasswordPage } from './ForgotPasswordPage.js';
import { LocaleProvider } from './i18n/LocaleContext.js';

function renderPage(props: Parameters<typeof ForgotPasswordPage>[0]) {
  return render(
    <AppThemeProvider>
      <ForgotPasswordPage {...props} />
    </AppThemeProvider>,
  );
}

describe('ForgotPasswordPage', () => {
  it('renders a native POST form with the username and the hidden fields', () => {
    const { container } = renderPage({ antiforgeryToken: 'tok', returnUrl: '/authorize?x=1' });

    const form = container.querySelector('form');
    expect(form?.getAttribute('action')).toBe('/account/password/forgot');
    expect(form?.getAttribute('method')).toBe('post');
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Reset your password');
    expect(
      screen.getByText(
        'Enter your username. If its account has a verified email address, we will send a code to it.',
      ),
    ).toBeInTheDocument();

    const username = screen.getByLabelText(/Username/);
    expect(username).toHaveAttribute('id', 'username');
    expect(username).toHaveAttribute('name', 'username');
    expect(username).toHaveAttribute('autocomplete', 'username');
    expect(username).toHaveFocus();
    expect(username).toBeRequired();

    const token = container.querySelector(
      'input[name="__RequestVerificationToken"]',
    ) as HTMLInputElement | null;
    expect(token?.type).toBe('hidden');
    expect(token?.value).toBe('tok');
    const returnUrl = container.querySelector('input[name="ReturnUrl"]') as HTMLInputElement | null;
    expect(returnUrl?.type).toBe('hidden');
    expect(returnUrl?.value).toBe('/authorize?x=1');

    expect(container.querySelector('#send-code')).toBe(
      screen.getByRole('button', { name: 'Send code' }),
    );
    expect(container.querySelector('input[name="ui_locales"]')).toBeNull();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('shows the error message in an alert when one is provided', () => {
    renderPage({
      antiforgeryToken: 'tok',
      returnUrl: '/',
      errorMessage: 'Enter both a username and a password.',
    });

    expect(screen.getByRole('alert')).toHaveTextContent('Enter both a username and a password.');
  });

  it.each([
    ['null', { errorMessage: null }],
    ['empty', { errorMessage: '' }],
  ] as const)('shows no alert when the error message is %s', (_label, extra) => {
    renderPage({ antiforgeryToken: 'tok', returnUrl: '/', ...extra });

    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('renders in the language the host selected and carries it back on the form', () => {
    const { container } = render(
      <LocaleProvider pageLanguage="fr">
        <AppThemeProvider>
          <ForgotPasswordPage antiforgeryToken="tok" returnUrl="/" />
        </AppThemeProvider>
      </LocaleProvider>,
    );

    const carried = container.querySelector('input[name="ui_locales"]') as HTMLInputElement | null;
    expect(carried?.value).toBe('fr');
    expect(screen.getByRole('heading', { level: 1 })).not.toHaveTextContent('Reset your password');
  });
});

describe('ForgotPasswordPage connection field', () => {
  it('carries the connection as a select when several are offered', () => {
    const { container } = renderPage({
      antiforgeryToken: 't',
      returnUrl: '/',
      connections: [
        { name: 'local', displayName: 'Local' },
        { name: 'corp', displayName: 'Corp' },
      ],
      selectedConnection: 'local',
    });

    const select = container.querySelector('select#connection') as HTMLSelectElement | null;
    expect(select?.name).toBe('connection');
    expect(select?.value).toBe('local');
  });

  it('carries the connection as a hidden input when one is offered', () => {
    const { container } = renderPage({
      antiforgeryToken: 't',
      returnUrl: '/',
      connections: [{ name: 'local', displayName: 'Local' }],
      selectedConnection: 'local',
    });

    const hidden = container.querySelector('input[name="connection"]') as HTMLInputElement | null;
    expect(hidden?.type).toBe('hidden');
    expect(hidden?.value).toBe('local');
  });
});
