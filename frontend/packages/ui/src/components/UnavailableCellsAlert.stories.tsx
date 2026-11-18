import type { Meta, StoryObj } from '@storybook/react-vite';
import { UnavailableCellsAlert } from './UnavailableCellsAlert.js';

const meta: Meta<typeof UnavailableCellsAlert> = {
  title: 'Management/UnavailableCellsAlert',
  component: UnavailableCellsAlert,
};
export default meta;

type Story = StoryObj<typeof UnavailableCellsAlert>;

export const OneCell: Story = { args: { cells: ['eu-1'] } };
export const SeveralCells: Story = { args: { cells: ['eu-1', 'us-1'] } };
export const EveryCellAnswered: Story = { args: { cells: [] } };
