import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { LocalAccountTable, type LocalAccountRow } from './LocalAccountTable.js';

const accounts: LocalAccountRow[] = [
  {
    localAccountId: 'a1',
    displayName: 'Ada Lovelace',
    userName: 'ada',
    email: 'ada@example.com',
    principalId: 'p-1',
    isEnabled: true,
  },
  {
    localAccountId: 'b2',
    displayName: 'Bob Builder',
    userName: 'bob',
    email: null,
    principalId: null,
    isEnabled: false,
  },
];

describe('LocalAccountTable', () => {
  it('renders every column of each account', () => {
    render(<LocalAccountTable accounts={accounts} />);
    expect(screen.getByRole('table', { name: 'Users' })).toBeInTheDocument();
    for (const name of ['Display name', 'Username', 'Email', 'Principal ID', 'Status']) {
      expect(screen.getByRole('columnheader', { name })).toBeInTheDocument();
    }
    const row = screen.getByTestId('local-account-row-a1');
    expect(row).toHaveTextContent('Ada Lovelace');
    expect(row).toHaveTextContent('ada');
    expect(row).toHaveTextContent('ada@example.com');
    expect(row).toHaveTextContent('p-1');
    expect(row).toHaveTextContent('Active');
  });

  it('shows placeholders for a missing email and principal and marks disabled accounts', () => {
    render(<LocalAccountTable accounts={accounts} />);
    const row = screen.getByTestId('local-account-row-b2');
    expect(row).toHaveTextContent('——');
    expect(row).toHaveTextContent('Disabled');
  });

  it('shows no actions when no callbacks are supplied', () => {
    render(<LocalAccountTable accounts={accounts} />);
    expect(screen.queryByRole('columnheader', { name: 'Actions' })).not.toBeInTheDocument();
    expect(screen.queryByTestId('local-account-edit-a1')).not.toBeInTheDocument();
    expect(screen.queryByTestId('local-account-password-a1')).not.toBeInTheDocument();
    expect(screen.queryByTestId('local-account-toggle-a1')).not.toBeInTheDocument();
    expect(screen.queryByTestId('local-account-delete-a1')).not.toBeInTheDocument();
  });

  it('invokes each callback with the account id', () => {
    const onEdit = vi.fn();
    const onResetPassword = vi.fn();
    const onToggleEnabled = vi.fn();
    const onDelete = vi.fn();
    render(
      <LocalAccountTable
        accounts={accounts}
        onEdit={onEdit}
        onResetPassword={onResetPassword}
        onToggleEnabled={onToggleEnabled}
        onDelete={onDelete}
      />,
    );
    expect(screen.getByRole('columnheader', { name: 'Actions' })).toBeInTheDocument();
    fireEvent.click(screen.getByTestId('local-account-edit-a1'));
    fireEvent.click(screen.getByTestId('local-account-password-a1'));
    fireEvent.click(screen.getByTestId('local-account-toggle-a1'));
    fireEvent.click(screen.getByTestId('local-account-delete-b2'));
    expect(onEdit).toHaveBeenCalledWith('a1');
    expect(onResetPassword).toHaveBeenCalledWith('a1');
    expect(onToggleEnabled).toHaveBeenCalledWith('a1');
    expect(onDelete).toHaveBeenCalledWith('b2');
  });

  it('labels the toggle by the account state', () => {
    render(<LocalAccountTable accounts={accounts} onToggleEnabled={() => undefined} />);
    expect(screen.getByTestId('local-account-toggle-a1')).toHaveAccessibleName('Disable');
    expect(screen.getByTestId('local-account-toggle-b2')).toHaveAccessibleName('Enable');
  });

  it('shows only the supplied actions', () => {
    render(<LocalAccountTable accounts={accounts} onDelete={() => undefined} />);
    expect(screen.getByTestId('local-account-delete-a1')).toBeInTheDocument();
    expect(screen.queryByTestId('local-account-edit-a1')).not.toBeInTheDocument();
  });
});
