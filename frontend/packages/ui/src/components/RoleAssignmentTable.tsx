import IconButton from '@mui/material/IconButton';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import DeleteIcon from '@mui/icons-material/Delete';

/** The minimal role assignment shape this presentational table needs (decoupled from the API contract). */
export type RoleAssignmentRow = {
  readonly assignmentId: string;
  readonly principalId: string;
  readonly displayName: string | null;
  readonly principalType: string | null;
};

/**
 * Describes a holder that has a display name: its principal type and identifier, or the identifier alone when the type
 * is unknown.
 */
function describeHolder(assignment: RoleAssignmentRow): string {
  switch (assignment.principalType) {
    case 'local_account':
      return `User · ${assignment.principalId}`;
    case 'client':
      return `Machine client · ${assignment.principalId}`;
    case 'federated_user':
      return `Federated user · ${assignment.principalId}`;
    case null:
      return assignment.principalId;
    default:
      return `${assignment.principalType} · ${assignment.principalId}`;
  }
}

export type RoleAssignmentTableProps = {
  readonly label: string;
  readonly testIdPrefix: string;
  readonly assignments: readonly RoleAssignmentRow[];
  readonly onRevoke?: (principalId: string) => void;
};

/**
 * Stateless table of the principals that hold one role on a node. Receives rows and callbacks as
 * props; holds no state and does no fetching, so it renders directly in Storybook and under RTL. The
 * revoke control appears only when `onRevoke` is supplied.
 */
export function RoleAssignmentTable({
  label,
  testIdPrefix,
  assignments,
  onRevoke,
}: RoleAssignmentTableProps) {
  return (
    <Table size="small" aria-label={label}>
      <TableHead>
        <TableRow>
          <TableCell>Principal</TableCell>
          {onRevoke && <TableCell align="right">Actions</TableCell>}
        </TableRow>
      </TableHead>
      <TableBody>
        {assignments.map((assignment) => (
          <TableRow
            key={assignment.assignmentId}
            data-testid={`${testIdPrefix}-row-${assignment.principalId}`}
          >
            <TableCell>
              <div data-testid={`${testIdPrefix}-name-${assignment.principalId}`}>
                {assignment.displayName ?? assignment.principalId}
              </div>
              {assignment.displayName !== null && (
                <Typography variant="caption" color="text.secondary">
                  {describeHolder(assignment)}
                </Typography>
              )}
            </TableCell>
            {onRevoke && (
              <TableCell align="right">
                <Tooltip title="Revoke">
                  <IconButton
                    size="small"
                    aria-label={`Revoke ${assignment.displayName ?? assignment.principalId}`}
                    data-testid={`${testIdPrefix}-revoke-${assignment.principalId}`}
                    onClick={() => onRevoke(assignment.principalId)}
                  >
                    <DeleteIcon fontSize="small" />
                  </IconButton>
                </Tooltip>
              </TableCell>
            )}
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
