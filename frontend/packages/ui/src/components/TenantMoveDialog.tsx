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
import {
  createTenantMove,
  tenantMoveStages,
  type TenantMove,
  type TenantMoveFailure,
  type TenantMoveOperations,
  type TenantMoveTransferKey,
} from './tenantMove.js';

/** A cell of the platform and the region it serves (decoupled from the API contract). */
export type TenantMoveDialogCell = {
  readonly name: string;
  readonly region: string;
};

/**
 * The transfer key a receiving cell issues: its `kid` and its public JSON Web Key (decoupled from the API contract).
 */
export type TenantMoveDialogTransferKey = TenantMoveTransferKey;

/**
 * The four calls of a move, each addressed to the cell it concerns (decoupled from the API contract). Each rejects with
 * an `Error` whose message says why it failed.
 */
export type TenantMoveDialogOperations = TenantMoveOperations;

export type TenantMoveDialogProps = {
  readonly open: boolean;
  readonly tenantId: string;
  /** Whether the tenant is disabled, which a move requires. */
  readonly isDisabled: boolean;
  /** The cell the tenant lives in. */
  readonly sourceCell: string;
  /**
   * Every cell of the platform; the dialog offers those of the tenant's region other than its own.
   */
  readonly cells: readonly TenantMoveDialogCell[];
  /** Whether the platform's cells are still loading. */
  readonly isCellsLoading: boolean;
  /** Whether the platform's cells could not be loaded. */
  readonly isCellsFailed: boolean;
  readonly operations: TenantMoveDialogOperations;
  readonly onClose: () => void;
  /** Called with the cell the tenant moved to when the dialog closes after a completed move. */
  readonly onMoved?: (targetCell: string) => void;
};

type Stage = {
  readonly key: string;
  readonly label: string;
};

