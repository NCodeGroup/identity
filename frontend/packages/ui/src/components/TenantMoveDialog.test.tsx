import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import {
  TenantMoveDialog,
  type TenantMoveDialogCell,
  type TenantMoveDialogOperations,
  type TenantMoveDialogProps,
} from './TenantMoveDialog.js';
import { TenantMoveOutcomeUnknownError, TenantPlacedElsewhereError } from './tenantMove.js';

const cells: readonly TenantMoveDialogCell[] = [
  { name: 'eu-1', region: 'eu' },
  { name: 'eu-2', region: 'eu' },
  { name: 'eu-3', region: 'eu' },
  { name: 'us-1', region: 'us' },
];

const jwk = { kty: 'EC', crv: 'P-256' };

function createOperations(calls: string[] = []): TenantMoveDialogOperations {
  return {
    createTransferKey: vi.fn(async (cell: string) => {
      calls.push(`transfer-key ${cell}`);
      return { kid: 'k-1', jwk };
    }),
    exportTenant: vi.fn(async (cell: string, tenantId: string) => {
      calls.push(`export ${cell} ${tenantId}`);
      return 'sealed';
    }),
    importTenant: vi.fn(async (cell: string) => {
      calls.push(`import ${cell}`);
    }),
    releaseMovedTenant: vi.fn(async (cell: string, tenantId: string) => {
      calls.push(`release ${cell} ${tenantId}`);
    }),
  };
}

function renderDialog(overrides: Partial<TenantMoveDialogProps> = {}) {
  const props: TenantMoveDialogProps = {
    open: true,
    tenantId: 'acme',
    isDisabled: true,
    sourceCell: 'eu-1',
    cells,
    isCellsLoading: false,
    isCellsFailed: false,
    operations: createOperations(),
    onClose: vi.fn(),
    ...overrides,
  };
  const view = render(<TenantMoveDialog {...props} />);
  return { props, ...view };
}

function choose(cell: string) {
  fireEvent.change(screen.getByTestId('move-target'), { target: { value: cell } });
}

