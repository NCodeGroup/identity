import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/react';
import {
  ConnectionFormDialog,
  emptyConnectionFormValues,
  isValidConnectionName,
} from './ConnectionFormDialog.js';

describe('isValidConnectionName', () => {
  it('accepts letters, digits, - and _ after a letter or digit, up to 128 characters', () => {
    expect(isValidConnectionName('staff-1_a')).toBe(true);
    expect(isValidConnectionName('a'.repeat(128))).toBe(true);
    expect(isValidConnectionName('a'.repeat(129))).toBe(false);
    expect(isValidConnectionName('_staff')).toBe(false);
    expect(isValidConnectionName('-staff')).toBe(false);
    expect(isValidConnectionName('sta ff')).toBe(false);
    expect(isValidConnectionName('')).toBe(false);
  });
});

describe('ConnectionFormDialog', () => {
  it('submits the trimmed name, display name, strategy, store and enabled toggle', () => {
    const onSubmit = vi.fn();
    render(
      <ConnectionFormDialog
        open
        title="Create connection"
        submitLabel="Create"
        initialValues={emptyConnectionFormValues}
        onSubmit={onSubmit}
        onCancel={vi.fn()}
      />,
    );
    fireEvent.change(screen.getByTestId('connection-name'), { target: { value: '  staff  ' } });
    fireEvent.change(screen.getByTestId('connection-display-name'), {
      target: { value: '  Staff  ' },
    });
    fireEvent.change(screen.getByTestId('connection-store'), { target: { value: ' staff-db ' } });
    fireEvent.click(screen.getByRole('switch', { name: 'Enabled' }));
    fireEvent.click(screen.getByTestId('connection-form-submit'));
    expect(onSubmit).toHaveBeenCalledExactlyOnceWith({
      name: 'staff',
      displayName: 'Staff',
      strategy: 'local',
      store: 'staff-db',
      isEnabled: false,
    });
  });

  it('blocks submit until the name is valid and the display name is provided', () => {
    render(
      <ConnectionFormDialog
        open
        title="Create connection"
        submitLabel="Create"
        initialValues={emptyConnectionFormValues}
        onSubmit={vi.fn()}
        onCancel={vi.fn()}
      />,
    );
    expect(screen.getByTestId('connection-form-submit')).toBeDisabled();
    expect(screen.getByTestId('connection-store')).toBeEnabled();
    fireEvent.change(screen.getByTestId('connection-name'), { target: { value: 'bad name' } });
    fireEvent.change(screen.getByTestId('connection-display-name'), { target: { value: 'Staff' } });
    expect(screen.getByTestId('connection-form-submit')).toBeDisabled();
    fireEvent.change(screen.getByTestId('connection-name'), { target: { value: 'staff' } });
    expect(screen.getByTestId('connection-form-submit')).toBeEnabled();
    fireEvent.change(screen.getByTestId('connection-store'), { target: { value: '  ' } });
    expect(screen.getByTestId('connection-form-submit')).toBeDisabled();
  });

  it('submits the strategy picked from the strategy select', () => {
    const onSubmit = vi.fn();
    render(
      <ConnectionFormDialog
        open
        title="Create connection"
        submitLabel="Create"
        strategies={['local', 'corp']}
        initialValues={{ ...emptyConnectionFormValues, name: 'staff', displayName: 'Staff' }}
        onSubmit={onSubmit}
        onCancel={vi.fn()}
      />,
    );
    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Strategy' }));
    fireEvent.click(within(screen.getByRole('listbox')).getByRole('option', { name: 'corp' }));
    fireEvent.click(screen.getByTestId('connection-form-submit'));
    expect(onSubmit).toHaveBeenCalledExactlyOnceWith({
      name: 'staff',
      displayName: 'Staff',
      strategy: 'corp',
      store: 'default',
      isEnabled: true,
    });
  });

  it('locks the name and strategy when they are not editable', () => {
    const onSubmit = vi.fn();
    render(
      <ConnectionFormDialog
        open
        title="Edit connection"
        submitLabel="Save"
        nameEditable={false}
        initialValues={{
          name: 'staff',
          displayName: 'Staff',
          strategy: 'local',
          store: 'default',
          isEnabled: true,
        }}
        onSubmit={onSubmit}
        onCancel={vi.fn()}
      />,
    );
    expect(screen.getByTestId('connection-name')).toBeDisabled();
    expect(screen.getByTestId('connection-store')).toBeDisabled();
    expect(screen.getByText('The name is immutable after creation.')).toBeInTheDocument();
    fireEvent.change(screen.getByTestId('connection-display-name'), {
      target: { value: ' Staff members ' },
    });
    fireEvent.click(screen.getByTestId('connection-form-submit'));
    expect(onSubmit).toHaveBeenCalledExactlyOnceWith({
      name: 'staff',
      displayName: 'Staff members',
      strategy: 'local',
      store: 'default',
      isEnabled: true,
    });
  });

  it('surfaces a server error and blocks actions while submitting', () => {
    render(
      <ConnectionFormDialog
        open
        title="Create connection"
        submitLabel="Create"
        initialValues={emptyConnectionFormValues}
        isSubmitting
        error="Boom"
        onSubmit={vi.fn()}
        onCancel={vi.fn()}
      />,
    );
    expect(screen.getByTestId('connection-form-error')).toHaveTextContent('Boom');
    expect(screen.getByTestId('connection-form-submit')).toBeDisabled();
    expect(screen.getByTestId('connection-form-submit')).toHaveTextContent('Saving…');
    expect(screen.getByTestId('connection-form-cancel')).toBeDisabled();
  });

  it('invokes onCancel from the cancel button', () => {
    const onCancel = vi.fn();
    const onSubmit = vi.fn();
    render(
      <ConnectionFormDialog
        open
        title="Create connection"
        submitLabel="Create"
        initialValues={emptyConnectionFormValues}
        onSubmit={onSubmit}
        onCancel={onCancel}
      />,
    );
    fireEvent.click(screen.getByTestId('connection-form-cancel'));
    expect(onCancel).toHaveBeenCalledOnce();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('reseeds from initialValues on reopen', () => {
    const props = {
      title: 'Edit connection',
      submitLabel: 'Save',
      nameEditable: false,
      onSubmit: vi.fn(),
      onCancel: vi.fn(),
    };
    const first = {
      name: 'a',
      displayName: 'A',
      strategy: 'local',
      store: 'default',
      isEnabled: true,
    };
    const { rerender } = render(<ConnectionFormDialog {...props} open initialValues={first} />);
    fireEvent.change(screen.getByTestId('connection-display-name'), {
      target: { value: 'Unsaved' },
    });
    rerender(<ConnectionFormDialog {...props} open initialValues={first} />);
    expect(screen.getByTestId('connection-display-name')).toHaveValue('Unsaved');
    rerender(<ConnectionFormDialog {...props} open={false} initialValues={first} />);
    rerender(
      <ConnectionFormDialog
        {...props}
        open
        initialValues={{ ...first, name: 'b', displayName: 'B', isEnabled: false }}
      />,
    );
    expect(screen.getByTestId('connection-name')).toHaveValue('b');
    expect(screen.getByTestId('connection-display-name')).toHaveValue('B');
    expect(screen.getByRole('switch', { name: 'Enabled' })).not.toBeChecked();
  });
});
