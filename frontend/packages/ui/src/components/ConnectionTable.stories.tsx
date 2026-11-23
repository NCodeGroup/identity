import type { Meta, StoryObj } from '@storybook/react-vite';
import { ConnectionTable } from './ConnectionTable.js';

const meta: Meta<typeof ConnectionTable> = {
  title: 'Management/ConnectionTable',
  component: ConnectionTable,
};
export default meta;

type Story = StoryObj<typeof ConnectionTable>;

const connections = [
  {
    connectionId: 'cn-local',
    name: 'local',
    displayName: 'Local accounts',
    strategy: 'local',
    isEnabled: true,
  },
  {
    connectionId: 'cn-staff',
    name: 'staff',
    displayName: 'Staff',
    strategy: 'local',
    isEnabled: false,
  },
];

export const Populated: Story = { args: { connections } };

export const Empty: Story = { args: { connections: [] } };

export const WithActions: Story = {
  args: {
    connections,
    onSelect: () => undefined,
    onEdit: () => undefined,
    onDelete: () => undefined,
  },
};
