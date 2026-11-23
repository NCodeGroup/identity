import type { Meta, StoryObj } from '@storybook/react-vite';
import { LocalAccountClaimsEditor } from './LocalAccountClaimsEditor.js';

const meta: Meta<typeof LocalAccountClaimsEditor> = {
  title: 'Management/LocalAccountClaimsEditor',
  component: LocalAccountClaimsEditor,
};
export default meta;

type Story = StoryObj<typeof LocalAccountClaimsEditor>;

export const Empty: Story = {
  args: { claims: [], onSave: () => undefined },
};

export const WithClaims: Story = {
  args: {
    claims: [
      { type: 'locale', value: 'en-US' },
      { type: 'zoneinfo', value: 'UTC' },
    ],
    onSave: () => undefined,
  },
};
