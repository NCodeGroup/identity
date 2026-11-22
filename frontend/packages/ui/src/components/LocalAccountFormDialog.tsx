import { useEffect, useRef, useState } from 'react';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Checkbox from '@mui/material/Checkbox';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import FormControlLabel from '@mui/material/FormControlLabel';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';

/** The local-account form's own field shape. */
export type LocalAccountFormValues = {
  readonly displayName: string;
  readonly userName: string;
  readonly email: string;
  readonly emailVerified: boolean;
  readonly password: string;
  readonly confirmPassword: string;
};

export type LocalAccountFormDialogProps = {
  readonly open: boolean;
  /** Whether the form creates an account, which takes a password, or edits one, which does not. */
  readonly mode: 'create' | 'edit';
  readonly title: string;
  readonly submitLabel: string;
  readonly initialValues: LocalAccountFormValues;
  readonly isSubmitting?: boolean;
  readonly error?: string | undefined;
  readonly onSubmit: (values: LocalAccountFormValues) => void;
  readonly onCancel: () => void;
};

export const emptyLocalAccountFormValues: LocalAccountFormValues = {
  displayName: '',
  userName: '',
  email: '',
  emailVerified: false,
  password: '',
  confirmPassword: '',
};

/**
 * Stateless local-account create/edit form rendered in a dialog. The field state is local because it is intrinsic to
 * the controlled inputs (it reseeds from {@link LocalAccountFormDialogProps.initialValues} each time the dialog
 * opens), so the component still renders in isolation under Storybook/RTL. Only create mode asks for a password. All
 * persistence and server-error handling lives in the container that wires the mutation.
 */
export function LocalAccountFormDialog({
  open,
  mode,
  title,
  submitLabel,
  initialValues,
  isSubmitting = false,
  error,
  onSubmit,
  onCancel,
}: LocalAccountFormDialogProps) {
  const [values, setValues] = useState<LocalAccountFormValues>(initialValues);
  const wasOpenRef = useRef(open);

  // Reseed the form only on the rising edge of `open`, so a new `initialValues` identity while the dialog is already
  // open never discards in-progress edits.
  useEffect(() => {
    if (open && !wasOpenRef.current) {
      setValues(initialValues);
    }
    wasOpenRef.current = open;
  }, [open, initialValues]);

  const isCreate = mode === 'create';
  const displayName = values.displayName.trim();
  const userName = values.userName.trim();
  const email = values.email.trim();
  const passwordsMatch = values.password === values.confirmPassword;
  const showMismatch = isCreate && values.confirmPassword.length > 0 && !passwordsMatch;
  const isPasswordValid = !isCreate || (values.password.length > 0 && passwordsMatch);
  const canSubmit =
    displayName.length > 0 && userName.length > 0 && isPasswordValid && !isSubmitting;

  const handleSubmit = () => {
    if (!canSubmit) {
      return;
    }
    onSubmit({ ...values, displayName, userName, email });
  };

  return (
    <Dialog open={open} onClose={onCancel} fullWidth maxWidth="sm">
      <DialogTitle>{title}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {error && (
            <Alert severity="error" data-testid="local-account-form-error">
              {error}
            </Alert>
          )}
          <TextField
            label="Display name"
            value={values.displayName}
            onChange={(event) => setValues((v) => ({ ...v, displayName: event.target.value }))}
            required
            fullWidth
            autoFocus
            slotProps={{ htmlInput: { 'data-testid': 'local-account-display-name' } }}
          />
          <TextField
            label="Username"
            value={values.userName}
            onChange={(event) => setValues((v) => ({ ...v, userName: event.target.value }))}
            required
            fullWidth
            slotProps={{ htmlInput: { 'data-testid': 'local-account-user-name' } }}
          />
          <TextField
            label="Email"
            type="email"
            value={values.email}
            onChange={(event) => setValues((v) => ({ ...v, email: event.target.value }))}
            fullWidth
            slotProps={{ htmlInput: { 'data-testid': 'local-account-email' } }}
          />
          <FormControlLabel
            control={
              <Checkbox
                checked={values.emailVerified}
                onChange={(event) =>
                  setValues((v) => ({ ...v, emailVerified: event.target.checked }))
                }
                slotProps={{ input: { 'data-testid': 'local-account-email-verified' } as object }}
              />
            }
            label="Email verified"
          />
          {isCreate && (
            <>
              <TextField
                label="Password"
                type="password"
                value={values.password}
                onChange={(event) => setValues((v) => ({ ...v, password: event.target.value }))}
                required
                fullWidth
                autoComplete="new-password"
                slotProps={{ htmlInput: { 'data-testid': 'local-account-password' } }}
              />
              <TextField
                label="Confirm password"
                type="password"
                value={values.confirmPassword}
                onChange={(event) =>
                  setValues((v) => ({ ...v, confirmPassword: event.target.value }))
                }
                required
                fullWidth
                autoComplete="new-password"
                error={showMismatch}
                helperText={showMismatch ? 'The passwords do not match.' : undefined}
                slotProps={{ htmlInput: { 'data-testid': 'local-account-confirm-password' } }}
              />
            </>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onCancel} disabled={isSubmitting} data-testid="local-account-form-cancel">
          Cancel
        </Button>
        <Button
          variant="contained"
          onClick={handleSubmit}
          disabled={!canSubmit}
          data-testid="local-account-form-submit"
        >
          {isSubmitting ? 'Saving…' : submitLabel}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
