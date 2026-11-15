import { useEffect, useRef, useState } from 'react';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import List from '@mui/material/List';
import ListItem from '@mui/material/ListItem';
import ListItemText from '@mui/material/ListItemText';
import MenuItem from '@mui/material/MenuItem';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';

/** A cell of the platform and the region it serves (decoupled from the API contract). */
export type TenantMoveDialogCell = {
  readonly name: string;
  readonly region: string;
};

/** The transfer key a receiving cell issues: its `kid` and its public JSON Web Key (decoupled from the API contract). */
export type TenantMoveDialogTransferKey = {
  readonly kid: string;
  readonly jwk: Readonly<Record<string, unknown>>;
};

/**
 * The four calls of a move, each addressed to the cell it concerns (decoupled from the API contract). Each rejects
 * with an `Error` whose message says why it failed.
 */
export type TenantMoveDialogOperations = {
  /** Asks `cell`, the receiving cell, for a one-time transfer key. */
  readonly createTransferKey: (cell: string) => Promise<TenantMoveDialogTransferKey>;
  /** Exports the disabled tenant from `cell`, the tenant's own cell, sealed to `jwk`; resolves with the package. */
  readonly exportTenant: (
    cell: string,
    tenantId: string,
    jwk: Readonly<Record<string, unknown>>,
  ) => Promise<string>;
  /** Imports `sealedPackage` into `cell`, the receiving cell, with the transfer key named `kid`. */
  readonly importTenant: (cell: string, kid: string, sealedPackage: string) => Promise<void>;
  /** Releases what `cell`, the tenant's own cell, still holds of the tenant that moved. */
  readonly releaseMovedTenant: (cell: string, tenantId: string) => Promise<void>;
};

export type TenantMoveDialogProps = {
  readonly open: boolean;
  readonly tenantId: string;
  /** The cell the tenant lives in. */
  readonly sourceCell: string;
  /** Every cell of the platform; the dialog offers those of the tenant's region other than its own. */
  readonly cells: readonly TenantMoveDialogCell[];
  readonly operations: TenantMoveDialogOperations;
  readonly onClose: () => void;
};

type Stage = {
  readonly key: string;
  readonly label: string;
};

/** The stages of a move, in the order they run. */
function stagesOf(sourceCell: string, targetCell: string): readonly Stage[] {
  return [
    { key: 'transfer-key', label: `Issue a transfer key at ${targetCell}` },
    { key: 'export', label: `Export the tenant from ${sourceCell}` },
    { key: 'import', label: `Import the tenant into ${targetCell}` },
    { key: 'release', label: `Release the tenant at ${sourceCell}` },
  ];
}

const releaseStage = 3;

type Failure = {
  /** The index of the stage that failed. */
  readonly stage: number;
  readonly message: string;
};

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

/**
 * Move dialog for a disabled tenant: pick another cell of the tenant's region, then run the four stages of a move in
 * order (a transfer key from the receiving cell, the export from the tenant's cell, the import into the receiving
 * cell and the release at the tenant's cell), showing each stage. A failure stops the run and names the failed stage
 * and what remains. The chosen cell and the progress are local because the dialog runs the sequence itself; the
 * calls are the container's to supply, so this package holds no store.
 */
