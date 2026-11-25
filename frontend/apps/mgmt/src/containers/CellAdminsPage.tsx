import { useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import TextField from '@mui/material/TextField';
import ToggleButton from '@mui/material/ToggleButton';
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup';
import Typography from '@mui/material/Typography';
import {
  ConfirmDialog,
  DataStateBoundary,
  RoleAssignmentTable,
  type RoleAssignmentRow,
} from '@ncodegroup/identity-ui';
import {
  apiErrorMessage,
  useAddCellAdminMutation,
  useListCellAdminsQuery,
  useRemoveCellAdminMutation,
  type ApiError,
} from '@ncodegroup/identity-store';

/** The prefix of the test ids of the administrator list, which is the role it lists. */
const testIdPrefix = 'CellAdmin';

/**
 * Container: the administrators of the cell the console manages, with a form to grant the `CellAdmin` role to a
 * principal and a confirmed revoke. The grant form and revoke controls appear only when the list says the caller may
 * change it; a refused grant or revoke shows the server's error.
 */
export function CellAdminsPage() {
  const { data, isLoading, error: loadError } = useListCellAdminsQuery();
  const [addCellAdmin, { isLoading: isGranting }] = useAddCellAdminMutation();
  const [removeCellAdmin, { isLoading: isRevoking }] = useRemoveCellAdminMutation();
  const [grantBy, setGrantBy] = useState<'userName' | 'principalId'>('userName');
  const [principalId, setPrincipalId] = useState('');
  const [revoking, setRevoking] = useState<RoleAssignmentRow>();
  const [error, setError] = useState<string>();

  const assignments: RoleAssignmentRow[] = (data?.items ?? []).map((holder) => ({
    assignmentId: holder.assignmentId,
    principalId: holder.principalId,
    displayName: holder.principal?.displayName ?? null,
    principalType: holder.principal?.principalType ?? null,
  }));
  const canChange = data?.canChange ?? false;
  const isReadOnly = data !== undefined && !data.canChange;
  const trimmed = principalId.trim();

  const handleGrant = async () => {
    if (trimmed.length === 0) {
      return;
    }
    setError(undefined);
    try {
      await addCellAdmin(
        grantBy === 'userName' ? { userName: trimmed } : { principalId: trimmed },
      ).unwrap();
      setPrincipalId('');
    } catch (grantError) {
      setError(apiErrorMessage(grantError as ApiError));
    }
  };

  const handleRevoke = async () => {
    if (revoking === undefined) {
      return;
    }
    setError(undefined);
    try {
      await removeCellAdmin({ principalId: revoking.principalId }).unwrap();
    } catch (revokeError) {
      setError(apiErrorMessage(revokeError as ApiError));
    }
    setRevoking(undefined);
  };

  return (
    <Box>
      <Typography variant="h5" component="h2">
        Administrators
      </Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
        The principals that administer this cell: they list its tenants and manage its
        administrators.
      </Typography>
      {error && (
        <Alert severity="error" sx={{ mb: 1 }} data-testid={`${testIdPrefix}-error`}>
          {error}
        </Alert>
      )}
      {isReadOnly && (
        <Typography
          variant="body2"
          color="text.secondary"
          sx={{ mb: 1 }}
          data-testid={`${testIdPrefix}-read-only`}
        >
          You can see who holds this role but cannot grant or revoke it.
        </Typography>
      )}
      {canChange && (
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 1 }}>
          <ToggleButtonGroup
            size="small"
            exclusive
            value={grantBy}
            aria-label="Grant by"
            onChange={(_event, value: 'userName' | 'principalId' | null) => {
              if (value !== null) {
                setGrantBy(value);
              }
            }}
          >
            <ToggleButton value="userName" data-testid={`${testIdPrefix}-grant-by-user-name`}>
              Username
            </ToggleButton>
            <ToggleButton value="principalId" data-testid={`${testIdPrefix}-grant-by-principal-id`}>
              Principal ID
            </ToggleButton>
          </ToggleButtonGroup>
          <TextField
            size="small"
            label={grantBy === 'userName' ? 'Username' : 'Principal ID'}
            value={principalId}
            onChange={(event) => setPrincipalId(event.target.value)}
            slotProps={{ htmlInput: { 'data-testid': `${testIdPrefix}-principal` } }}
          />
          <Button
            variant="contained"
            disabled={trimmed.length === 0 || isGranting}
            onClick={handleGrant}
            data-testid={`${testIdPrefix}-grant`}
          >
            Grant
          </Button>
        </Box>
      )}
      <DataStateBoundary
        isLoading={isLoading}
        error={loadError ? 'Failed to load administrators.' : undefined}
        isEmpty={assignments.length === 0}
        emptyMessage="No one administers this cell."
      >
        <RoleAssignmentTable
          label="Administrators"
          testIdPrefix={testIdPrefix}
          assignments={assignments}
          {...(canChange
            ? {
                onRevoke: (id: string) =>
                  setRevoking(assignments.find((row) => row.principalId === id)),
              }
            : {})}
        />
      </DataStateBoundary>
      <ConfirmDialog
        open={revoking !== undefined}
        title="Revoke role"
        message={`Revoke this role from ${revoking?.displayName ?? revoking?.principalId ?? ''}?`}
        confirmLabel="Revoke"
        isBusy={isRevoking}
        onConfirm={handleRevoke}
        onCancel={() => setRevoking(undefined)}
      />
    </Box>
  );
}
