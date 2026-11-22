import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { AppThemeProvider } from '@ncodegroup/identity-ui';
import { SignInCodePage } from './SignInCodePage.js';
import { en } from './i18n/en.js';

function renderPage(props: Parameters<typeof SignInCodePage>[0]) {
  return render(
    <AppThemeProvider>
      <SignInCodePage {...props} />
    </AppThemeProvider>,
  );
}

describe('SignInCodePage', () => {
  it('renders a native POST form with the code field and the antiforgery token', () => {
    const { container } = renderPage({ antiforgeryToken: 'tok' });

    const form = container.querySelector('form');
    expect(form?.getAttribute('action')).toBe('/account/login/code');
    expect(form?.getAttribute('method')).toBe('post');
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(en.signInCode.title);
    expect(screen.getByText(en.signInCode.prompt)).toBeInTheDocument();

    const code = screen.getByLabelText(/^Code/);
    expect(code).toHaveAttribute('id', 'code');
    expect(code).toHaveAttribute('name', 'code');
    expect(code).toHaveAttribute('inputmode', 'numeric');
    expect(code).toHaveAttribute('autocomplete', 'one-time-code');
    expect(code).toHaveFocus();
    expect(code).toBeRequired();

    const token = container.querySelector(
      'input[name="__RequestVerificationToken"]',
    ) as HTMLInputElement | null;
    expect(token?.type).toBe('hidden');
    expect(token?.value).toBe('tok');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('offers a verify button and a resend button that skips the form validation', () => {
    const { container } = renderPage({ antiforgeryToken: 'tok' });

    const verify = screen.getByRole('button', { name: en.signInCode.verify });
    expect(verify).toHaveAttribute('id', 'verify-code');
    expect(verify).toHaveAttribute('name', 'action');
    expect(verify).toHaveAttribute('value', 'verify');
    expect(verify).toHaveAttribute('type', 'submit');
    expect(verify).not.toHaveAttribute('formnovalidate');

    const resend = screen.getByRole('button', { name: en.signInCode.resend });
    expect(resend).toHaveAttribute('id', 'resend-code');
    expect(resend).toHaveAttribute('name', 'action');
    expect(resend).toHaveAttribute('value', 'resend');
    expect(resend).toHaveAttribute('type', 'submit');
    expect(resend).toHaveAttribute('formnovalidate');

    const buttons = [...container.querySelectorAll('button')];
    expect(buttons[0]).toBe(verify);
  });

  it('confirms that a new code was sent', () => {
    renderPage({ antiforgeryToken: 'tok', sent: true });

    expect(screen.getByRole('alert')).toHaveTextContent(en.signInCode.sent);
  });

  it('shows no confirmation when no code was sent', () => {
    renderPage({ antiforgeryToken: 'tok', sent: false });

    expect(screen.queryByText(en.signInCode.sent)).not.toBeInTheDocument();
  });

  it('shows the error message in an alert when one is provided', () => {
    renderPage({ antiforgeryToken: 'tok', errorMessage: 'The code is incorrect or has expired.' });

    expect(screen.getByRole('alert')).toHaveTextContent('The code is incorrect or has expired.');
  });

  it.each([
    ['null', { errorMessage: null }],
    ['empty', { errorMessage: '' }],
  ] as const)('shows no alert when the error message is %s', (_label, extra) => {
    renderPage({ antiforgeryToken: 'tok', ...extra });

    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
});
