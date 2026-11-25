import { useState } from 'react';
import AddIcon from '@mui/icons-material/Add';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { ConfirmDialog, DataStateBoundary, UnavailableCellsAlert } from '@ncodegroup/identity-ui';
import {
  apiErrorMessage,
  useAppSelector,
  useListCellsQuery,
  useRegisterCellMutation,
  useRetireCellMutation,
  type ApiError,
} from '@ncodegroup/identity-store';

/** Parses JSON Web Key Set text, returning the set or the reason it is not one. */
function parseJwks(text: string): { readonly jwks: Record<string, unknown> } | { error: string } {
  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch {
    return { error: 'The JWKS must be valid JSON.' };
  }
  if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) {
    return { error: 'The JWKS must be a JSON object.' };
  }
  const keys = (parsed as Record<string, unknown>)['keys'];
  if (!Array.isArray(keys) || keys.length === 0) {
    return { error: 'The JWKS must have a non-empty "keys" array.' };
  }
  return { jwks: parsed as Record<string, unknown> };
}

const yesNo = (value: boolean) => (value ? 'Yes' : 'No');

/**
 * Container: the platform's cells. It lists every cell the control plane knows, names each configured console cell the
 * list does not contain, registers a cell with the global directory from its name, region, kind and public JWKS, and
 * retires a cell after a confirmation, showing the server's refusal when the cell still holds tenants.
 */
export function CellsPage() {
  const { data, isLoading, error } = useListCellsQuery();
  const cells = data ?? [];
  const runtime = useAppSelector((state) => state.config.runtime);
  const configured = runtime?.kind === 'bff' ? (runtime.cells ?? []) : [];
  const unavailableCells =
    data === undefined ? [] : configured.filter((name) => !cells.some((c) => c.name === name));

  const [registerCell, { isLoading: isRegistering }] = useRegisterCellMutation();
  const [retireCell, { isLoading: isRetiring }] = useRetireCellMutation();

  const [registerOpen, setRegisterOpen] = useState(false);
  const [name, setName] = useState('');
  const [region, setRegion] = useState('');
  const [kind, setKind] = useState('shared');
  const [jwksText, setJwksText] = useState('');
  const [registerError, setRegisterError] = useState<string>();
  const [retiring, setRetiring] = useState<string>();
  const [retireError, setRetireError] = useState<string>();

  const openRegister = () => {
    setName('');
    setRegion('');
    setKind('shared');
    setJwksText('');
    setRegisterError(undefined);
    setRegisterOpen(true);
  };

  const handleRegister = async () => {
    const parsed = parseJwks(jwksText);
    if ('error' in parsed) {
      setRegisterError(parsed.error);
      return;
    }
    setRegisterError(undefined);
    try {
      await registerCell({
        name: name.trim(),
        body: { region: region.trim(), kind: kind.trim(), jwks: parsed.jwks },
      }).unwrap();
      setRegisterOpen(false);
    } catch (submitError) {
      setRegisterError(apiErrorMessage(submitError as ApiError));
    }
  };

  const handleRetire = async () => {
    if (retiring === undefined) {
      return;
    }
    setRetireError(undefined);
    try {
      await retireCell({ name: retiring }).unwrap();
      setRetiring(undefined);
    } catch (removeError) {
      setRetireError(apiErrorMessage(removeError as ApiError));
    }
  };

  const canSubmit = name.trim() !== '' && region.trim() !== '' && kind.trim() !== '';

  return (
    <>
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
        <Typography variant="h5" component="h2">
          Cells
        </Typography>
        <Button
          variant="contained"
          startIcon={<AddIcon />}
          onClick={openRegister}
          data-testid="register-cell"
        >
          Register cell
        </Button>
      </Box>
      <UnavailableCellsAlert cells={unavailableCells} />
      <DataStateBoundary
        isLoading={isLoading}
        error={error ? 'Failed to load cells.' : undefined}
        isEmpty={cells.length === 0}
        emptyMessage="No cells exist yet."
      >
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>Name</TableCell>
              <TableCell>Region</TableCell>
              <TableCell>Kind</TableCell>
              <TableCell>Control plane</TableCell>
              <TableCell>Registered</TableCell>
              <TableCell>Retired</TableCell>
              <TableCell />
            </TableRow>
          </TableHead>
          <TableBody>
            {cells.map((cell) => (
              <TableRow key={cell.name} data-testid={`cell-row-${cell.name}`}>
                <TableCell>{cell.name}</TableCell>
                <TableCell>{cell.region}</TableCell>
                <TableCell>{cell.kind}</TableCell>
                <TableCell>{yesNo(cell.isControlPlane)}</TableCell>
                <TableCell>{yesNo(cell.isRegistered)}</TableCell>
                <TableCell>{yesNo(cell.isRetired)}</TableCell>
                <TableCell align="right">
                  {!cell.isRetired && !cell.isControlPlane && (
                    <Button
                      color="error"
                      size="small"
                      onClick={() => {
                        setRetireError(undefined);
                        setRetiring(cell.name);
                      }}
                      data-testid={`retire-cell-${cell.name}`}
                    >
                      Retire
                    </Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </DataStateBoundary>

      <Dialog open={registerOpen} onClose={() => setRegisterOpen(false)} maxWidth="sm" fullWidth>
        <DialogTitle>Register cell</DialogTitle>
        <DialogContent>
          {registerError && (
            <Alert severity="error" sx={{ mb: 2 }} data-testid="register-cell-error">
              {registerError}
            </Alert>
          )}
          <TextField
            label="Name"
            value={name}
            onChange={(event) => setName(event.target.value)}
            fullWidth
            margin="normal"
            slotProps={{ htmlInput: { 'data-testid': 'cell-name' } }}
          />
          <TextField
            label="Region"
            value={region}
            onChange={(event) => setRegion(event.target.value)}
            fullWidth
            margin="normal"
            slotProps={{ htmlInput: { 'data-testid': 'cell-region' } }}
          />
          <TextField
            label="Kind"
            value={kind}
            onChange={(event) => setKind(event.target.value)}
            fullWidth
            margin="normal"
            slotProps={{ htmlInput: { 'data-testid': 'cell-kind' } }}
          />
          <TextField
            label="JWKS"
            value={jwksText}
            onChange={(event) => setJwksText(event.target.value)}
            fullWidth
            multiline
            minRows={4}
            margin="normal"
            slotProps={{ htmlInput: { 'data-testid': 'cell-jwks' } }}
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setRegisterOpen(false)} disabled={isRegistering}>
            Cancel
          </Button>
          <Button
            variant="contained"
            onClick={() => void handleRegister()}
            disabled={!canSubmit || isRegistering}
            data-testid="register-cell-submit"
          >
            {isRegistering ? 'Working…' : 'Register'}
          </Button>
        </DialogActions>
      </Dialog>

      <ConfirmDialog
        open={retiring !== undefined}
        title="Retire cell"
        message={`Retire “${retiring ?? ''}”? A retired cell name is never reissued.`}
        confirmLabel="Retire"
        isBusy={isRetiring}
        error={retireError}
        onConfirm={handleRetire}
        onCancel={() => setRetiring(undefined)}
      />
    </>
  );
}
