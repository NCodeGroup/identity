import type { Meta, StoryObj } from '@storybook/react-vite';
import Box from '@mui/material/Box';
import { Brace } from './Brace.js';

const meta: Meta<typeof Brace> = {
  title: 'Components/Brace',
  component: Brace,
  // A brace takes the full height of its container, so each story stands it in a box of fixed height in the tenant's
  // scope ink, as TenantCard draws it.
  render: (args) => (
    <Box sx={(theme) => ({ height: 240, color: theme.palette.instrument['scope-tenant'] })}>
      <Brace {...args} />
    </Box>
  ),
};
export default meta;

type Story = StoryObj<typeof Brace>;

export const Small: Story = {
  args: { size: 'sm' },
};

export const Medium: Story = {
  args: { size: 'md' },
};

export const Large: Story = {
  args: { size: 'lg' },
};