export function TenantMoveDialog({
  open,
  tenantId,
  sourceCell,
  cells,
  operations,
  onClose,
}: TenantMoveDialogProps) {
  const [targetCell, setTargetCell] = useState('');
  const [completed, setCompleted] = useState(0);
  const [isRunning, setIsRunning] = useState(false);
  const [failure, setFailure] = useState<Failure>();
  const wasOpenRef = useRef(open);

  // Reset only on the rising edge of `open`, so a re-render while open never discards the progress.
  useEffect(() => {
    if (open && !wasOpenRef.current) {
      setTargetCell('');
      setCompleted(0);
      setIsRunning(false);
      setFailure(undefined);
    }
    wasOpenRef.current = open;
  }, [open]);

  // A tenant moves only within its region, so the targets are the cells of the region of its own cell, its own excluded.
  const source = cells.find((cell) => cell.name === sourceCell);
  const targets =
    source === undefined
      ? []
      : cells.filter((cell) => cell.name !== sourceCell && cell.region === source.region);
  const target = targets.find((cell) => cell.name === targetCell);

  const stages = target === undefined ? [] : stagesOf(sourceCell, target.name);
  const hasStarted = isRunning || completed > 0 || failure !== undefined;
  const isMoved = completed === stages.length && stages.length > 0;

  const handleMove = async () => {
    if (target === undefined) {
      return;
    }
    setIsRunning(true);
    setFailure(undefined);
    setCompleted(0);
    // `completed` counts the stages that finished, so it is also the index of the stage that runs next.
    let completedStages = 0;
    try {
      const key = await operations.createTransferKey(target.name);
      completedStages = 1;
      setCompleted(completedStages);
      const sealedPackage = await operations.exportTenant(sourceCell, tenantId, key.jwk);
      completedStages = 2;
      setCompleted(completedStages);
      await operations.importTenant(target.name, key.kid, sealedPackage);
      completedStages = 3;
      setCompleted(completedStages);
      await operations.releaseMovedTenant(sourceCell, tenantId);
      completedStages = 4;
      setCompleted(completedStages);
    } catch (error) {
      setFailure({ stage: completedStages, message: errorMessage(error) });
    } finally {
      setIsRunning(false);
    }
  };

  const failedStage = failure === undefined ? undefined : stages[failure.stage];
  const remaining = failure === undefined ? [] : stages.slice(failure.stage);

  return (
    <Dialog open={open} onClose={isRunning ? undefined : onClose} fullWidth maxWidth="sm">
      <DialogTitle>Move tenant</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <Typography variant="body2">
            Move {tenantId} from {sourceCell} to another cell of its region. The tenant stays
            disabled while it moves.
          </Typography>
          {targets.length === 0 && (
            <Alert severity="info" data-testid="move-no-cells">
              The region of {sourceCell} has no other cell.
            </Alert>
          )}
          <TextField
            select
            label="Target cell"
            value={targetCell}
            onChange={(event) => setTargetCell(event.target.value)}
            disabled={hasStarted || targets.length === 0}
            helperText="A cell of the same region that receives the tenant."
            fullWidth
            slotProps={{ htmlInput: { 'data-testid': 'move-target' } }}
          >
            {targets.map((cell) => (
              <MenuItem key={cell.name} value={cell.name}>
                {cell.name}
              </MenuItem>
            ))}
          </TextField>
          {hasStarted && (
            <List dense disablePadding data-testid="move-stages">
              {stages.map((stage, index) => {
                const status =
                  failure?.stage === index
                    ? 'failed'
                    : index < completed
                      ? 'done'
                      : isRunning && index === completed
                        ? 'running'
                        : 'pending';
                return (
                  <ListItem key={stage.key} disableGutters data-testid={`move-stage-${stage.key}`}>
                    <ListItemText primary={stage.label} secondary={status} />
                  </ListItem>
                );
              })}
            </List>
          )}
          {failure !== undefined && failedStage !== undefined && (
            <Alert severity="error" data-testid="move-failure">
              <Typography variant="body2">
                {failedStage.label} failed: {failure.message}
              </Typography>
              <Typography variant="body2" data-testid="move-remaining">
                Remaining: {remaining.map((stage) => stage.label).join('; ')}.
              </Typography>
              <Typography variant="body2">
                {failure.stage < releaseStage
                  ? `The tenant has not moved and is still at ${sourceCell}.`
                  : `The tenant is imported into ${targetCell}; ${sourceCell} still holds it until it is released.`}
              </Typography>
            </Alert>
          )}
          {isMoved && (
            <Alert severity="success" data-testid="move-done">
              Moved to {targetCell}.
            </Alert>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        {isMoved ? (
          <Button variant="contained" onClick={onClose} data-testid="move-close">
            Close
          </Button>
        ) : (
          <>
            <Button onClick={onClose} disabled={isRunning} data-testid="move-cancel">
              {failure === undefined ? 'Cancel' : 'Close'}
            </Button>
            <Button
              variant="contained"
              onClick={() => {
                void handleMove();
              }}
              disabled={target === undefined || hasStarted}
              data-testid="move-confirm"
            >
              {isRunning ? 'Moving…' : 'Move'}
            </Button>
          </>
        )}
      </DialogActions>
    </Dialog>
  );
}
