import { useEffect } from 'react';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';
import {
  DataStateBoundary,
  LoadMoreButton,
  TeamTable,
  TenantTable,
  UnavailableCellsAlert,
} from '@ncodegroup/identity-ui';
import {
  pagedItems,
  useGetCallerQuery,
  useGetCellScopeQuery,
  useListCellTenantsInfiniteQuery,
  useListCallerTeamsQuery,
  useListCallerTenantsQuery,
} from '@ncodegroup/identity-store';
import { soleDestination } from '../access.js';
import { cellRoute, platformRoute, teamRoute, tenantRoute, type ConsoleRoute } from '../routes.js';
import { useCallerAccess } from '../useCallerAccess.js';
import { useIsCellConsole } from '../consoleKind.js';

export type HomePageProps = {
  /** Opens a route; `replace` swaps the home entry in the history instead of adding one. */
  readonly onNavigate: (route: ConsoleRoute, options?: { readonly replace?: boolean }) => void;
};

/** The roles at a cell's node that may list its administrators. */
const cellAdministratorRoles = ['CellAdmin', 'GlobalAdmin'];

/**
 * Container: the caller's home. A console that manages one cell shows that cell's tenants; every other console shows
 * the places the caller holds authority.
 */
export function HomePage({ onNavigate }: HomePageProps) {
  const isCellConsole = useIsCellConsole();
  return isCellConsole ? (
    <CellHome onNavigate={onNavigate} />
  ) : (
    <AuthorityHome onNavigate={onNavigate} />
  );
}

/**
 * Container: the home of a console that manages one cell. Lists the tenants the cell holds, a page at a time, with the
 * team of each and a link into the tenant, and links to the cell's administrators for a caller who holds `CellAdmin` or
 * `GlobalAdmin` at the cell. A tenant opens in the console's own API, which addresses no other cell.
 */
