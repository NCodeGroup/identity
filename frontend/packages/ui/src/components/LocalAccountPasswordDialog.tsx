import { useEffect, useRef, useState } from 'react';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogContentText from '@mui/material/DialogContentText';
import DialogTitle from '@mui/material/DialogTitle';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';

export type LocalAccountPasswordDialogProps = {
  readonly open: boolean;
  /** How the dialog names the account whose password it resets. */
  readonly accountLabel: string;
  readonly isSubmitting?: boolean;
  readonly error?: string | undefined;
  readonly onSubmit: (password: string) => void;
  readonly onCancel: () => void;
};

/**
 * Stateless password-reset form rendered in a dialog. The two field values are local because they are intrinsic to the
 * controlled inputs (they clear each time the dialog opens), so the component still renders in isolation under
 * Storybook/RTL. All persistence and server-error handling lives in the container that wires the mutation.
 */
export function LocalAccountPasswordDialog({
  open,
  accountLabel,
  isSubmitting = false,
  error,
  onSubmit,
  onCancel,
}: LocalAccountPasswordDialogProps) {
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const wasOpenRef = useRef(open);

  // Clear the fields only on the rising edge of `open`.
  useEffect(() => {
    if (open && !wasOpenRef.current) {
      setPassword('');
      setConfirmPassword('');
    }
    wasOpenRef.current = open;
  }, [open]);

  const passwordsMatch = password === confirmPassword;
  const showMismatch = confirmPassword.length > 0 && !passwordsMatch;
  const canSubmit = password.length > 0 && passwordsMatch && !isSubmitting;

  const handleSubmit = () => {
    if (!canSubmit) {
      return;
    }
    onSubmit(password);
  };

  return (
    <Dialog open={open} onClose={onCancel} fullWidth maxWidth="sm">
      <DialogTitle>Reset password</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {error && (
            <Alert severity="error" data-testid="local-account-password-error">
              {error}
            </Alert>
          )}
          <DialogContentText>
            Resetting the password of {accountLabel} ends their sessions.
          </DialogContentText>
          <TextField
            label="Password"
            type="password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            required
            fullWidth
            autoFocus
            autoComplete="new-password"
            slotProps={{ htmlInput: { 'data-testid': 'local-account-password-input' } }}
          />
          <TextField
            label="Confirm password"
            type="password"
            value={confirmPassword}
            onChange={(event) => setConfirmPassword(event.target.value)}
            required
            fullWidth
            autoComplete="new-password"
            error={showMismatch}
            helperText={showMismatch ? 'The passwords do not match.' : undefined}
            slotProps={{ htmlInput: { 'data-testid': 'local-account-password-confirm' } }}
          />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button
          onClick={onCancel}
          disabled={isSubmitting}
          data-testid="local-account-password-cancel"
        >
          Cancel
        </Button>
        <Button
          variant="contained"
          onClick={handleSubmit}
          disabled={!canSubmit}
          data-testid="local-account-password-submit"
        >
          {isSubmitting ? 'Saving…' : 'Reset password'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