describe('TenantMoveDialog', () => {
  it('offers the other cells of the region of the tenant cell', () => {
    renderDialog();

    fireEvent.mouseDown(screen.getByRole('combobox', { name: /target cell/i }));

    expect(screen.getAllByRole('option').map((option) => option.textContent)).toEqual([
      'eu-2',
      'eu-3',
    ]);
  });

  it('offers no cell and says so when the region has no other cell', () => {
    renderDialog({ sourceCell: 'us-1' });

    expect(screen.getByTestId('move-no-cells')).toBeInTheDocument();
    expect(screen.getByTestId('move-target')).toBeDisabled();
    expect(screen.getByTestId('move-confirm')).toBeDisabled();
  });

  it('offers no cell when the tenant cell is not among the cells', () => {
    renderDialog({ sourceCell: 'ap-1' });

    expect(screen.getByTestId('move-no-cells')).toBeInTheDocument();
  });

  it('says the cells are loading instead of offering none while they load', () => {
    renderDialog({ cells: [], isCellsLoading: true });

    expect(screen.getByTestId('move-cells-loading')).toHaveTextContent(
      'Loading the cells of the platform…',
    );
    expect(screen.queryByTestId('move-no-cells')).not.toBeInTheDocument();
    expect(screen.queryByTestId('move-cells-failed')).not.toBeInTheDocument();
  });

  it('says the list of cells could not be loaded instead of offering none', () => {
    renderDialog({ cells: [], isCellsFailed: true });

    expect(screen.getByTestId('move-cells-failed')).toHaveTextContent(
      'The list of cells could not be loaded.',
    );
    expect(screen.queryByTestId('move-no-cells')).not.toBeInTheDocument();
  });

  it('disables Move until a cell is chosen', () => {
    renderDialog();
    expect(screen.getByTestId('move-confirm')).toBeDisabled();
    expect(screen.queryByTestId('move-enabled')).not.toBeInTheDocument();

    choose('eu-2');

    expect(screen.getByTestId('move-confirm')).toBeEnabled();
  });

  it('does not start a move of an enabled tenant', () => {
    const operations = createOperations();
    renderDialog({ isDisabled: false, operations });

    choose('eu-2');
    fireEvent.click(screen.getByTestId('move-confirm'));

    expect(screen.getByTestId('move-enabled')).toHaveTextContent('Disable acme before moving it.');
    expect(screen.getByTestId('move-confirm')).toBeDisabled();
    expect(operations.createTransferKey).not.toHaveBeenCalled();
    expect(operations.exportTenant).not.toHaveBeenCalled();
    expect(operations.importTenant).not.toHaveBeenCalled();
    expect(operations.releaseMovedTenant).not.toHaveBeenCalled();
  });

  it('runs the transfer key, the export, the import and the release in order', async () => {
    const calls: string[] = [];
    const operations = createOperations(calls);
    renderDialog({ operations });
    choose('eu-2');

    fireEvent.click(screen.getByTestId('move-confirm'));

    expect(await screen.findByTestId('move-done')).toHaveTextContent('Moved to eu-2.');
    expect(calls).toEqual([
      'transfer-key eu-2',
      'export eu-1 acme',
      'import eu-2',
      'release eu-1 acme',
    ]);
    expect(operations.exportTenant).toHaveBeenCalledWith('eu-1', 'acme', 'eu-2', jwk);
    expect(operations.importTenant).toHaveBeenCalledWith('eu-2', 'k-1', 'sealed');
    expect(screen.getByTestId('move-stage-release')).toHaveTextContent('done');
    expect(screen.getByTestId('move-close')).toBeInTheDocument();
  });

  it('shows each stage as it runs', async () => {
    let finishExport: (sealedPackage: string) => void = () => undefined;
    const operations: TenantMoveDialogOperations = {
      ...createOperations(),
      exportTenant: vi.fn(
        () =>
          new Promise<string>((resolve) => {
            finishExport = resolve;
          }),
      ),
    };
    renderDialog({ operations });
    choose('eu-2');

    fireEvent.click(screen.getByTestId('move-confirm'));

    await waitFor(() =>
      expect(screen.getByTestId('move-stage-export')).toHaveTextContent('running'),
    );
    expect(screen.getByTestId('move-stage-transfer-key')).toHaveTextContent('done');
    expect(screen.getByTestId('move-stage-import')).toHaveTextContent('pending');
    expect(screen.getByTestId('move-cancel')).toBeDisabled();

    finishExport('sealed');

    expect(await screen.findByTestId('move-done')).toBeInTheDocument();
  });

  it('names the failed stage and what remains when the export fails', async () => {
    const calls: string[] = [];
    const operations: TenantMoveDialogOperations = {
      ...createOperations(calls),
      exportTenant: vi.fn(async () => {
        throw new Error('The tenant is not disabled.');
      }),
    };
    renderDialog({ operations });
    choose('eu-2');

    fireEvent.click(screen.getByTestId('move-confirm'));

    const failure = await screen.findByTestId('move-failure');
    expect(failure).toHaveTextContent(
      'Export the tenant from eu-1 failed: The tenant is not disabled.',
    );
    expect(screen.getByTestId('move-remaining')).toHaveTextContent(
      'Remaining: Export the tenant from eu-1; Import the tenant into eu-2; Release the tenant at eu-1.',
    );
    expect(failure).toHaveTextContent('The tenant has not moved and is still at eu-1.');
    expect(screen.getByTestId('move-stage-transfer-key')).toHaveTextContent('done');
    expect(screen.getByTestId('move-stage-export')).toHaveTextContent('failed');
    expect(screen.getByTestId('move-stage-import')).toHaveTextContent('pending');
    expect(calls).toEqual(['transfer-key eu-2']);
    expect(screen.getByTestId('move-confirm')).toBeDisabled();
  });

  it('shows the first two stages done and the import failed when the import fails', async () => {
    const operations: TenantMoveDialogOperations = {
      ...createOperations(),
      importTenant: vi.fn(async () => {
        throw new Error('The cell is unavailable.');
      }),
    };
    renderDialog({ operations });
    choose('eu-2');

    fireEvent.click(screen.getByTestId('move-confirm'));

    expect(await screen.findByTestId('move-failure')).toHaveTextContent(
      'Import the tenant into eu-2 failed: The cell is unavailable.',
    );
    expect(screen.getByTestId('move-stage-transfer-key')).toHaveTextContent('done');
    expect(screen.getByTestId('move-stage-export')).toHaveTextContent('done');
    expect(screen.getByTestId('move-stage-import')).toHaveTextContent('failed');
    expect(screen.getByTestId('move-stage-release')).toHaveTextContent('pending');
  });

  it('reports the release at the source as what remains when only the release fails', async () => {
    const operations: TenantMoveDialogOperations = {
      ...createOperations(),
      releaseMovedTenant: vi.fn(async () => {
        throw new Error('The cell is unavailable.');
      }),
    };
    renderDialog({ operations });
    choose('eu-3');

    fireEvent.click(screen.getByTestId('move-confirm'));

    const failure = await screen.findByTestId('move-failure');
    expect(failure).toHaveTextContent(
      'Release the tenant at eu-1 failed: The cell is unavailable.',
    );
    expect(screen.getByTestId('move-remaining')).toHaveTextContent(
      'Remaining: Release the tenant at eu-1.',
    );
    expect(failure).toHaveTextContent('The tenant is imported into eu-3; eu-1 still holds it');
    expect(screen.getByTestId('move-stage-import')).toHaveTextContent('done');
    expect(screen.queryByTestId('move-done')).not.toBeInTheDocument();
  });

  it('retries from the release after the release fails and completes the move', async () => {
    const calls: string[] = [];
    let releases = 0;
    const operations: TenantMoveDialogOperations = {
      ...createOperations(calls),
      releaseMovedTenant: vi.fn(async (cell: string, tenantId: string) => {
        releases += 1;
        calls.push(`release ${cell} ${tenantId}`);
        if (releases === 1) {
          throw new Error('The cell is unavailable.');
        }
      }),
    };
    renderDialog({ operations });
    choose('eu-2');
    fireEvent.click(screen.getByTestId('move-confirm'));
    await screen.findByTestId('move-failure');
    expect(screen.getByTestId('move-confirm')).toBeDisabled();
    calls.length = 0;

    fireEvent.click(screen.getByTestId('move-retry'));

    expect(await screen.findByTestId('move-done')).toHaveTextContent('Moved to eu-2.');
    expect(calls).toEqual(['release eu-1 acme']);
    expect(operations.createTransferKey).toHaveBeenCalledTimes(1);
    expect(operations.exportTenant).toHaveBeenCalledTimes(1);
    expect(operations.importTenant).toHaveBeenCalledTimes(1);
    expect(operations.releaseMovedTenant).toHaveBeenCalledTimes(2);
    expect(screen.queryByTestId('move-failure')).not.toBeInTheDocument();
    expect(screen.queryByTestId('move-retry')).not.toBeInTheDocument();
    expect(screen.getByTestId('move-stage-release')).toHaveTextContent('done');
  });

  it('keeps the failure and Retry when the retry fails again', async () => {
    const operations: TenantMoveDialogOperations = {
      ...createOperations(),
      importTenant: vi.fn(async () => {
        throw new Error('The cell is unavailable.');
      }),
    };
    renderDialog({ operations });
    choose('eu-2');
    fireEvent.click(screen.getByTestId('move-confirm'));
    await screen.findByTestId('move-failure');

    fireEvent.click(screen.getByTestId('move-retry'));

    await waitFor(() => expect(operations.importTenant).toHaveBeenCalledTimes(2));
    expect(await screen.findByTestId('move-failure')).toHaveTextContent(
      'Import the tenant into eu-2 failed: The cell is unavailable.',
    );
    expect(screen.getByTestId('move-retry')).toBeEnabled();
    expect(operations.exportTenant).toHaveBeenCalledTimes(2);
  });

  it('retries from the transfer key after the import fails and completes the move', async () => {
    const calls: string[] = [];
    let imports = 0;
    const operations: TenantMoveDialogOperations = {
      ...createOperations(calls),
      importTenant: vi.fn(async (cell: string) => {
        imports += 1;
        calls.push(`import ${cell}`);
        if (imports === 1) {
          throw new Error('The cell is unavailable.');
        }
      }),
    };
    renderDialog({ operations });
    choose('eu-2');
    fireEvent.click(screen.getByTestId('move-confirm'));
    await screen.findByTestId('move-failure');
    calls.length = 0;

    fireEvent.click(screen.getByTestId('move-retry'));

    expect(await screen.findByTestId('move-done')).toHaveTextContent('Moved to eu-2.');
    expect(calls).toEqual([
      'transfer-key eu-2',
      'export eu-1 acme',
      'import eu-2',
      'release eu-1 acme',
    ]);
    expect(operations.createTransferKey).toHaveBeenCalledTimes(2);
    expect(operations.exportTenant).toHaveBeenCalledTimes(2);
    expect(operations.importTenant).toHaveBeenCalledTimes(2);
    expect(operations.releaseMovedTenant).toHaveBeenCalledTimes(1);
    expect(screen.queryByTestId('move-failure')).not.toBeInTheDocument();
  });

  it('retries from the export with the transfer key the move already holds', async () => {
    let exports = 0;
    const operations: TenantMoveDialogOperations = {
      ...createOperations(),
      exportTenant: vi.fn(async () => {
        exports += 1;
        if (exports === 1) {
          throw new Error('The cell is unavailable.');
        }
        return 'sealed';
      }),
    };
    renderDialog({ operations });
    choose('eu-2');
    fireEvent.click(screen.getByTestId('move-confirm'));
    await screen.findByTestId('move-failure');

    fireEvent.click(screen.getByTestId('move-retry'));

    expect(await screen.findByTestId('move-done')).toHaveTextContent('Moved to eu-2.');
    expect(operations.createTransferKey).toHaveBeenCalledTimes(1);
    expect(operations.exportTenant).toHaveBeenCalledTimes(2);
    expect(operations.exportTenant).toHaveBeenLastCalledWith('eu-1', 'acme', 'eu-2', jwk);
    expect(operations.importTenant).toHaveBeenCalledWith('eu-2', 'k-1', 'sealed');
    expect(screen.queryByTestId('move-failure')).not.toBeInTheDocument();
  });

  it('shows no Retry before a failure', () => {
    renderDialog();
    choose('eu-2');

    expect(screen.queryByTestId('move-retry')).not.toBeInTheDocument();
  });

  it('closes through Close after a failure', async () => {
    const operations: TenantMoveDialogOperations = {
      ...createOperations(),
      createTransferKey: vi.fn(async () => {
        throw new Error('Unavailable.');
      }),
    };
    const { props } = renderDialog({ operations });
    choose('eu-2');
    fireEvent.click(screen.getByTestId('move-confirm'));
    await screen.findByTestId('move-failure');

    fireEvent.click(screen.getByTestId('move-cancel'));

    expect(screen.getByTestId('move-cancel')).toHaveTextContent('Close');
    expect(props.onClose).toHaveBeenCalledTimes(1);
  });

  it('hands onMoved the cell the tenant moved to when it closes after a completed move', async () => {
    const onMoved = vi.fn();
    const { props } = renderDialog({ onMoved });
    choose('eu-3');
    fireEvent.click(screen.getByTestId('move-confirm'));
    await screen.findByTestId('move-done');

    fireEvent.click(screen.getByTestId('move-close'));

    expect(onMoved).toHaveBeenCalledTimes(1);
    expect(onMoved).toHaveBeenCalledWith('eu-3');
    expect(props.onClose).toHaveBeenCalledTimes(1);
  });

  it('closes after a completed move without an onMoved', async () => {
    const { props } = renderDialog();
    choose('eu-2');
    fireEvent.click(screen.getByTestId('move-confirm'));
    await screen.findByTestId('move-done');

    fireEvent.click(screen.getByTestId('move-close'));

    expect(props.onClose).toHaveBeenCalledTimes(1);
  });

  it('calls no onMoved when it closes before a move completes', () => {
    const onMoved = vi.fn();
    const { props } = renderDialog({ onMoved });
    choose('eu-2');

    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' });

    expect(props.onClose).toHaveBeenCalledTimes(1);
    expect(onMoved).not.toHaveBeenCalled();
  });

  it('forgets the chosen cell and the progress when it opens again', async () => {
    const props: TenantMoveDialogProps = {
      open: true,
      tenantId: 'acme',
      isDisabled: true,
      sourceCell: 'eu-1',
      cells,
      isCellsLoading: false,
      isCellsFailed: false,
      operations: createOperations(),
      onClose: vi.fn(),
    };
    const { rerender } = render(<TenantMoveDialog {...props} />);
    fireEvent.change(screen.getByTestId('move-target'), { target: { value: 'eu-2' } });
    fireEvent.click(screen.getByTestId('move-confirm'));
    await screen.findByTestId('move-done');

    rerender(<TenantMoveDialog {...props} open={false} />);
    rerender(<TenantMoveDialog {...props} open />);

    expect(screen.queryByTestId('move-done')).not.toBeInTheDocument();
    expect(screen.getByTestId('move-target')).toHaveValue('');
  });

  it('keeps a failed move and its Retry when it opens again, and retries from the failed stage', async () => {
    let keys = 0;
    const operations: TenantMoveDialogOperations = {
      ...createOperations(),
      createTransferKey: vi.fn(async () => {
        keys += 1;
        if (keys === 1) {
          throw new Error('Unavailable.');
        }
        return { kid: 'k-1', jwk };
      }),
    };
    const props: TenantMoveDialogProps = {
      open: true,
      tenantId: 'acme',
      isDisabled: true,
      sourceCell: 'eu-1',
      cells,
      isCellsLoading: false,
      isCellsFailed: false,
      operations,
      onClose: vi.fn(),
    };
    const { rerender } = render(<TenantMoveDialog {...props} />);
    choose('eu-2');
    fireEvent.click(screen.getByTestId('move-confirm'));
    await screen.findByTestId('move-failure');

    rerender(<TenantMoveDialog {...props} open={false} />);
    rerender(<TenantMoveDialog {...props} open />);

    expect(screen.getByTestId('move-failure')).toHaveTextContent(
      'Issue a transfer key at eu-2 failed: Unavailable.',
    );
    expect(screen.getByTestId('move-target')).toHaveValue('eu-2');
    expect(screen.getByTestId('move-cancel')).toHaveTextContent('Close');

    fireEvent.click(screen.getByTestId('move-retry'));

    expect(await screen.findByTestId('move-done')).toHaveTextContent('Moved to eu-2.');
    expect(operations.createTransferKey).toHaveBeenCalledTimes(2);
  });

  it('offers Release, which runs only the release, when the export finds the tenant placed in another cell', async () => {
    const calls: string[] = [];
    const operations: TenantMoveDialogOperations = {
      ...createOperations(calls),
      exportTenant: vi.fn(async () => {
        throw new TenantPlacedElsewhereError('The tenant is placed in eu-2.', 'eu-2');
      }),
    };
    renderDialog({ operations });
    choose('eu-3');
    fireEvent.click(screen.getByTestId('move-confirm'));

    const failure = await screen.findByTestId('move-failure');
    expect(failure).toHaveTextContent(
      'Export the tenant from eu-1 failed: The global directory already places the tenant in eu-2; release this copy at eu-1.',
    );
    expect(failure).not.toHaveTextContent('The tenant has not moved');
    expect(screen.getByTestId('move-remaining')).toHaveTextContent(
      'Remaining: Release the tenant at eu-1.',
    );
    expect(screen.queryByTestId('move-retry')).not.toBeInTheDocument();
    calls.length = 0;

    fireEvent.click(screen.getByTestId('move-release'));

    expect(await screen.findByTestId('move-done')).toHaveTextContent('Moved to eu-2.');
    expect(calls).toEqual(['release eu-1 acme']);
    expect(operations.createTransferKey).toHaveBeenCalledTimes(1);
    expect(operations.importTenant).not.toHaveBeenCalled();
  });

  it('says that the outcome is unknown, not that the tenant has not moved, when the import cannot tell', async () => {
    const operations: TenantMoveDialogOperations = {
      ...createOperations(),
      importTenant: vi.fn(async () => {
        throw new TenantMoveOutcomeUnknownError('The outcome of the move is unknown.');
      }),
    };
    renderDialog({ operations });
    choose('eu-2');

    fireEvent.click(screen.getByTestId('move-confirm'));

    const failure = await screen.findByTestId('move-failure');
    expect(failure).toHaveTextContent(
      'The outcome of the move is unknown: the global directory may already place the tenant in eu-2. Read its placement before you release it at eu-1; Retry starts the move again.',
    );
    expect(failure).not.toHaveTextContent('The tenant has not moved');
    expect(screen.getByTestId('move-retry')).toBeInTheDocument();
  });
});
