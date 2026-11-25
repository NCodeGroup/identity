import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { AppThemeProvider } from '@ncodegroup/identity-ui';
import { RootAccountStep } from './RootAccountStep.js';

function renderStep(administratorRole = 'GlobalAdmin') {
  const onContinue = vi.fn();
  render(
    <AppThemeProvider>
      <RootAccountStep
        minimumPasswordLength={15}
        administratorRole={administratorRole}
        onContinue={onContinue}
      />
    </AppThemeProvider>,
  );
  return { onContinue };
}

function fill(label: RegExp, value: string): void {
  fireEvent.change(screen.getByLabelText(label), { target: { value } });
}

function fillAccount(password = 'a long passphrase!', confirmation = password): void {
  fill(/^Display name/, '  Root Admin  ');
  fill(/^User name/, '  root  ');
  fill(/^Password/, password);
  fill(/^Confirm password/, confirmation);
}

function submit(): void {
  fireEvent.click(screen.getByRole('button', { name: 'Continue' }));
}

describe('RootAccountStep', () => {
  it('titles the step for a cell administrator when the role is CellAdmin', () => {
    renderStep('CellAdmin');

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(
      'Create the cell administrator',
    );
  });

  it('renders the account fields with their autocomplete hints', () => {
    renderStep();

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(
      'Create the root administrator',
    );
    const displayName = screen.getByLabelText(/^Display name/);
    expect(displayName).toHaveAttribute('autocomplete', 'name');
    expect(displayName).toBeRequired();
    expect(displayName).toHaveAccessibleDescription(
      'The name the management console and your applications show.',
    );
    const userName = screen.getByLabelText(/^User name/);
    expect(userName).toHaveAttribute('autocomplete', 'username');
    expect(userName).toBeRequired();
    expect(screen.getByLabelText(/^Email/)).not.toBeRequired();
    const password = screen.getByLabelText(/^Password/);
    expect(password).toHaveAttribute('type', 'password');
    expect(password).toHaveAttribute('autocomplete', 'new-password');
    expect(password).toHaveAccessibleDescription('Use at least 15 characters.');
    const confirm = screen.getByLabelText(/^Confirm password/);
    expect(confirm).toHaveAttribute('type', 'password');
    expect(confirm).toHaveAttribute('autocomplete', 'new-password');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('refuses a password shorter than the minimum without continuing', () => {
    const { onContinue } = renderStep();

    fillAccount('too short', 'too short');
    submit();

    expect(screen.getByRole('alert')).toHaveTextContent(
      'The password must have at least 15 characters.',
    );
    expect(onContinue).not.toHaveBeenCalled();
  });

  it('refuses a confirmation that differs without continuing', () => {
    const { onContinue } = renderStep();

    fillAccount('a long passphrase!', 'another long passphrase!');
    submit();

    expect(screen.getByRole('alert')).toHaveTextContent(
      'The password and its confirmation do not match.',
    );
    expect(onContinue).not.toHaveBeenCalled();
  });

  it('refuses a blank display name without continuing', () => {
    const { onContinue } = renderStep();

    fillAccount();
    fill(/^Display name/, '   ');
    submit();

    expect(screen.getByRole('alert')).toHaveTextContent('The display name is required.');
    expect(onContinue).not.toHaveBeenCalled();
  });

  it('continues with the trimmed display name, the trimmed user name and omits a blank email', () => {
    const { onContinue } = renderStep();

    fillAccount();
    submit();

    expect(onContinue).toHaveBeenCalledExactlyOnceWith({
      displayName: 'Root Admin',
      userName: 'root',
      password: 'a long passphrase!',
    });
  });

  it('continues with the trimmed email when one is given and clears an earlier error', () => {
    const { onContinue } = renderStep();

    fillAccount('too short', 'too short');
    submit();
    expect(screen.getByRole('alert')).toBeInTheDocument();

    fillAccount();
    fill(/^Email/, '  root@example.com ');
    submit();

    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(onContinue).toHaveBeenCalledExactlyOnceWith({
      displayName: 'Root Admin',
      userName: 'root',
      email: 'root@example.com',
      password: 'a long passphrase!',
    });
  });
});
