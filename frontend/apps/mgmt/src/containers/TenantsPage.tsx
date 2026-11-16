import { useCallback, useEffect, useState } from 'react';
import AddIcon from '@mui/icons-material/Add';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Typography from '@mui/material/Typography';
import {
  ConfirmDialog,
  DataStateBoundary,
  TenantEnvironmentFilter,
  TenantFormDialog,
  TenantPromotionDialog,
  TenantTable,
  allTenantEnvironments,
  emptyTenantFormValues,
  isValidTenantId,
  matchesTenantEnvironment,
  tenantEnvironmentTypes,
  type TenantAddressInput,
  type TenantCellOption,
  type TenantFormValues,
  type TenantPromotionDialogRequest,
  type TenantRow,
} from '@ncode/ui';
import type { PromoteTenantRequest, TenantPromotionResource } from '@ncode/api';
import {
  apiErrorMessage,
  useAppSelector,
  useCreateTenantMutation,
  useDeleteTenantMutation,
  useGetNewTenantAddressQuery,
  useGetTenantAddressQuery,
  useLazyGetTenantConfigurationQuery,
  useListCellsQuery,
  useListTenantsQuery,
  usePreviewTenantPromotionMutation,
  usePromoteTenantMutation,
  useUpdateTenantMutation,
  type ApiError,
  type CellTenantResource,
} from '@ncode/store';

export type TenantsPageProps = {
  /** The team whose tenants are listed and created. */
  readonly teamId: string;
  readonly onOpenTenant: (tenantId: string, cell?: string) => void;
  /**
   * How long the address must stay unchanged before it is previewed. Defaults to 300 ms so typing sends no burst of
   * requests; a test passes `0`.
   */
  readonly addressPreviewDelayMs?: number;
};

type DialogState =
  | { readonly kind: 'none' }
  | { readonly kind: 'create' }
  | { readonly kind: 'promote' }
  | { readonly kind: 'edit'; readonly tenant: CellTenantResource }
  | { readonly kind: 'delete'; readonly tenant: CellTenantResource };

const closed: DialogState = { kind: 'none' };

/**
 * How long the address must stay unchanged before it is previewed, so typing sends no burst of requests.
 */
const defaultAddressPreviewDelayMs = 300;

/** The status of a problem whose `errors` extension lists why a promotion cannot be applied. */
const conflictStatus = 409;

/**
 * The message of a failed promotion request: the problem's detail, followed by each error a `409` problem lists in its
 * `errors` extension.
 */
function promotionErrorMessage(error: ApiError): string {
  const message = apiErrorMessage(error);
  const data = 'status' in error && error.status === conflictStatus ? error.data : undefined;
  const errors =
    data && typeof data === 'object' ? (data as { errors?: unknown }).errors : undefined;
  if (!Array.isArray(errors)) {
    return message;
  }
  const lines = errors.flatMap((item: unknown) => {
    if (!item || typeof item !== 'object') {
      return [];
    }
    const { resourceType, resourceId, detail } = item as {
      resourceType?: unknown;
      resourceId?: unknown;
      detail?: unknown;
    };
    if (typeof detail !== 'string') {
      return [];
    }
    const resource = [resourceType, resourceId]
      .filter((part): part is string => typeof part === 'string' && part.length > 0)
      .join(' ');
    return [resource.length > 0 ? `${resource}: ${detail}` : detail];
  });
  return lines.length > 0 ? `${message} ${lines.join(' ')}` : message;
}

function useDebouncedValue<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delayMs);
    return () => clearTimeout(timer);
  }, [value, delayMs]);
  return debounced;
}

/**
 * Container: a team's tenant browser and CRUD surface. Wires the RTK Query tenant list and mutations to the stateless
 * {@link TenantTable}, {@link TenantEnvironmentFilter}, {@link TenantFormDialog}, and {@link ConfirmDialog}. Owns only
 * dialog, filter, and error state; all persistence, cache invalidation, and error shaping come from the store layer.
 * Selecting a tenant crosses into that tenant's security boundary.
 */
