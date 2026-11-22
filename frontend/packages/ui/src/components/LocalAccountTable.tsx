import Chip from '@mui/material/Chip';
import IconButton from '@mui/material/IconButton';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Tooltip from '@mui/material/Tooltip';
import BlockIcon from '@mui/icons-material/Block';
import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import DeleteIcon from '@mui/icons-material/Delete';
import EditIcon from '@mui/icons-material/Edit';
import KeyIcon from '@mui/icons-material/Key';

/** The minimal local-account shape this presentational table needs (decoupled from the API contract). */
export type LocalAccountRow = {
  readonly localAccountId: string;
  readonly displayName: string;
  readonly userName: string;
  readonly email: string | null;
  readonly principalId: string | null;
  readonly isEnabled: boolean;
};

export type LocalAccountTableProps = {
  readonly accounts: readonly LocalAccountRow[];
  readonly onEdit?: (localAccountId: string) => void;
  readonly onResetPassword?: (localAccountId: string) => void;
  readonly onToggleEnabled?: (localAccountId: string) => void;
  readonly onDelete?: (localAccountId: string) => void;
};

/**
 * Stateless local-account table. Receives rows and callbacks as props; holds no state and does no fetching. Each
 * row-level action appears only when its callback is supplied.
 */
export function LocalAccountTable({
  accounts,
  onEdit,
  onResetPassword,
  onToggleEnabled,
  onDelete,
}: LocalAccountTableProps) {
  const showActions = Boolean(onEdit || onResetPassword || onToggleEnabled || onDelete);
  return (
    <Table size="small" aria-label="Users">
      <TableHead>
        <TableRow>
          <TableCell>Display name</TableCell>
          <TableCell>Username</TableCell>
          <TableCell>Email</TableCell>
          <TableCell>Principal ID</TableCell>
          <TableCell>Status</TableCell>
          {showActions && <TableCell align="right">Actions</TableCell>}
        </TableRow>
      </TableHead>
      <TableBody>
        {accounts.map((account) => {
          const id = account.localAccountId;
          const toggleLabel = account.isEnabled ? 'Disable' : 'Enable';
          return (
            <TableRow key={id} data-testid={`local-account-row-${id}`}>
              <TableCell>{account.displayName}</TableCell>
              <TableCell>{account.userName}</TableCell>
              <TableCell>{account.email ?? '—'}</TableCell>
              <TableCell>{account.principalId ?? '—'}</TableCell>
              <TableCell>
                <Chip
                  size="small"
                  color={account.isEnabled ? 'success' : 'default'}
                  label={account.isEnabled ? 'Active' : 'Disabled'}
                />
              </TableCell>
              {showActions && (
                <TableCell align="right">
                  <Stack direction="row" spacing={0.5} sx={{ justifyContent: 'flex-end' }}>
                    {onEdit && (
                      <Tooltip title="Edit">
                        <IconButton
                          size="small"
                          aria-label="Edit"
                          data-testid={`local-account-edit-${id}`}
                          onClick={() => onEdit(id)}
                        >
                          <EditIcon fontSize="small" />
                        </IconButton>
                      </Tooltip>
                    )}
                    {onResetPassword && (
                      <Tooltip title="Reset password">
                        <IconButton
                          size="small"
                          aria-label="Reset password"
                          data-testid={`local-account-password-${id}`}
                          onClick={() => onResetPassword(id)}
                        >
                          <KeyIcon fontSize="small" />
                        </IconButton>
                      </Tooltip>
                    )}
                    {onToggleEnabled && (
                      <Tooltip title={toggleLabel}>
                        <IconButton
                          size="small"
                          aria-label={toggleLabel}
                          data-testid={`local-account-toggle-${id}`}
                          onClick={() => onToggleEnabled(id)}
                        >
                          {account.isEnabled ? (
                            <BlockIcon fontSize="small" />
                          ) : (
                            <CheckCircleIcon fontSize="small" />
                          )}
                        </IconButton>
                      </Tooltip>
                    )}
                    {onDelete && (
                      <Tooltip title="Delete">
                        <IconButton
                          size="small"
                          aria-label="Delete"
                          data-testid={`local-account-delete-${id}`}
                          onClick={() => onDelete(id)}
                        >
                          <DeleteIcon fontSize="small" />
                        </IconButton>
                      </Tooltip>
                    )}
                  </Stack>
                </TableCell>
              )}
            </TableRow>
          );
        })}
      </TableBody>
    </Table>
  );
}