function CellHome({ onNavigate }: HomePageProps) {
  const caller = useGetCallerQuery();
  const scope = useGetCellScopeQuery();
  const { data, isLoading, error, hasNextPage, isFetchingNextPage, fetchNextPage } =
    useListCellTenantsInfiniteQuery();
  const tenants = pagedItems(data);
  const canListAdmins = (caller.data?.cellRoles ?? []).some((role) =>
    cellAdministratorRoles.includes(role),
  );

  return (
    <>
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
        <Typography variant="h5" component="h2" data-testid="cell-title">
          {scope.data ? `Cell ${scope.data.name}` : 'Cell'}
        </Typography>
        {canListAdmins && (
          <Button
            variant="outlined"
            onClick={() => onNavigate(cellRoute('admins'))}
            data-testid="home-open-cell-admins"
          >
            Administrators
          </Button>
        )}
      </Box>
      <DataStateBoundary
        isLoading={isLoading}
        error={error ? 'Failed to load the tenants of this cell.' : undefined}
        isEmpty={tenants.length === 0}
        emptyMessage="This cell holds no tenants yet."
      >
        <Table size="small" aria-label="Tenants">
          <TableHead>
            <TableRow>
              <TableCell>Display name</TableCell>
              <TableCell>Tenant</TableCell>
              <TableCell>Team</TableCell>
              <TableCell>Status</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {tenants.map((tenant) => (
              <TableRow
                key={tenant.tenantId}
                hover
                sx={{ cursor: 'pointer' }}
                onClick={() => onNavigate(tenantRoute(tenant.tenantId))}
                data-testid={`tenant-row-${tenant.tenantId}`}
              >
                <TableCell>{tenant.displayName}</TableCell>
                <TableCell>{tenant.tenantId}</TableCell>
                <TableCell data-testid={`tenant-team-${tenant.tenantId}`}>
                  {tenant.teamId}
                </TableCell>
                <TableCell>
                  <Chip
                    size="small"
                    color={tenant.isDisabled ? 'default' : 'success'}
                    label={tenant.isDisabled ? 'Disabled' : 'Active'}
                  />
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </DataStateBoundary>
      <LoadMoreButton
        hasMore={hasNextPage}
        isLoading={isFetchingNextPage}
        onLoadMore={() => void fetchNextPage()}
      />
    </>
  );
}

/**
 * Container: the caller's home (ADR-1465). Lists the places the signed-in caller holds authority: the platform for a
 * platform administrator, and the teams and tenants at which it holds a role, and names each cell that did not answer,
 * whose tenants are missing. When exactly one destination exists and every cell answered, it opens it at once,
 * replacing the home entry. Owns no state; the caller's access comes from the store layer.
 */
function AuthorityHome({ onNavigate }: HomePageProps) {
  const caller = useGetCallerQuery();
  const teamsQuery = useListCallerTeamsQuery();
  const tenantsQuery = useListCallerTenantsQuery();

  const teams = teamsQuery.data?.items;
  const tenants = tenantsQuery.data?.items;
  const callerData = caller.data;

  const access = useCallerAccess(true);

  const unavailableCells = tenantsQuery.data?.unavailableCells ?? [];
  const hasUnavailableCells = unavailableCells.length > 0;

  useEffect(() => {
    // A cell that did not answer may hold another destination, so the home opens none on partial access.
    if (!access || hasUnavailableCells) {
      return;
    }
    const destination = soleDestination(access);
    if (destination) {
      onNavigate(destination, { replace: true });
    }
  }, [access, hasUnavailableCells, onNavigate]);

  const isLoading = caller.isLoading || teamsQuery.isLoading || tenantsQuery.isLoading;
  const failed = Boolean(caller.error || teamsQuery.error || tenantsQuery.error);
  const teamItems = teams ?? [];
  const tenantItems = tenants ?? [];
  const isPlatformAdministrator = callerData?.isPlatformAdministrator ?? false;
  const isEmpty =
    !hasUnavailableCells &&
    !isPlatformAdministrator &&
    teamItems.length === 0 &&
    tenantItems.length === 0;

  return (
    <>
      <Typography variant="h5" component="h2" sx={{ mb: 1 }}>
        Home
      </Typography>
      <UnavailableCellsAlert cells={unavailableCells} />
      <DataStateBoundary
        isLoading={isLoading}
        error={failed ? 'Failed to load your access.' : undefined}
        isEmpty={isEmpty}
        emptyMessage="You hold no role in this console. Ask an administrator for access to a team or a tenant."
      >
        {isPlatformAdministrator && (
          <Box sx={{ mb: 3 }}>
            <Button
              variant="contained"
              onClick={() => onNavigate(platformRoute('teams'))}
              data-testid="home-open-platform"
            >
              Open the platform
            </Button>
          </Box>
        )}
        {teamItems.length > 0 && (
          <Box sx={{ mb: 3 }}>
            <Typography variant="h6" component="h3" sx={{ mb: 1 }}>
              Teams
            </Typography>
            <TeamTable
              teams={teamItems.map((t) => ({
                teamId: t.teamId,
                displayName: t.displayName,
                isDisabled: t.isDisabled,
              }))}
              onSelect={(teamId) => onNavigate(teamRoute(teamId))}
            />
          </Box>
        )}
        {tenantItems.length > 0 && (
          <Box sx={{ mb: 3 }}>
            <Typography variant="h6" component="h3" sx={{ mb: 1 }}>
              Tenants
            </Typography>
            <TenantTable
              tenants={tenantItems.map((t) => ({
                tenantId: t.tenantId,
                displayName: t.displayName,
                issuer: t.issuer,
                mtlsEndpointAliasAuthority: t.mtlsEndpointAliasAuthority,
                isDisabled: t.isDisabled,
                ...(t.cell !== undefined && { cell: { name: t.cell, region: null } }),
              }))}
              showCell={tenantItems.some((t) => t.cell !== undefined)}
              onSelect={(tenantId, cell) => onNavigate(tenantRoute(tenantId, undefined, cell))}
            />
          </Box>
        )}
      </DataStateBoundary>
    </>
  );
}
