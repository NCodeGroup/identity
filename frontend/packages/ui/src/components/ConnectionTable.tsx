import Chip from '@mui/material/Chip';
import IconButton from '@mui/material/IconButton';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Tooltip from '@mui/material/Tooltip';
import DeleteIcon from '@mui/icons-material/Delete';
import EditIcon from '@mui/icons-material/Edit';

/** The minimal connection shape this presentational table needs (decoupled from the API contract). */
export type ConnectionRow = {
  readonly connectionId: string;
  readonly name: string;
  readonly displayName: string;
  readonly strategy: string;
  readonly isEnabled: boolean;
};

export type ConnectionTableProps = {
  readonly connections: readonly ConnectionRow[];
  readonly onSelect?: (connectionId: string) => void;
  readonly onEdit?: (connectionId: string) => void;
  readonly onDelete?: (connectionId: string) => void;
};

/**
 * Stateless connection table. Receives rows and callbacks as props; holds no state and does no fetching, so it
 * renders directly in Storybook and under RTL. Selecting a row opens that connection. The row-level edit and delete
 * controls appear only when their callbacks are supplied (and stop propagation so they never trigger selection).
 */
export function ConnectionTable({ connections, onSelect, onEdit, onDelete }: ConnectionTableProps) {
  const showActions = Boolean(onEdit || onDelete);
  return (
    <Table size="small" aria-label="Connections">
      <TableHead>
        <TableRow>
          <TableCell>Name</TableCell>
          <TableCell>Display name</TableCell>
          <TableCell>Strategy</TableCell>
          <TableCell>Enabled</TableCell>
          {showActions && <TableCell align="right">Actions</TableCell>}
        </TableRow>
      </TableHead>
      <TableBody>
        {connections.map((connection) => (
          <TableRow
            key={connection.connectionId}
            hover={Boolean(onSelect)}
            {...(onSelect
              ? {
                  onClick: () => onSelect(connection.connectionId),
                  sx: { cursor: 'pointer' },
                }
              : {})}
            data-testid={`connection-row-${connection.connectionId}`}
          >
            <TableCell>{connection.name}</TableCell>
            <TableCell>{connection.displayName}</TableCell>
            <TableCell>{connection.strategy}</TableCell>
            <TableCell>
              <Chip
                size="small"
                color={connection.isEnabled ? 'success' : 'default'}
                label={connection.isEnabled ? 'Enabled' : 'Disabled'}
              />
            </TableCell>
            {showActions && (
              <TableCell align="right">
                <Stack direction="row" spacing={0.5} sx={{ justifyContent: 'flex-end' }}>
                  {onEdit && (
                    <Tooltip title="Edit connection">
                      <IconButton
                        size="small"
                        aria-label={`Edit ${connection.name}`}
                        data-testid={`connection-edit-${connection.connectionId}`}
                        onClick={(event) => {
                          event.stopPropagation();
                          onEdit(connection.connectionId);
                        }}
                      >
                        <EditIcon fontSize="small" />
                      </IconButton>
                    </Tooltip>
                  )}
                  {onDelete && (
                    <Tooltip title="Delete connection">
                      <IconButton
                        size="small"
                        aria-label={`Delete ${connection.name}`}
                        data-testid={`connection-delete-${connection.connectionId}`}
                        onClick={(event) => {
                          event.stopPropagation();
                          onDelete(connection.connectionId);
                        }}
                      >
                        <DeleteIcon fontSize="small" />
                      </IconButton>
                    </Tooltip>
                  )}
                </Stack>
              </TableCell>
            )}
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
