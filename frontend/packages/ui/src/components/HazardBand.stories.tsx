import type { Meta, StoryObj } from '@storybook/react-vite';
import { HazardBand } from './HazardBand.js';

const meta: Meta<typeof HazardBand> = {
  title: 'Components/HazardBand',
  component: HazardBand,
};
export default meta;

type Story = StoryObj<typeof HazardBand>;

export const NonProduction: Story = {
  args: {
    children: 'staging environment — not for production use',
  },
};
