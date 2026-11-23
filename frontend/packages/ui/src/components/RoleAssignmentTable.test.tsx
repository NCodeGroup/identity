import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { RoleAssignmentTable, type RoleAssignmentRow } from './RoleAssignmentTable.js';

const assignments: RoleAssignmentRow[] = [
  { assignmentId: 'a-1', principalId: 'alice', displayName: null, principalType: null },
  { assignmentId: 'a-2', principalId: 'bob', displayName: null, principalType: null },
];

describe('RoleAssignmentTable', () => {
  it('renders a row per assignment with its principal id', () => {
    render(<RoleAssignmentTable label="Owners" testIdPrefix="owners" assignments={assignments} />);
    expect(screen.getByRole('table', { name: 'Owners' })).toBeInTheDocument();
    expect(screen.getByTestId('owners-row-alice')).toHaveTextContent('alice');
    expect(screen.getByTestId('owners-row-bob')).toHaveTextContent('bob');
  });

  it('renders no actions column without onRevoke', () => {
    render(<RoleAssignmentTable label="Owners" testIdPrefix="owners" assignments={assignments} />);
    expect(screen.queryByRole('columnheader', { name: 'Actions' })).not.toBeInTheDocument();
    expect(screen.queryByTestId('owners-revoke-alice')).not.toBeInTheDocument();
  });

  it('invokes onRevoke with the principal id', () => {
    const onRevoke = vi.fn();
    render(
      <RoleAssignmentTable
        label="Owners"
        testIdPrefix="owners"
        assignments={assignments}
        onRevoke={onRevoke}
      />,
    );
    expect(screen.getByRole('columnheader', { name: 'Actions' })).toBeInTheDocument();
    fireEvent.click(screen.getByTestId('owners-revoke-bob'));
    expect(onRevoke).toHaveBeenCalledExactlyOnceWith('bob');
  });

  it('names each holder', () => {
    render(
      <RoleAssignmentTable
        label="Owners"
        testIdPrefix="owners"
        assignments={[
          {
            assignmentId: 'a-1',
            principalId: 'u-1',
            displayName: 'Alice',
            principalType: 'local_account',
          },
          {
            assignmentId: 'a-2',
            principalId: 'c-1',
            displayName: 'Build bot',
            principalType: 'client',
          },
          { assignmentId: 'a-3', principalId: 'ghost', displayName: null, principalType: null },
        ]}
        onRevoke={vi.fn()}
      />,
    );
    expect(screen.getByTestId('owners-name-u-1')).toHaveTextContent('Alice');
    expect(screen.getByTestId('owners-row-u-1')).toHaveTextContent('User · u-1');
    expect(screen.getByTestId('owners-row-c-1')).toHaveTextContent('Machine client · c-1');
    expect(screen.getByTestId('owners-name-ghost')).toHaveTextContent('ghost');
    expect(screen.getByTestId('owners-row-ghost')).toHaveTextContent(/^ghost$/);
    expect(screen.getByRole('button', { name: 'Revoke Alice' })).toBeInTheDocument();
  });

  it('captions a federated user', () => {
    render(
      <RoleAssignmentTable
        label="Owners"
        testIdPrefix="owners"
        assignments={[
          {
            assignmentId: 'a-4',
            principalId: 'f-1',
            displayName: 'Frank',
            principalType: 'federated_user',
          },
        ]}
      />,
    );
    expect(screen.getByTestId('owners-row-f-1')).toHaveTextContent('Federated user · f-1');
  });

  it('captions a named holder without a type by its id alone', () => {
    render(
      <RoleAssignmentTable
        label="Owners"
        testIdPrefix="owners"
        assignments={[
          { assignmentId: 'a-5', principalId: 'n-1', displayName: 'Nina', principalType: null },
        ]}
      />,
    );
    expect(screen.getByTestId('owners-row-n-1')).toHaveTextContent(/^Ninan-1$/);
  });

  it('captions a holder of an unknown type by the type and id', () => {
    render(
      <RoleAssignmentTable
        label="Owners"
        testIdPrefix="owners"
        assignments={[
          { assignmentId: 'a-6', principalId: 'x-1', displayName: 'Xena', principalType: 'robot' },
        ]}
      />,
    );
    expect(screen.getByTestId('owners-row-x-1')).toHaveTextContent('robot · x-1');
  });
});
