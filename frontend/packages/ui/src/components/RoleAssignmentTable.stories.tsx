import type { Meta, StoryObj } from '@storybook/react-vite';
import { RoleAssignmentTable } from './RoleAssignmentTable.js';

const meta: Meta<typeof RoleAssignmentTable> = {
  title: 'Management/RoleAssignmentTable',
  component: RoleAssignmentTable,
};
export default meta;

type Story = StoryObj<typeof RoleAssignmentTable>;

const assignments = [
  {
    assignmentId: 'a-1',
    principalId: 'alice',
    displayName: 'Alice',
    principalType: 'local_account',
  },
  {
    assignmentId: 'a-2',
    principalId: 'build-bot',
    displayName: 'Build bot',
    principalType: 'client',
  },
  { assignmentId: 'a-3', principalId: 'unknown-1', displayName: null, principalType: null },
];

export const Default: Story = {
  args: {
    label: 'Owners',
    testIdPrefix: 'owners',
    assignments,
    onRevoke: () => undefined,
  },
};

export const ReadOnly: Story = {
  args: { label: 'Owners', testIdPrefix: 'owners', assignments },
};

export const Empty: Story = {
  args: { label: 'Owners', testIdPrefix: 'owners', assignments: [] },
};
