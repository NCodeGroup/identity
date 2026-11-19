import type { Meta, StoryObj } from '@storybook/react-vite';
import Button from '@mui/material/Button';
import CardContent from '@mui/material/CardContent';
import Typography from '@mui/material/Typography';
import { TenantCard } from './TenantCard.js';

const meta: Meta<typeof TenantCard> = {
  title: 'Components/TenantCard',
  component: TenantCard,
};
export default meta;

type Story = StoryObj<typeof TenantCard>;

export const SignIn: Story = {
  args: {
    maxWidth: 400,
    children: (
      <CardContent>
        <Typography variant="h5">Sign in</Typography>
        <Button variant="contained">Continue</Button>
      </CardContent>
    ),
  },
};
