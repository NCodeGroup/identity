import type { Meta, StoryObj } from '@storybook/react-vite';
import { expect, fireEvent, screen, waitFor } from 'storybook/test';
import {
  TenantMoveDialog,
  type TenantMoveDialogCell,
  type TenantMoveDialogOperations,
} from './TenantMoveDialog.js';

const cells: readonly TenantMoveDialogCell[] = [
  { name: 'eu-1', region: 'eu' },
  { name: 'eu-2', region: 'eu' },
  { name: 'eu-3', region: 'eu' },
  { name: 'us-1', region: 'us' },
];

const jwk = { kty: 'EC', crv: 'P-256' };

const succeeding: TenantMoveDialogOperations = {
  createTransferKey: async () => ({ kid: 'k-1', jwk }),
  exportTenant: async () => 'sealed',
  importTenant: async () => undefined,
  releaseMovedTenant: async () => undefined,
};

const meta: Meta<typeof TenantMoveDialog> = {
  title: 'Management/TenantMoveDialog',
  component: TenantMoveDialog,
  args: {
    open: true,
    tenantId: 'acme',
    sourceCell: 'eu-1',
    cells,
    operations: succeeding,
    onClose: () => undefined,
  },
};
export default meta;

type Story = StoryObj<typeof TenantMoveDialog>;

/** Chooses eu-2 as the target cell and presses Move, as a viewer would. */
async function startMove(): Promise<void> {
  await fireEvent.change(await screen.findByTestId('move-target'), { target: { value: 'eu-2' } });
  await waitFor(() => expect(screen.getByTestId('move-confirm')).toBeEnabled());
  await fireEvent.click(screen.getByTestId('move-confirm'));
}

export const Default: Story = {};

export const NoOtherCell: Story = {
  args: { sourceCell: 'us-1' },
};

export const Running: Story = {
  args: {
    operations: { ...succeeding, exportTenant: () => new Promise<string>(() => undefined) },
  },
  play: async () => {
    await startMove();
    await screen.findByText('running');
  },
};

export const Moved: Story = {
  play: async () => {
    await startMove();
    await screen.findByTestId('move-done');
  },
};

export const FailedImport: Story = {
  args: {
    operations: {
      ...succeeding,
      importTenant: () =>
        Promise.reject(new Error('The transfer key is unknown, expired or already used.')),
    },
  },
  play: async () => {
    await startMove();
    await screen.findByTestId('move-failure');
  },
};

export const FailedRelease: Story = {
  args: {
    operations: {
      ...succeeding,
      releaseMovedTenant: () =>
        Promise.reject(new Error('The global directory is unavailable. Retry the request.')),
    },
  },
  play: async () => {
    await startMove();
    await screen.findByTestId('move-failure');
  },
};
