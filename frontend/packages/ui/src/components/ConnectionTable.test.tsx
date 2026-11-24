import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { ConnectionTable, type ConnectionRow } from './ConnectionTable.js';

const connections: ConnectionRow[] = [
  {
    connectionId: 'cn-local',
    name: 'local',
    displayName: 'Local accounts',
    strategy: 'local',
    isSystem: true,
    isEnabled: true,
  },
  {
    connectionId: 'cn-staff',
    name: 'staff',
    displayName: 'Staff',
    strategy: 'local',
    isSystem: false,
    isEnabled: false,
  },
];

describe('ConnectionTable', () => {
  it('renders a row per connection with its name, display name, strategy and enabled state', () => {
    render(<ConnectionTable connections={connections} />);
    const row = screen.getByTestId('connection-row-cn-local');
    expect(row).toHaveTextContent('local');
    expect(row).toHaveTextContent('Local accounts');
    expect(row).toHaveTextContent('Enabled');
    expect(screen.getByTestId('connection-row-cn-staff')).toHaveTextContent('Disabled');
  });

  it('renders no actions column without callbacks', () => {
    render(<ConnectionTable connections={connections} />);
    expect(screen.queryByTestId('connection-edit-cn-local')).not.toBeInTheDocument();
  });

  it('invokes onSelect, onEdit, and onDelete with the connection id', () => {
    const onSelect = vi.fn();
    const onEdit = vi.fn();
    const onDelete = vi.fn();
    render(
      <ConnectionTable
        connections={connections}
        onSelect={onSelect}
        onEdit={onEdit}
        onDelete={onDelete}
      />,
    );
    fireEvent.click(screen.getByTestId('connection-row-cn-staff'));
    fireEvent.click(screen.getByTestId('connection-edit-cn-staff'));
    fireEvent.click(screen.getByTestId('connection-delete-cn-staff'));
    expect(onSelect).toHaveBeenCalledOnce();
    expect(onSelect).toHaveBeenCalledWith('cn-staff');
    expect(onEdit).toHaveBeenCalledWith('cn-staff');
    expect(onDelete).toHaveBeenCalledWith('cn-staff');
  });

  it('marks a system connection and offers no delete control for it', () => {
    render(<ConnectionTable connections={connections} onEdit={vi.fn()} onDelete={vi.fn()} />);
    expect(screen.getByTestId('connection-row-cn-local')).toHaveTextContent('System');
    expect(screen.getByTestId('connection-row-cn-staff')).not.toHaveTextContent('System');
    expect(screen.queryByTestId('connection-delete-cn-local')).not.toBeInTheDocument();
    expect(screen.getByTestId('connection-delete-cn-staff')).toBeInTheDocument();
  });
});
