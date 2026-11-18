import { useState } from 'react';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import {
  apiErrorMessage,
  apiProblem,
  type ApiError,
  useCreateTransferKeyMutation,
  useExportTenantMutation,
  useGetCallerQuery,
  useGetTenantQuery,
  useGetTenantUsageQuery,
  useImportTenantMutation,
  useListCellsQuery,
  useReleaseMovedTenantMutation,
} from '@ncode/store';
import {
  DataStateBoundary,
  TenantMoveDialog,
  TenantMoveOutcomeUnknownError,
  TenantPlacedElsewhereError,
  TenantUsageTable,
  type TenantMoveDialogOperations,
} from '@ncode/ui';
import { tenantRoute, type ConsoleRoute, type TenantOwnSurface } from '../routes.js';
import { useTenantCell } from '../tenantCell.js';
import { OwnerAccessTab } from './OwnerAccessTab.js';
import { OwnerEncryptionKeysTab } from './OwnerEncryptionKeysTab.js';
import { OwnerSecretsTab } from './OwnerSecretsTab.js';
import { OwnerSettingsTab } from './OwnerSettingsTab.js';

export type TenantDetailPageProps = {
  readonly tenantId: string;
  /** The tenant surface the address bar names. */
  readonly surface: TenantOwnSurface;
  /** Opens a route: after a completed move, the same surface of the tenant in the cell it moved to. */
  readonly onNavigate: (route: ConsoleRoute) => void;
};

type TenantTabProps = {
  readonly tenantId: string;
};

type TenantMoveActionProps = {
  readonly tenantId: string;
  /** The tenant surface the address bar names, which the tenant opens on in the cell it moved to. */
  readonly surface: TenantOwnSurface;
  readonly onNavigate: (route: ConsoleRoute) => void;
};

/**
 * Container: the current tenant's own secrets, encryption keys, settings, access, or usage surface (as opposed to its
 * clients'), as the `surface` prop names. The address bar selects the surface (ADR-1255), so this page holds no tab
 * state. Wires the owner-generic {@link OwnerSecretsTab}/{@link OwnerSettingsTab}, the {@link OwnerEncryptionKeysTab}
 * and the {@link OwnerAccessTab} (the tenant's administrators and owners) to the tenant, and shows the tenant's usage
 * ledger (ADR-1199) through {@link TenantUsageTable}. The tenant id comes from the active scope, so the boundary stays
 * structural (a tenant administrator can never address another tenant). A platform administrator also gets the
 * {@link TenantMoveAction}.
 */
export function TenantDetailPage({ tenantId, surface, onNavigate }: TenantDetailPageProps) {
  const owner = { kind: 'tenant', tenantId } as const;

  return (
    <>
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 2 }}>
        <Stack>
          <Typography variant="h5" component="h2">
            Tenant
          </Typography>
          <Typography variant="body2" sx={{ color: 'text.secondary' }}>
            {tenantId}
          </Typography>
        </Stack>
        <TenantMoveAction tenantId={tenantId} surface={surface} onNavigate={onNavigate} />
      </Box>
      {surface === 'secrets' && <OwnerSecretsTab owner={owner} ownerLabel="tenant" />}
      {surface === 'encryption-keys' && (
        <OwnerEncryptionKeysTab owner={owner} ownerLabel="tenant" />
      )}
      {surface === 'settings' && <OwnerSettingsTab owner={owner} />}
      {surface === 'access' && <OwnerAccessTab owner={owner} />}
      {surface === 'usage' && <TenantUsageTab tenantId={tenantId} />}
    </>
  );
}

/**
 * The Move action of a tenant: shown to a platform administrator (the export and the release are authorized at the
 * platform node) when the tenant lives in a named cell, and enabled only while the tenant is disabled. The cells the
 * dialog offers come from the platform's cells, by the region of the tenant's own cell; the four calls of the move each
 * address the cell they concern. An export that the global directory refuses because it already places the tenant in
 * another cell hands the dialog that cell, so the dialog offers the release of the copy the tenant's own cell holds.
 * Closing the dialog after a completed move opens the same surface of the tenant in the cell it moved to.
 */