/** A move whose run failed: the move that a retry resumes, and the stage that failed and why. */
type FailedMove = {
  readonly move: TenantMove;
  readonly failure: TenantMoveFailure;
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

/**
 * Move dialog for a disabled tenant: pick another cell of the tenant's region, then run the four stages of a move in
 * order (a transfer key from the receiving cell, the export from the tenant's cell, the import into the receiving cell
 * and the release at the tenant's cell), showing each stage. An enabled tenant starts no move: the dialog says to
 * disable it first, while Retry of a move under way is not gated. A failure stops the run and names the failed stage
 * and what remains, and it outlasts closing the dialog, so opening it again shows the failure and its Retry. Retry
 * resumes from the failed stage, so the stages that finished do not run again, except after a failed import, which
 * spends the transfer key, so Retry then runs from the transfer key. An export that finds the tenant already placed in
 * another cell offers Release instead, which runs only the release of the copy the tenant's own cell holds. An import
 * that cannot tell whether the global directory moved the tenant says that the outcome is unknown instead of saying
 * where the tenant is. While the platform's cells load, or when they cannot be loaded, the dialog says so instead of
 * saying that the region has no other cell. Closing it after a completed move hands `onMoved` the cell the tenant moved
 * to. The chosen cell and the progress are local because the dialog runs the sequence itself; the calls are the
 * container's to supply, so this package holds no store.
 */
export function TenantMoveDialog({
  open,
  tenantId,
  isDisabled,
  sourceCell,
  cells,
  isCellsLoading,
  isCellsFailed,
  operations,
  onClose,
  onMoved,
}: TenantMoveDialogProps) {
  const [targetCell, setTargetCell] = useState('');
  const [completed, setCompleted] = useState(0);
  const [isRunning, setIsRunning] = useState(false);
  const [failedMove, setFailedMove] = useState<FailedMove>();
  const [placedCell, setPlacedCell] = useState<string>();
  const wasOpenRef = useRef(open);

  // Reset only on the rising edge of `open`, so a re-render while open never discards the progress,
  // and only when no move failed, so a failed move and its Retry or Release outlast closing the
  // dialog.
  useEffect(() => {
    if (open && !wasOpenRef.current && failedMove === undefined) {
      setTargetCell('');
      setCompleted(0);
      setIsRunning(false);
      setPlacedCell(undefined);
    }
    wasOpenRef.current = open;
  }, [open, failedMove]);

  // A tenant moves only within its region, so the targets are the cells of the region of its own
  // cell, its own excluded.
  const source = cells.find((cell) => cell.name === sourceCell);
  const targets =
    source === undefined
      ? []
      : cells.filter((cell) => cell.name !== sourceCell && cell.region === source.region);
  const target = targets.find((cell) => cell.name === targetCell);
  // The cell the tenant ends up in: the chosen cell, or the cell the global directory already
  // places it in.
  const receivingCell = placedCell ?? targetCell;

  const stages = target === undefined ? [] : stagesOf(sourceCell, target.name);
  const failure = failedMove?.failure;
  const hasStarted = isRunning || completed > 0 || failure !== undefined;
  const isMoved = completed === stages.length && stages.length > 0;

  /**
   * Runs the move from `fromStage`: 0 for a fresh move, the failure's `resumeStage` for a retry.
   */
  const runFrom = async (move: TenantMove, fromStage: number) => {
    setIsRunning(true);
    setFailedMove(undefined);
    try {
      const runFailure = await move.run(fromStage, setCompleted);
      if (runFailure?.placedCell !== undefined) {
        setPlacedCell(runFailure.placedCell);
      }
      setFailedMove(runFailure === undefined ? undefined : { move, failure: runFailure });
    } finally {
      setIsRunning(false);
    }
  };

  // The buttons hand each handler its cell or failed move, so neither runs without one.
  const handleMove = async (chosen: TenantMoveDialogCell) => {
    const move = createTenantMove(operations, {
      tenantId,
      sourceCell,
      targetCell: chosen.name,
    });
    setCompleted(0);
    await runFrom(move, tenantMoveStages.transferKey);
  };

  const handleRetry = async (retried: FailedMove) => {
    setCompleted(retried.failure.resumeStage);
    await runFrom(retried.move, retried.failure.resumeStage);
  };

  const failedStage = failure === undefined ? undefined : stages[failure.stage];
  const remaining =
    failure === undefined
      ? []
      : stages.slice(failure.placedCell === undefined ? failure.stage : failure.resumeStage);

  // Closing after a completed move hands the container the cell the tenant moved to.
  const handleClose = () => {
    onClose();
    if (isMoved) {
      onMoved?.(receivingCell);
    }
  };

  return (
    <Dialog open={open} onClose={isRunning ? undefined : handleClose} fullWidth maxWidth="sm">
      <DialogTitle>Move tenant</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <Typography variant="body2">
            Move {tenantId} from {sourceCell} to another cell of its region. The tenant stays
            disabled while it moves.
          </Typography>
          {targets.length === 0 &&
            (isCellsLoading ? (
              <Alert severity="info" data-testid="move-cells-loading">
                Loading the cells of the platform…
              </Alert>
            ) : isCellsFailed ? (
              <Alert severity="error" data-testid="move-cells-failed">
                The list of cells could not be loaded.
              </Alert>
            ) : (
              <Alert severity="info" data-testid="move-no-cells">
                The region of {sourceCell} has no other cell.
              </Alert>
            ))}
          {!isDisabled && !hasStarted && (
            <Alert severity="warning" data-testid="move-enabled">
              Disable {tenantId} before moving it.
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
              {failure.placedCell === undefined && (
                <Typography variant="body2">
                  {failure.isOutcomeUnknown === true
                    ? `The outcome of the move is unknown: the global directory may already place the tenant in ${receivingCell}. Read its placement before you release it at ${sourceCell}; Retry starts the move again.`
                    : failure.stage < tenantMoveStages.release
                      ? `The tenant has not moved and is still at ${sourceCell}.`
                      : `The tenant is imported into ${receivingCell}; ${sourceCell} still holds it until it is released.`}
                </Typography>
              )}
            </Alert>
          )}
          {isMoved && (
            <Alert severity="success" data-testid="move-done">
              Moved to {receivingCell}.
            </Alert>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        {isMoved ? (
          <Button variant="contained" onClick={handleClose} data-testid="move-close">
            Close
          </Button>
        ) : (
          <>
            <Button onClick={onClose} disabled={isRunning} data-testid="move-cancel">
              {failure === undefined ? 'Cancel' : 'Close'}
            </Button>
            {failedMove !== undefined && (
              <Button
                variant="contained"
                onClick={() => {
                  void handleRetry(failedMove);
                }}
                disabled={isRunning}
                data-testid={
                  failedMove.failure.placedCell === undefined ? 'move-retry' : 'move-release'
                }
              >
                {failedMove.failure.placedCell === undefined ? 'Retry' : 'Release'}
              </Button>
            )}
            <Button
              variant="contained"
              onClick={
                target === undefined
                  ? undefined
                  : () => {
                      void handleMove(target);
                    }
              }
              disabled={target === undefined || hasStarted || !isDisabled}
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
