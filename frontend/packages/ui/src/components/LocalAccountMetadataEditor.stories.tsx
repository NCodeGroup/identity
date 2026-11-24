import type { Meta, StoryObj } from '@storybook/react-vite';
import { LocalAccountMetadataEditor } from './LocalAccountMetadataEditor.js';

const meta: Meta<typeof LocalAccountMetadataEditor> = {
  title: 'Management/LocalAccountMetadataEditor',
  component: LocalAccountMetadataEditor,
};
export default meta;

type Story = StoryObj<typeof LocalAccountMetadataEditor>;

export const Empty: Story = {
  args: { profileJson: '{}', systemJson: '{}', onSave: () => undefined },
};

export const Populated: Story = {
  args: {
    profileJson: '{\n  "plan": "pro"\n}',
    systemJson: '{\n  "riskScore": 3\n}',
    onSave: () => undefined,
  },
};
