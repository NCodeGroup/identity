import type { Meta, StoryObj } from '@storybook/react-vite';
import { LocalAccountFormDialog, emptyLocalAccountFormValues } from './LocalAccountFormDialog.js';

const meta: Meta<typeof LocalAccountFormDialog> = {
  title: 'Management/LocalAccountFormDialog',
  component: LocalAccountFormDialog,
  args: {
    open: true,
    onSubmit: () => undefined,
    onCancel: () => undefined,
  },
};
export default meta;

type Story = StoryObj<typeof LocalAccountFormDialog>;

export const Create: Story = {
  args: {
    mode: 'create',
    title: 'Create local account',
    submitLabel: 'Create',
    initialValues: emptyLocalAccountFormValues,
  },
};

export const Edit: Story = {
  args: {
    mode: 'edit',
    title: 'Edit local account',
    submitLabel: 'Save',
    initialValues: {
      ...emptyLocalAccountFormValues,
      displayName: 'Ada Lovelace',
      userName: 'ada',
      email: 'ada@example.com',
      emailVerified: true,
    },
  },
};
