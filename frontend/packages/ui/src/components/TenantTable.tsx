import Chip from '@mui/material/Chip';
import IconButton from '@mui/material/IconButton';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import DeleteIcon from '@mui/icons-material/Delete';
import EditIcon from '@mui/icons-material/Edit';

/** A tenant's effective environment. Production is told by `isProduction`, never by the open `type` name. */
export type TenantEnvironmentRow = {
  readonly type: string;
  readonly name: string | null;
  readonly isProduction: boolean;
};

/** The cell a tenant lives in: its name, and the region it runs in when that is known. */
export type TenantCellRow = {
  readonly name: string;
  readonly region: string | null;
};

/** The minimal tenant shape this presentational table needs (decoupled from the API contract). */
export type TenantRow = {
  readonly tenantId: string;
  readonly displayName: string;
  readonly issuer: string | null;
  readonly mtlsEndpointAliasAuthority: string | null;
  readonly isDisabled: boolean;
  /** Absent when the environment cannot be determined. */
  readonly environment?: TenantEnvironmentRow;
  /** Absent when the tenant is not tied to a named cell. */
  readonly cell?: TenantCellRow;
};

export type TenantTableProps = {
  readonly tenants: readonly TenantRow[];
  /** Whether to show the Cell column, for a platform whose tenants live in more than one cell. */
  readonly showCell?: boolean;
  /** Called with the tenant id and the cell, `undefined` when it has none, of the selected row. */
  readonly onSelect?: (tenantId: string, cell: string | undefined) => void;
  /** Called with the tenant id and the cell, `undefined` when it has none, of the edited row. */
  readonly onEdit?: (tenantId: string, cell: string | undefined) => void;
  /** Called with the tenant id and the cell, `undefined` when it has none, of the deleted row. */
  readonly onDelete?: (tenantId: string, cell: string | undefined) => void;
};

/**
 * The id that tells a row apart in its test ids: the tenant id, after the tenant's cell when it has one, because two
 * cells list one tenant while it moves between them.
 */
function rowTestId(tenant: TenantRow): string {
  return tenant.cell === undefined ? tenant.tenantId : `${tenant.cell.name}/${tenant.tenantId}`;
}

/**
 * Stateless tenant table. Receives rows and callbacks as props; holds no state and does no fetching, so it renders
 * directly in Storybook and under RTL. The row-level edit/delete controls appear only when their callbacks are
 * supplied, and stop propagation so they never trigger row selection. A non-production environment is highlighted, so a
 * sandbox tenant is never mistaken for production. The Cell column appears only when `showCell` is set. A row is told
 * apart by its cell and its tenant id, because two cells list one tenant while it moves between them, so each callback
 * receives both.
 */
export function TenantTable({
  tenants,
  showCell = false,
  onSelect,
  onEdit,
  onDelete,
}: TenantTableProps) {
  const showActions = Boolean(onEdit || onDelete);
  return (
    <Table size="small" aria-label="Tenants">
      <TableHead>
        <TableRow>
          <TableCell>Display name</TableCell>
          <TableCell>Tenant ID</TableCell>
          {showCell && <TableCell>Cell</TableCell>}
          <TableCell>Issuer</TableCell>
          <TableCell>mTLS endpoint alias</TableCell>
          <TableCell>Environment</TableCell>
          <TableCell>Status</TableCell>
          {showActions && <TableCell align="right">Actions</TableCell>}
        </TableRow>
      </TableHead>
      <TableBody>
        {tenants.map((tenant) => (
          <TableRow
            key={`${tenant.cell?.name ?? ''}/${tenant.tenantId}`}
            hover={Boolean(onSelect)}
            {...(onSelect
              ? {
                  onClick: () => onSelect(tenant.tenantId, tenant.cell?.name),
                  sx: { cursor: 'pointer' },
                }
              : {})}
            data-testid={`tenant-row-${rowTestId(tenant)}`}
          >
            <TableCell>{tenant.displayName}</TableCell>
            <TableCell>{tenant.tenantId}</TableCell>
            {showCell && (
              <TableCell data-testid={`tenant-cell-${rowTestId(tenant)}`}>
                {tenant.cell && (
                  <>
                    {tenant.cell.name}
                    {tenant.cell.region && (
                      <Typography variant="caption" color="text.secondary" component="div">
                        {tenant.cell.region}
                      </Typography>
                    )}
                  </>
                )}
              </TableCell>
            )}
            <TableCell>{tenant.issuer ?? '—'}</TableCell>
            <TableCell>{tenant.mtlsEndpointAliasAuthority ?? '—'}</TableCell>
            <TableCell>
              <TenantEnvironmentChip testId={rowTestId(tenant)} environment={tenant.environment} />
            </TableCell>
            <TableCell>
              <Chip
                size="small"
                color={tenant.isDisabled ? 'default' : 'success'}
                label={tenant.isDisabled ? 'Disabled' : 'Active'}
              />
            </TableCell>
            {showActions && (
              <TableCell align="right">
                <Stack direction="row" spacing={0.5} sx={{ justifyContent: 'flex-end' }}>
                  {onEdit && (
                    <Tooltip title="Edit tenant">
                      <IconButton
                        size="small"
                        aria-label={`Edit ${tenant.displayName}`}
                        data-testid={`tenant-edit-${rowTestId(tenant)}`}
                        onClick={(event) => {
                          event.stopPropagation();
                          onEdit(tenant.tenantId, tenant.cell?.name);
                        }}
                      >
                        <EditIcon fontSize="small" />
                      </IconButton>
                    </Tooltip>
                  )}
                  {onDelete && (
                    <Tooltip title="Delete tenant">
                      <IconButton
                        size="small"
                        aria-label={`Delete ${tenant.displayName}`}
                        data-testid={`tenant-delete-${rowTestId(tenant)}`}
                        onClick={(event) => {
                          event.stopPropagation();
                          onDelete(tenant.tenantId, tenant.cell?.name);
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

function TenantEnvironmentChip({
  testId,
  environment,
}: {
  readonly testId: string;
  readonly environment: TenantEnvironmentRow | undefined;
}) {
  if (!environment) {
    return <>—</>;
  }
  const label = environment.name ? `${environment.name} (${environment.type})` : environment.type;
  return (
    <Chip
      size="small"
      variant={environment.isProduction ? 'outlined' : 'filled'}
      color={environment.isProduction ? 'default' : 'warning'}
      label={label}
      title={environment.isProduction ? 'Production environment' : 'Non-production environment'}
      data-testid={`tenant-environment-${testId}`}
      data-production={environment.isProduction}
    />
  );
}