export function TenantsPage({
  teamId,
  onOpenTenant,
  addressPreviewDelayMs = defaultAddressPreviewDelayMs,
}: TenantsPageProps) {
  const { data, isLoading, error } = useListTenantsQuery({ teamId });
  const tenants = data?.items ?? [];
  const unavailableCells = data?.unavailableCells ?? [];

  // The console forwards to the cells its bootstrap names; the platform's cell list supplies each
  // one's region.
  const runtime = useAppSelector((state) => state.config.runtime);
  const cellNames = runtime?.kind === 'bff' ? (runtime.cells ?? []) : [];
  const platformCells = useListCellsQuery(undefined, { skip: cellNames.length === 0 }).data ?? [];
  const regionOf = (cell: string) => platformCells.find((c) => c.name === cell)?.region ?? null;
  const cellOptions: TenantCellOption[] = cellNames.map((name) => ({
    name,
    region: regionOf(name),
  }));
  const controlPlaneCell = platformCells.find(
    (c) => c.isControlPlane && cellNames.includes(c.name),
  )?.name;

  const rows: TenantRow[] = tenants.map(({ cell, ...tenant }) => ({
    ...tenant,
    ...(cell !== undefined && { cell: { name: cell, region: regionOf(cell) } }),
  }));

  const [environmentFilter, setEnvironmentFilter] = useState(allTenantEnvironments);
  const environmentTypes = tenantEnvironmentTypes(rows);
  // A filter whose type no listed tenant has any longer (its last tenant was deleted) shows every
  // tenant.
  const activeEnvironment = environmentTypes.includes(environmentFilter)
    ? environmentFilter
    : allTenantEnvironments;
  const visibleTenants = rows.filter((tenant) =>
    matchesTenantEnvironment(tenant, activeEnvironment),
  );

  const [createTenant, { isLoading: isCreating }] = useCreateTenantMutation();
  const [updateTenant, { isLoading: isUpdating }] = useUpdateTenantMutation();
  const [deleteTenant, { isLoading: isDeleting }] = useDeleteTenantMutation();
  const [previewPromotion, { isLoading: isPreviewingPromotion }] =
    usePreviewTenantPromotionMutation();
  const [promoteTenant, { isLoading: isPromoting }] = usePromoteTenantMutation();
  const [getTenantConfiguration, { isFetching: isFetchingConfiguration }] =
    useLazyGetTenantConfigurationQuery();

  const [dialog, setDialog] = useState<DialogState>(closed);
  const [formError, setFormError] = useState<string>();
  const [deleteError, setDeleteError] = useState<string>();
  const [promotionReport, setPromotionReport] = useState<TenantPromotionResource>();
  const [promotionError, setPromotionError] = useState<string>();
  const [addressInput, setAddressInput] = useState<TenantAddressInput>();
  const handleAddressChange = useCallback((address: TenantAddressInput) => {
    setAddressInput(address);
  }, []);

  const openCreate = () => {
    setFormError(undefined);
    setDialog({ kind: 'create' });
  };
  const openPromote = () => {
    setPromotionReport(undefined);
    setPromotionError(undefined);
    setDialog({ kind: 'promote' });
  };
  const openEdit = (tenantId: string) => {
    const tenant = tenants.find((t) => t.tenantId === tenantId);
    if (!tenant) {
      return;
    }
    setFormError(undefined);
    setDialog({ kind: 'edit', tenant });
  };
  const openDelete = (tenantId: string) => {
    const tenant = tenants.find((t) => t.tenantId === tenantId);
    if (!tenant) {
      return;
    }
    setDeleteError(undefined);
    setDialog({ kind: 'delete', tenant });
  };

  const formOpen = dialog.kind === 'create' || dialog.kind === 'edit';
  const formInitial: TenantFormValues =
    dialog.kind === 'edit'
      ? {
          tenantId: dialog.tenant.tenantId,
          displayName: dialog.tenant.displayName,
          issuer: dialog.tenant.issuer ?? '',
          mtlsEndpointAliasAuthority: dialog.tenant.mtlsEndpointAliasAuthority ?? '',
          isDisabled: dialog.tenant.isDisabled,
        }
      : {
          ...emptyTenantFormValues,
          ...(controlPlaneCell !== undefined && { cell: controlPlaneCell }),
        };

  // The address is previewed against the server's own rules: for a new tenant through its team's
  // route, always, and for an existing tenant through its own route, only when its issuer or mTLS
  // endpoint alias changes, because the server judges an address only when it can change.
  const debouncedAddress = useDebouncedValue(addressInput, addressPreviewDelayMs);
  const isAddressUnchanged =
    dialog.kind === 'edit' &&
    debouncedAddress !== undefined &&
    debouncedAddress.issuer === (dialog.tenant.issuer ?? '') &&
    debouncedAddress.mtlsEndpointAliasAuthority ===
      (dialog.tenant.mtlsEndpointAliasAuthority ?? '');
  const shouldPreview =
    formOpen &&
    debouncedAddress !== undefined &&
    isValidTenantId(debouncedAddress.tenantId) &&
    !isAddressUnchanged;
  const previewArgsInput = {
    tenantId: debouncedAddress?.tenantId ?? '',
    ...(debouncedAddress?.issuer ? { issuer: debouncedAddress.issuer } : {}),
    ...(debouncedAddress?.mtlsEndpointAliasAuthority
      ? { mtlsEndpointAliasAuthority: debouncedAddress.mtlsEndpointAliasAuthority }
      : {}),
  };
  const newTenantPreview = useGetNewTenantAddressQuery(
    {
      teamId,
      ...previewArgsInput,
      cell: debouncedAddress?.cell ? debouncedAddress.cell : undefined,
    },
    { skip: dialog.kind !== 'create' || !shouldPreview, refetchOnMountOrArgChange: true },
  );
  const existingTenantPreview = useGetTenantAddressQuery(
    {
      ...previewArgsInput,
      cell: dialog.kind === 'edit' ? dialog.tenant.cell : undefined,
    },
    {
      skip: dialog.kind !== 'edit' || !shouldPreview,
      refetchOnMountOrArgChange: true,
    },
  );
  const { data: preview, originalArgs: previewArgs } =
    dialog.kind === 'edit' ? existingTenantPreview : newTenantPreview;

  // A preview is shown only while it describes the address as it is entered now.
  const addressPreview =
    shouldPreview &&
    preview &&
    addressInput &&
    previewArgs?.tenantId === addressInput.tenantId &&
    (previewArgs.issuer ?? '') === addressInput.issuer &&
    (previewArgs.mtlsEndpointAliasAuthority ?? '') === addressInput.mtlsEndpointAliasAuthority &&
    (dialog.kind === 'edit' || previewArgs.cell === addressInput.cell)
      ? preview
      : undefined;

  const handleSubmit = async (values: TenantFormValues) => {
    setFormError(undefined);
    const issuer = values.issuer.length > 0 ? values.issuer : null;
    const mtlsEndpointAliasAuthority =
      values.mtlsEndpointAliasAuthority.length > 0 ? values.mtlsEndpointAliasAuthority : null;
    try {
      if (dialog.kind === 'create') {
        await createTenant({
          teamId,
          cell: values.cell,
          body: {
            tenantId: values.tenantId,
            issuer,
            mtlsEndpointAliasAuthority,
            displayName: values.displayName,
            isDisabled: values.isDisabled,
            settings: {},
          },
        }).unwrap();
      } else if (dialog.kind === 'edit') {
        await updateTenant({
          teamId,
          tenantId: dialog.tenant.tenantId,
          cell: dialog.tenant.cell,
          concurrencyToken: dialog.tenant.concurrencyToken,
          fields: {
            issuer,
            mtlsEndpointAliasAuthority,
            displayName: values.displayName,
            isDisabled: values.isDisabled,
          },
        }).unwrap();
      }
      setDialog(closed);
    } catch (submitError) {
      setFormError(apiErrorMessage(submitError as ApiError));
    }
  };

  const handleDelete = async () => {
    if (dialog.kind !== 'delete') {
      return;
    }
    setDeleteError(undefined);
    try {
      await deleteTenant({
        teamId,
        tenantId: dialog.tenant.tenantId,
        cell: dialog.tenant.cell,
      }).unwrap();
      setDialog(closed);
    } catch (removeError) {
      setDeleteError(apiErrorMessage(removeError as ApiError));
    }
  };

  /**
   * The body of a promotion: within one cell the source is named, otherwise its configuration is exported from its own
   * cell and carried inline, because the target's cell cannot read a tenant of another cell.
   */
  const promotionBody = async (
    request: TenantPromotionDialogRequest,
  ): Promise<PromoteTenantRequest> => {
    const { sourceTenantId, sourceCell, targetCell, environmentSettings } = request;
    if (sourceCell === targetCell) {
      return { sourceTenantId, environmentSettings };
    }
    const source = await getTenantConfiguration({
      tenantId: sourceTenantId,
      cell: sourceCell,
    }).unwrap();
    return { source, environmentSettings };
  };

  const handlePreviewPromotion = async (request: TenantPromotionDialogRequest) => {
    setPromotionError(undefined);
    try {
      const body = await promotionBody(request);
      setPromotionReport(
        await previewPromotion({
          tenantId: request.targetTenantId,
          body,
          cell: request.targetCell,
        }).unwrap(),
      );
    } catch (previewError) {
      setPromotionError(promotionErrorMessage(previewError as ApiError));
    }
  };

  const handlePromote = async (request: TenantPromotionDialogRequest) => {
    setPromotionError(undefined);
    try {
      const body = await promotionBody(request);
      setPromotionReport(
        await promoteTenant({
          tenantId: request.targetTenantId,
          body,
          cell: request.targetCell,
        }).unwrap(),
      );
    } catch (promoteError) {
      setPromotionError(promotionErrorMessage(promoteError as ApiError));
    }
  };

  return (
    <>
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
        <Typography variant="h5" component="h2">
          Tenants
        </Typography>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 2 }}>
          <TenantEnvironmentFilter
            types={environmentTypes}
            value={activeEnvironment}
            onChange={setEnvironmentFilter}
          />
          <Button
            variant="outlined"
            onClick={openPromote}
            disabled={tenants.length < 2}
            data-testid="promote-tenant"
          >
            Promote
          </Button>
          <Button
            variant="contained"
            startIcon={<AddIcon />}
            onClick={openCreate}
            data-testid="create-tenant"
          >
            Create tenant
          </Button>
        </Box>
      </Box>
      {unavailableCells.length > 0 && (
        <Alert severity="warning" sx={{ mb: 1 }} data-testid="unavailable-cells">
          {unavailableCells.map((cell) => (
            <div
              key={cell}
            >{`The cell ${cell} did not answer, so its tenants are not listed.`}</div>
          ))}
        </Alert>
      )}
      <DataStateBoundary
        isLoading={isLoading}
        error={error ? 'Failed to load tenants.' : undefined}
        isEmpty={tenants.length === 0}
        emptyMessage="No tenants exist yet."
      >
        <TenantTable
          tenants={visibleTenants}
          showCell={cellNames.length > 0}
          onSelect={(tenantId) => {
            // The tenant opens in its own cell, which the list carries; a tenant without one opens
            // in the control plane.
            const cell = tenants.find((tenant) => tenant.tenantId === tenantId)?.cell;
            if (cell === undefined) {
              onOpenTenant(tenantId);
            } else {
              onOpenTenant(tenantId, cell);
            }
          }}
          onEdit={openEdit}
          onDelete={openDelete}
        />
      </DataStateBoundary>

      <TenantFormDialog
        open={formOpen}
        title={dialog.kind === 'edit' ? 'Edit tenant' : 'Create tenant'}
        submitLabel={dialog.kind === 'edit' ? 'Save' : 'Create'}
        initialValues={formInitial}
        isTenantIdLocked={dialog.kind === 'edit'}
        cells={cellOptions}
        isSubmitting={isCreating || isUpdating}
        error={formError}
        addressPreview={addressPreview}
        onAddressChange={handleAddressChange}
        onSubmit={handleSubmit}
        onCancel={() => setDialog(closed)}
      />

      <TenantPromotionDialog
        open={dialog.kind === 'promote'}
        tenants={tenants.map((tenant) => ({
          tenantId: tenant.tenantId,
          ...(tenant.cell !== undefined && { cell: tenant.cell, region: regionOf(tenant.cell) }),
        }))}
        report={promotionReport}
        isBusy={isPreviewingPromotion || isPromoting || isFetchingConfiguration}
        error={promotionError}
        onPreview={handlePreviewPromotion}
        onPromote={handlePromote}
        onCancel={() => setDialog(closed)}
      />

      <ConfirmDialog
        open={dialog.kind === 'delete'}
        title="Delete tenant"
        message={
          dialog.kind === 'delete'
            ? `Delete “${dialog.tenant.displayName}”? This cannot be undone.`
            : ''
        }
        isBusy={isDeleting}
        error={deleteError}
        onConfirm={handleDelete}
        onCancel={() => setDialog(closed)}
      />
    </>
  );
}
