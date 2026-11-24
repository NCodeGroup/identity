import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { LocalAccountFormDialog, emptyLocalAccountFormValues } from './LocalAccountFormDialog.js';

const ada = {
  ...emptyLocalAccountFormValues,
  displayName: 'Ada',
  userName: 'ada',
  email: 'ada@example.com',
  emailVerified: true,
};

const change = (id: string, value: string) =>
  fireEvent.change(screen.getByTestId(id), { target: { value } });

describe('LocalAccountFormDialog', () => {
  it('seeds the fields and omits the password fields in edit mode', () => {
    render(
      <LocalAccountFormDialog
        open
        mode="edit"
        title="Edit"
        submitLabel="Save"
        initialValues={ada}
        onSubmit={vi.fn()}
        onCancel={vi.fn()}
      />,
    );
    expect(screen.getByTestId('local-account-display-name')).toHaveValue('Ada');
    expect(screen.getByTestId('local-account-user-name')).toHaveValue('ada');
    expect(screen.getByTestId('local-account-email')).toHaveValue('ada@example.com');
    expect(screen.getByTestId('local-account-email-verified')).toBeChecked();
    expect(screen.queryByTestId('local-account-password')).not.toBeInTheDocument();
    expect(screen.getByTestId('local-account-form-submit')).toBeEnabled();
  });

  it('requires a password that matches its confirmation in create mode', () => {
    render(
      <LocalAccountFormDialog
        open
        mode="create"
        title="Create"
        submitLabel="Create"
        initialValues={emptyLocalAccountFormValues}
        onSubmit={vi.fn()}
        onCancel={vi.fn()}
      />,
    );
    const submit = screen.getByTestId('local-account-form-submit');
    expect(submit).toBeDisabled();
    expect(screen.getByTestId('local-account-password')).toHaveAttribute(
      'autocomplete',
      'new-password',
    );

    change('local-account-display-name', 'New');
    change('local-account-user-name', 'new');
    expect(submit).toBeDisabled();

    change('local-account-password', 'secret');
    expect(submit).toBeDisabled();
    expect(screen.queryByText('The passwords do not match.')).not.toBeInTheDocument();

    change('local-account-confirm-password', 'other');
    expect(screen.getByText('The passwords do not match.')).toBeInTheDocument();
    expect(submit).toBeDisabled();

    change('local-account-confirm-password', 'secret');
    expect(screen.queryByText('The passwords do not match.')).not.toBeInTheDocument();
    expect(submit).toBeEnabled();

    change('local-account-user-name', '   ');
    expect(submit).toBeDisabled();
  });

  it('submits trimmed values', () => {
    const onSubmit = vi.fn();
    render(
      <LocalAccountFormDialog
        open
        mode="create"
        title="Create"
        submitLabel="Create"
        initialValues={emptyLocalAccountFormValues}
        onSubmit={onSubmit}
        onCancel={vi.fn()}
      />,
    );
    change('local-account-display-name', '  Ada  ');
    change('local-account-user-name', ' ada ');
    change('local-account-email', ' ada@example.com ');
    fireEvent.click(screen.getByTestId('local-account-email-verified'));
    change('local-account-password', 'secret');
    change('local-account-confirm-password', 'secret');
    fireEvent.click(screen.getByTestId('local-account-form-submit'));
    expect(onSubmit).toHaveBeenCalledWith({
      displayName: 'Ada',
      userName: 'ada',
      email: 'ada@example.com',
      emailVerified: true,
      password: 'secret',
      confirmPassword: 'secret',
    });
  });

  it('submits an email-only account with a blank user name', () => {
    const onSubmit = vi.fn();
    render(
      <LocalAccountFormDialog
        open
        mode="create"
        title="Create"
        submitLabel="Create"
        initialValues={emptyLocalAccountFormValues}
        onSubmit={onSubmit}
        onCancel={vi.fn()}
      />,
    );
    change('local-account-display-name', 'Ada');
    change('local-account-email', 'ada@example.com');
    change('local-account-password', 'secret');
    change('local-account-confirm-password', 'secret');
    fireEvent.click(screen.getByTestId('local-account-form-submit'));
    expect(onSubmit).toHaveBeenCalledWith(
      expect.objectContaining({ userName: '', email: 'ada@example.com' }),
    );
  });

  it('surfaces a server error and blocks submit while submitting', () => {
    render(
      <LocalAccountFormDialog
        open
        mode="edit"
        title="Edit"
        submitLabel="Save"
        initialValues={ada}
        isSubmitting
        error="Boom"
        onSubmit={vi.fn()}
        onCancel={vi.fn()}
      />,
    );
    expect(screen.getByTestId('local-account-form-error')).toHaveTextContent('Boom');
    expect(screen.getByTestId('local-account-form-submit')).toBeDisabled();
    expect(screen.getByTestId('local-account-form-submit')).toHaveTextContent('Saving…');
    expect(screen.getByTestId('local-account-form-cancel')).toBeDisabled();
  });

  it('does not submit when the form is invalid', () => {
    const onSubmit = vi.fn();
    render(
      <LocalAccountFormDialog
        open
        mode="create"
        title="Create"
        submitLabel="Create"
        initialValues={emptyLocalAccountFormValues}
        onSubmit={onSubmit}
        onCancel={vi.fn()}
      />,
    );
    fireEvent.click(screen.getByTestId('local-account-form-submit'));
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('keeps in-progress edits while open and reseeds from initialValues on reopen', () => {
    const props = {
      mode: 'edit' as const,
      title: 'Edit',
      submitLabel: 'Save',
      onSubmit: vi.fn(),
      onCancel: vi.fn(),
    };
    const { rerender } = render(<LocalAccountFormDialog {...props} open initialValues={ada} />);
    change('local-account-display-name', 'Unsaved edit');

    rerender(<LocalAccountFormDialog {...props} open initialValues={{ ...ada }} />);
    expect(screen.getByTestId('local-account-display-name')).toHaveValue('Unsaved edit');

    rerender(<LocalAccountFormDialog {...props} open={false} initialValues={ada} />);
    rerender(
      <LocalAccountFormDialog {...props} open initialValues={{ ...ada, displayName: 'Grace' }} />,
    );
    expect(screen.getByTestId('local-account-display-name')).toHaveValue('Grace');
  });

  it('invokes onCancel from the cancel button', () => {
    const onCancel = vi.fn();
    render(
      <LocalAccountFormDialog
        open
        mode="create"
        title="Create"
        submitLabel="Create"
        initialValues={emptyLocalAccountFormValues}
        onSubmit={vi.fn()}
        onCancel={onCancel}
      />,
    );
    fireEvent.click(screen.getByTestId('local-account-form-cancel'));
    expect(onCancel).toHaveBeenCalledOnce();
  });
});
