import type { Meta, StoryObj } from '@storybook/react-vite';
import { LocalAccountTable } from './LocalAccountTable.js';

const meta: Meta<typeof LocalAccountTable> = {
  title: 'Management/LocalAccountTable',
  component: LocalAccountTable,
};
export default meta;

type Story = StoryObj<typeof LocalAccountTable>;

const accounts = [
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

export const Populated: Story = { args: { accounts } };

export const Empty: Story = { args: { accounts: [] } };

export const WithActions: Story = {
  args: {
    accounts,
    onEdit: () => undefined,
    onResetPassword: () => undefined,
    onToggleEnabled: () => undefined,
    onDelete: () => undefined,
  },
};
