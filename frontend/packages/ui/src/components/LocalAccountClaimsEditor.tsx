import { useEffect, useRef, useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import IconButton from '@mui/material/IconButton';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import AddIcon from '@mui/icons-material/Add';
import DeleteIcon from '@mui/icons-material/Delete';

export type LocalAccountClaim = { readonly type: string; readonly value: string };

export type LocalAccountClaimsEditorProps = {
  /** The persisted profile claims; reseeds the editor when it changes. */
  readonly claims: readonly LocalAccountClaim[];
  readonly isSaving?: boolean;
  /** A server-side error surfaced after a failed save. */
  readonly error?: string | undefined;
  readonly onSave: (claims: LocalAccountClaim[]) => void;
};

type ClaimRow = { readonly id: number; readonly type: string; readonly value: string };

const reservedNameMessage = 'The display name is the name claim; edit it with the user.';

/**
 * Stateless profile claims editor. The rows are intrinsic to the control and reseed when
 * {@link LocalAccountClaimsEditorProps.claims} changes after a save; persistence lives in the
 * container, and {@link LocalAccountClaimsEditorProps.onSave} receives the trimmed rows.
 */
export function LocalAccountClaimsEditor({
  claims,
  isSaving = false,
  error,
  onSave,
}: LocalAccountClaimsEditorProps) {
  const nextIdRef = useRef(0);
  const seedRows = (source: readonly LocalAccountClaim[]): ClaimRow[] =>
    source.map((claim) => ({ ...claim, id: nextIdRef.current++ }));
  const [rows, setRows] = useState<ClaimRow[]>(() => seedRows(claims));
  const previousClaimsRef = useRef(claims);

  useEffect(() => {
    if (claims !== previousClaimsRef.current) {
      setRows(seedRows(claims));
      previousClaimsRef.current = claims;
    }
  }, [claims]);

  const isDirty =
    rows.length !== claims.length ||
    rows.some(
      (row, index) => row.type !== claims[index]?.type || row.value !== claims[index]?.value,
    );
  const hasBlankType = rows.some((row) => row.type.trim() === '');
  const hasReservedType = rows.some((row) => row.type.trim() === 'name');
  const canSave = isDirty && !isSaving && !hasBlankType && !hasReservedType;

  const update = (index: number, patch: Partial<LocalAccountClaim>) =>
    setRows((current) => current.map((row, i) => (i === index ? { ...row, ...patch } : row)));

  const handleSave = () => {
    if (!canSave) {
      return;
    }
    onSave(rows.map((row) => ({ type: row.type.trim(), value: row.value.trim() })));
  };

  return (
    <Stack spacing={2}>
      {error && (
        <Alert severity="error" data-testid="claims-error">
          {error}
        </Alert>
      )}
      {rows.map((row, index) => (
        <Box key={row.id} sx={{ display: 'flex', gap: 1, alignItems: 'flex-start' }}>
          <TextField
            label="Type"
            value={row.type}
            onChange={(event) => update(index, { type: event.target.value })}
            error={row.type.trim() === 'name'}
            helperText={row.type.trim() === 'name' ? reservedNameMessage : undefined}
            size="small"
            slotProps={{ htmlInput: { 'data-testid': `claim-type-${index}` } }}
          />
          <TextField
            label="Value"
            value={row.value}
            onChange={(event) => update(index, { value: event.target.value })}
            size="small"
            fullWidth
            slotProps={{ htmlInput: { 'data-testid': `claim-value-${index}` } }}
          />
          <IconButton
            aria-label={`Remove claim ${index + 1}`}
            onClick={() => setRows((current) => current.filter((_, i) => i !== index))}
            data-testid={`claim-remove-${index}`}
          >
            <DeleteIcon />
          </IconButton>
        </Box>
      ))}
      <Box sx={{ display: 'flex', justifyContent: 'space-between' }}>
        <Button
          startIcon={<AddIcon />}
          onClick={() =>
            setRows((current) => [...current, { id: nextIdRef.current++, type: '', value: '' }])
          }
          data-testid="claim-add"
        >
          Add claim
        </Button>
        <Button
          variant="contained"
          onClick={handleSave}
          disabled={!canSave}
          data-testid="claims-save"
        >
          {isSaving ? 'Saving…' : 'Save claims'}
        </Button>
      </Box>
    </Stack>
  );
}
