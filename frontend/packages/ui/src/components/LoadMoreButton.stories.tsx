import type { Meta, StoryObj } from '@storybook/react-vite';
import { LoadMoreButton } from './LoadMoreButton.js';

const meta: Meta<typeof LoadMoreButton> = {
  title: 'Components/LoadMoreButton',
  component: LoadMoreButton,
  args: { hasMore: true, isLoading: false, onLoadMore: () => undefined },
};
export default meta;

type Story = StoryObj<typeof LoadMoreButton>;

export const Idle: Story = {};

export const Loading: Story = {
  args: { isLoading: true },
};