function TenantMoveAction({ tenantId, surface, onNavigate }: TenantMoveActionProps) {
  const cell = useTenantCell();
  const [isOpen, setIsOpen] = useState(false);
  const caller = useGetCallerQuery();
  const isShown = caller.data?.isPlatformAdministrator === true && cell !== undefined;
  const tenant = useGetTenantQuery({ tenantId, cell }, { skip: !isShown });
  const cells = useListCellsQuery(undefined, { skip: !isShown });
  const [createTransferKey] = useCreateTransferKeyMutation();
  const [exportTenant] = useExportTenantMutation();
  const [importTenant] = useImportTenantMutation();
  const [releaseMovedTenant] = useReleaseMovedTenantMutation();

  if (!isShown) {
    return null;
  }

  const isDisabled = tenant.data?.isDisabled === true;

  // Each call rejects with an Error that carries the server's reason, which the dialog shows
  // against its stage; a problem naming the cell the global directory already places the tenant
  // in rejects with a TenantPlacedElsewhereError, so the dialog offers the release; an import
  // whose problem says that the outcome of the move is unknown rejects with a
  // TenantMoveOutcomeUnknownError, so the dialog says so.
  const run = async <T,>(call: { unwrap: () => Promise<T> }): Promise<T> => {
    try {
      return await call.unwrap();
    } catch (error) {
      const message = apiErrorMessage(error as ApiError);
      const problem = apiProblem(error as ApiError);
      const placedCell = problem?.placedCell;
      if (typeof placedCell === 'string' && placedCell.length > 0) {
        throw new TenantPlacedElsewhereError(message, placedCell, { cause: error });
      }
      throw problem?.moveOutcome === 'unknown'
        ? new TenantMoveOutcomeUnknownError(message, { cause: error })
        : new Error(message, { cause: error });
    }
  };
  const operations: TenantMoveDialogOperations = {
    createTransferKey: (target) => run(createTransferKey({ cell: target })),
    exportTenant: async (source, id, targetCell, jwk) =>
      (await run(exportTenant({ cell: source, tenantId: id, targetCell, jwk }))).package,
    importTenant: async (target, kid, sealedPackage) => {
      await run(importTenant({ cell: target, kid, package: sealedPackage }));
    },
    releaseMovedTenant: async (source, id) => {
      await run(releaseMovedTenant({ cell: source, tenantId: id }));
    },
  };

  return (
    <>
      <Tooltip title={isDisabled ? '' : 'Disable the tenant before moving it.'}>
        <span>
          <Button
            variant="outlined"
            onClick={() => setIsOpen(true)}
            disabled={!isDisabled}
            data-testid="move-tenant"
          >
            Move
          </Button>
        </span>
      </Tooltip>
      <TenantMoveDialog
        open={isOpen}
        tenantId={tenantId}
        isDisabled={isDisabled}
        sourceCell={cell}
        cells={cells.data ?? []}
        isCellsLoading={cells.isLoading}
        isCellsFailed={Boolean(cells.error)}
        operations={operations}
        onClose={() => setIsOpen(false)}
        onMoved={(targetCell) => onNavigate(tenantRoute(tenantId, surface, targetCell))}
      />
    </>
  );
}

/**
 * The tenant's usage ledger for a range of UTC days. An empty bound is left to the server, which defaults the range to
 * the last 30 days and refuses one longer than 366 days; the effective range is echoed back and shown.
 */
function TenantUsageTab({ tenantId }: TenantTabProps) {
  const cell = useTenantCell();
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const { data, isFetching, error } = useGetTenantUsageQuery({
    tenantId,
    from: from || undefined,
    to: to || undefined,
    cell,
  });

  return (
    <Stack spacing={2}>
      <Stack direction="row" spacing={2} sx={{ alignItems: 'center' }}>
        <TextField
          type="date"
          size="small"
          label="From (UTC)"
          value={from}
          onChange={(event) => setFrom(event.target.value)}
          slotProps={{ inputLabel: { shrink: true }, htmlInput: { 'data-testid': 'usage-from' } }}
        />
        <TextField
          type="date"
          size="small"
          label="To (UTC)"
          value={to}
          onChange={(event) => setTo(event.target.value)}
          slotProps={{ inputLabel: { shrink: true }, htmlInput: { 'data-testid': 'usage-to' } }}
        />
        {data && (
          <Typography variant="body2" sx={{ color: 'text.secondary' }} data-testid="usage-range">
            {data.from} to {data.to}
          </Typography>
        )}
      </Stack>
      <DataStateBoundary
        isLoading={isFetching && !data}
        error={error ? apiErrorMessage(error as ApiError) : undefined}
        isEmpty={(data?.entries.length ?? 0) === 0}
        emptyMessage="No usage recorded in this range."
      >
        <TenantUsageTable entries={data?.entries ?? []} />
      </DataStateBoundary>
    </Stack>
  );
}
