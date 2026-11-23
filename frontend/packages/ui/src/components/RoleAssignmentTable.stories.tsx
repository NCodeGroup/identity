import type { Meta, StoryObj } from '@storybook/react-vite';
import { PRINCIPAL_TYPES } from '@ncodegroup/identity-api';
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
    principalType: PRINCIPAL_TYPES.localAccount,
  },
  {
    assignmentId: 'a-2',
    principalId: 'build-bot',
    displayName: 'Build bot',
    principalType: PRINCIPAL_TYPES.client,
  },
  {
    assignmentId: 'a-4',
    principalId: 'frank',
    displayName: 'Frank',
    principalType: PRINCIPAL_TYPES.federatedUser,
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
