import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { AppThemeProvider } from '@ncodegroup/identity-ui';
import { ResetPasswordPage } from './ResetPasswordPage.js';
import { LocaleProvider } from './i18n/LocaleContext.js';

function renderPage(props: Parameters<typeof ResetPasswordPage>[0]) {
  return render(
    <AppThemeProvider>
      <ResetPasswordPage {...props} />
    </AppThemeProvider>,
  );
}

describe('ResetPasswordPage', () => {
  it('renders a native POST form with the code, the two passwords and the hidden fields', () => {
    const { container } = renderPage({
      antiforgeryToken: 'tok',
      returnUrl: '/authorize?x=1',
      flow: 'handle-1',
    });

    const form = container.querySelector('form');
    expect(form?.getAttribute('action')).toBe('/account/password/reset');
    expect(form?.getAttribute('method')).toBe('post');
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Choose a new password');
    expect(
      screen.getByText('Enter the code from the email and choose a new password.'),
    ).toBeInTheDocument();

    const code = screen.getByLabelText(/^Code/);
    expect(code).toHaveAttribute('id', 'code');
    expect(code).toHaveAttribute('name', 'code');
    expect(code).toHaveAttribute('inputmode', 'numeric');
    expect(code).toHaveAttribute('autocomplete', 'one-time-code');
    expect(code).toHaveFocus();
    expect(code).toBeRequired();

    const next = screen.getByLabelText(/^New password/);
    expect(next).toHaveAttribute('id', 'new_password');
    expect(next).toHaveAttribute('name', 'new_password');
    expect(next).toHaveAttribute('type', 'password');
    expect(next).toHaveAttribute('autocomplete', 'new-password');
    expect(next).toBeRequired();

    const confirm = screen.getByLabelText(/Confirm new password/);
    expect(confirm).toHaveAttribute('id', 'confirm_password');
    expect(confirm).toHaveAttribute('name', 'confirm_password');
    expect(confirm).toHaveAttribute('type', 'password');
    expect(confirm).toHaveAttribute('autocomplete', 'new-password');
    expect(confirm).toBeRequired();

    const hidden = (name: string) =>
      container.querySelector(`input[name="${name}"]`) as HTMLInputElement | null;
    expect(hidden('__RequestVerificationToken')?.type).toBe('hidden');
    expect(hidden('__RequestVerificationToken')?.value).toBe('tok');
    expect(hidden('ReturnUrl')?.type).toBe('hidden');
    expect(hidden('ReturnUrl')?.value).toBe('/authorize?x=1');
    expect(hidden('flow')?.type).toBe('hidden');
    expect(hidden('flow')?.value).toBe('handle-1');
    expect(hidden('ui_locales')).toBeNull();

    expect(container.querySelector('#reset-password')).toBe(
      screen.getByRole('button', { name: 'Reset password' }),
    );
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('shows the error message in an alert when one is provided', () => {
    renderPage({
      antiforgeryToken: 'tok',
      returnUrl: '/',
      flow: 'handle-1',
      errorMessage: 'The code is incorrect or has expired.',
    });

    expect(screen.getByRole('alert')).toHaveTextContent('The code is incorrect or has expired.');
  });

  it.each([
    ['null', { errorMessage: null }],
    ['empty', { errorMessage: '' }],
  ] as const)('shows no alert when the error message is %s', (_label, extra) => {
    renderPage({ antiforgeryToken: 'tok', returnUrl: '/', flow: 'handle-1', ...extra });

    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('renders in the language the host selected and carries it back on the form', () => {
    const { container } = render(
      <LocaleProvider pageLanguage="fr">
        <AppThemeProvider>
          <ResetPasswordPage antiforgeryToken="tok" returnUrl="/" flow="handle-1" />
        </AppThemeProvider>
      </LocaleProvider>,
    );

    const carried = container.querySelector('input[name="ui_locales"]') as HTMLInputElement | null;
    expect(carried?.value).toBe('fr');
    expect(screen.getByRole('heading', { level: 1 })).not.toHaveTextContent(
      'Choose a new password',
    );
  });
});
