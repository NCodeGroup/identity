import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { LocalAccountPasswordDialog } from './LocalAccountPasswordDialog.js';

const change = (id: string, value: string) =>
  fireEvent.change(screen.getByTestId(id), { target: { value } });

describe('LocalAccountPasswordDialog', () => {
  it('names the account and requires a matching password', () => {
    const onSubmit = vi.fn();
    render(
      <LocalAccountPasswordDialog
        open
        accountLabel="Ada"
        onSubmit={onSubmit}
        onCancel={vi.fn()}
      />,
    );
    expect(
      screen.getByText('Resetting the password of Ada ends their sessions.'),
    ).toBeInTheDocument();
    expect(screen.getByTestId('local-account-password-input')).toHaveAttribute(
      'autocomplete',
      'new-password',
    );
    const submit = screen.getByTestId('local-account-password-submit');
    expect(submit).toHaveTextContent('Reset password');
    expect(submit).toBeDisabled();
    fireEvent.click(submit);
    expect(onSubmit).not.toHaveBeenCalled();

    change('local-account-password-input', 'secret');
    expect(submit).toBeDisabled();
    expect(screen.queryByText('The passwords do not match.')).not.toBeInTheDocument();

    change('local-account-password-confirm', 'other');
    expect(screen.getByText('The passwords do not match.')).toBeInTheDocument();
    expect(submit).toBeDisabled();

    change('local-account-password-confirm', 'secret');
    expect(screen.queryByText('The passwords do not match.')).not.toBeInTheDocument();
    expect(submit).toBeEnabled();
    fireEvent.click(submit);
    expect(onSubmit).toHaveBeenCalledWith('secret');
  });

  it('surfaces a server error and blocks actions while submitting', () => {
    render(
      <LocalAccountPasswordDialog
        open
        accountLabel="Ada"
        isSubmitting
        error="Boom"
        onSubmit={vi.fn()}
        onCancel={vi.fn()}
      />,
    );
    expect(screen.getByTestId('local-account-password-error')).toHaveTextContent('Boom');
    expect(screen.getByTestId('local-account-password-submit')).toBeDisabled();
    expect(screen.getByTestId('local-account-password-submit')).toHaveTextContent('Saving…');
    expect(screen.getByTestId('local-account-password-cancel')).toBeDisabled();
  });

  it('clears the fields on reopen and keeps them while open', () => {
    const props = { accountLabel: 'Ada', onSubmit: vi.fn(), onCancel: vi.fn() };
    const { rerender } = render(<LocalAccountPasswordDialog {...props} open />);
    change('local-account-password-input', 'secret');
    rerender(<LocalAccountPasswordDialog {...props} open />);
    expect(screen.getByTestId('local-account-password-input')).toHaveValue('secret');

    rerender(<LocalAccountPasswordDialog {...props} open={false} />);
    rerender(<LocalAccountPasswordDialog {...props} open />);
    expect(screen.getByTestId('local-account-password-input')).toHaveValue('');
    expect(screen.getByTestId('local-account-password-confirm')).toHaveValue('');
  });

  it('invokes onCancel from the cancel button', () => {
    const onCancel = vi.fn();
    render(
      <LocalAccountPasswordDialog
        open
        accountLabel="Ada"
        onSubmit={vi.fn()}
        onCancel={onCancel}
      />,
    );
    fireEvent.click(screen.getByTestId('local-account-password-cancel'));
    expect(onCancel).toHaveBeenCalledOnce();
  });
});
