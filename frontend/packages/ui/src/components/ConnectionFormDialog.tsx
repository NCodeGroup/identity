import { useEffect, useRef, useState } from 'react';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import FormControlLabel from '@mui/material/FormControlLabel';
import MenuItem from '@mui/material/MenuItem';
import Stack from '@mui/material/Stack';
import Switch from '@mui/material/Switch';
import TextField from '@mui/material/TextField';

/**
 * The connection form's own field shape. `name` and `strategy` are immutable after create; `store` is the named
 * local-account store, which the container writes into the connection's `options.store`.
 */
export type ConnectionFormValues = {
  readonly name: string;
  readonly displayName: string;
  readonly strategy: string;
  readonly store: string;
  readonly isEnabled: boolean;
};

export type ConnectionFormDialogProps = {
  readonly open: boolean;
  readonly title: string;
  readonly submitLabel: string;
  readonly initialValues: ConnectionFormValues;
  /** Whether the name and strategy may be edited. False on edit, since both are immutable. */
  readonly nameEditable?: boolean;
  /** The registered strategies the select offers. */
  readonly strategies?: readonly string[];
  readonly isSubmitting?: boolean;
  readonly error?: string | undefined;
  readonly onSubmit: (values: ConnectionFormValues) => void;
  readonly onCancel: () => void;
};

export const emptyConnectionFormValues: ConnectionFormValues = {
  name: '',
  displayName: '',
  strategy: 'local',
  store: 'default',
  isEnabled: true,
};

const DEFAULT_STRATEGIES: readonly string[] = ['local'];

const NAME_PATTERN = /^[A-Za-z0-9][A-Za-z0-9_-]{0,127}$/;

/** Whether a name is 1 to 128 ASCII letters, digits, `-` and `_`, starting with a letter or digit. */
export function isValidConnectionName(name: string): boolean {
  return NAME_PATTERN.test(name);
}

/**
 * Stateless connection create/edit form rendered in a dialog. The name and strategy are immutable after creation,
 * so they are read-only when {@link ConnectionFormDialogProps.nameEditable} is false. The name is validated like the
 * server's connection name rules. The field state is local because it is intrinsic to the controlled inputs (it
 * reseeds on the rising edge of `open`), so the component still renders in isolation under Storybook/RTL. All
 * persistence and server-error handling lives in the container that wires the mutation.
 */
export function ConnectionFormDialog({
  open,
  title,
  submitLabel,
  initialValues,
  nameEditable = true,
  strategies = DEFAULT_STRATEGIES,
  isSubmitting = false,
  error,
  onSubmit,
  onCancel,
}: ConnectionFormDialogProps) {
  const [values, setValues] = useState<ConnectionFormValues>(initialValues);
  const wasOpenRef = useRef(open);

  useEffect(() => {
    if (open && !wasOpenRef.current) {
      setValues(initialValues);
    }
    wasOpenRef.current = open;
  }, [open, initialValues]);

  const name = values.name.trim();
  const displayName = values.displayName.trim();
  const store = values.store.trim();
  const nameInvalid = nameEditable && name.length > 0 && !isValidConnectionName(name);
  const canSubmit =
    displayName.length > 0 &&
    store.length > 0 &&
    (!nameEditable || isValidConnectionName(name)) &&
    !isSubmitting;

  const handleSubmit = () => {
    if (!canSubmit) {
      return;
    }
    onSubmit({
      name,
      displayName,
      strategy: values.strategy,
      store,
      isEnabled: values.isEnabled,
    });
  };

  return (
    <Dialog open={open} onClose={onCancel} fullWidth maxWidth="sm">
      <DialogTitle>{title}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {error && (
            <Alert severity="error" data-testid="connection-form-error">
              {error}
            </Alert>
          )}
          <TextField
            label="Name"
            value={values.name}
            onChange={(event) => setValues((v) => ({ ...v, name: event.target.value }))}
            required={nameEditable}
            disabled={!nameEditable}
            fullWidth
            {...(nameEditable ? { autoFocus: true } : {})}
            error={nameInvalid}
            helperText={
              nameEditable
                ? 'Letters, digits, - and _, up to 128 characters, starting with a letter or digit.'
                : 'The name is immutable after creation.'
            }
            slotProps={{ htmlInput: { 'data-testid': 'connection-name' } }}
          />
          <TextField
            label="Display name"
            value={values.displayName}
            onChange={(event) => setValues((v) => ({ ...v, displayName: event.target.value }))}
            required
            fullWidth
            {...(nameEditable ? {} : { autoFocus: true })}
            slotProps={{ htmlInput: { 'data-testid': 'connection-display-name' } }}
          />
          <TextField
            select
            label="Strategy"
            value={values.strategy}
            onChange={(event) => setValues((v) => ({ ...v, strategy: event.target.value }))}
            disabled={!nameEditable}
            fullWidth
            slotProps={{ htmlInput: { 'data-testid': 'connection-strategy' } }}
          >
            {strategies.map((strategy) => (
              <MenuItem key={strategy} value={strategy}>
                {strategy}
              </MenuItem>
            ))}
          </TextField>
          <TextField
            label="Store"
            value={values.store}
            onChange={(event) => setValues((v) => ({ ...v, store: event.target.value }))}
            required
            fullWidth
            helperText="The named local-account store that holds this connection's accounts."
            slotProps={{ htmlInput: { 'data-testid': 'connection-store' } }}
          />
          <FormControlLabel
            control={
              <Switch
                checked={values.isEnabled}
                onChange={(event) => setValues((v) => ({ ...v, isEnabled: event.target.checked }))}
              />
            }
            label="Enabled"
          />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onCancel} disabled={isSubmitting} data-testid="connection-form-cancel">
          Cancel
        </Button>
        <Button
          variant="contained"
          onClick={handleSubmit}
          disabled={!canSubmit}
          data-testid="connection-form-submit"
        >
          {isSubmitting ? 'Saving…' : submitLabel}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
