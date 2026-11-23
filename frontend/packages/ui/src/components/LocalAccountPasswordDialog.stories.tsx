import type { Meta, StoryObj } from '@storybook/react-vite';
import { LocalAccountPasswordDialog } from './LocalAccountPasswordDialog.js';

const meta: Meta<typeof LocalAccountPasswordDialog> = {
  title: 'Management/LocalAccountPasswordDialog',
  component: LocalAccountPasswordDialog,
  args: {
    open: true,
    accountLabel: 'Ada Lovelace',
    onSubmit: () => undefined,
    onCancel: () => undefined,
  },
};
export default meta;

type Story = StoryObj<typeof LocalAccountPasswordDialog>;

export const Default: Story = {};

export const WithError: Story = {
  args: { error: 'The password does not meet the policy.' },
};
