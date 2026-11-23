import type { Meta, StoryObj } from '@storybook/react-vite';
import { ConnectionFormDialog, emptyConnectionFormValues } from './ConnectionFormDialog.js';

const meta: Meta<typeof ConnectionFormDialog> = {
  title: 'Management/ConnectionFormDialog',
  component: ConnectionFormDialog,
  args: {
    open: true,
    onSubmit: () => undefined,
    onCancel: () => undefined,
  },
};
export default meta;

type Story = StoryObj<typeof ConnectionFormDialog>;

export const Create: Story = {
  args: {
    title: 'Create connection',
    submitLabel: 'Create',
    initialValues: emptyConnectionFormValues,
  },
};

export const Edit: Story = {
  args: {
    title: 'Edit connection',
    submitLabel: 'Save',
    nameEditable: false,
    initialValues: {
      name: 'staff',
      displayName: 'Staff',
      strategy: 'local',
      store: 'default',
      isEnabled: true,
    },
  },
};

export const WithError: Story = {
  args: {
    title: 'Create connection',
    submitLabel: 'Create',
    initialValues: emptyConnectionFormValues,
    error: 'A connection with that name already exists.',
  },
};
