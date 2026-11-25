import { useCallback, useState } from 'react';
import AddIcon from '@mui/icons-material/Add';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import {
  TenantFormDialog,
  emptyTenantFormValues,
  isValidTenantId,
  type TenantAddressInput,
  type TenantFormValues,
} from '@ncodegroup/identity-ui';
import {
  apiErrorMessage,
  useCreateTenantMutation,
  useGetNewTenantAddressQuery,
  useListCellTeamsQuery,
  type ApiError,
} from '@ncodegroup/identity-store';
import {
  isTenantAddressPreviewCurrent,
  tenantAddressPreviewArgs,
} from '../tenantAddressPreview.js';
import { useDebouncedValue } from '../useDebouncedValue.js';

export type CellTenantCreationProps = {
  /** Invoked with the identifier of the tenant once it is created. */
  readonly onCreated?: (tenantId: string) => void;
  /**
   * How long the address must stay unchanged before it is previewed. Defaults to 300 ms so typing sends no burst of
   * requests; a test passes `0`.
   */
  readonly addressPreviewDelayMs?: number;
};

const defaultAddressPreviewDelayMs = 300;

/**
 * Container: creates a tenant in the cell that serves the console, for a team that grants the cell the creation of its
 * tenants through its `tenant_creation_cells` setting (ADR-1536). Lists those teams from `GET /api/cell/teams`, lets
 * the caller choose one, and creates the tenant through that team's route of the cell's own management API, so it needs
 * neither the control-plane cell nor the platform console. Renders nothing while no team grants the cell.
 */
export function CellTenantCreation({
  onCreated,
  addressPreviewDelayMs = defaultAddressPreviewDelayMs,
}: CellTenantCreationProps) {
  const teamsQuery = useListCellTeamsQuery();
  const teams = teamsQuery.data?.items ?? [];
  const [selectedTeamId, setSelectedTeamId] = useState<string>();
  const teamId = selectedTeamId ?? teams[0]?.teamId;

  const [createTenant, { isLoading: isCreating }] = useCreateTenantMutation();
  const [isOpen, setIsOpen] = useState(false);
  const [formError, setFormError] = useState<string>();
  const [addressInput, setAddressInput] = useState<TenantAddressInput>();
  const handleAddressChange = useCallback((address: TenantAddressInput) => {
    setAddressInput(address);
  }, []);

  // The address is previewed against the server's own rules, through the chosen team's route.
  const debouncedAddress = useDebouncedValue(addressInput, addressPreviewDelayMs);
  const shouldPreview =
    isOpen &&
    teamId !== undefined &&
    debouncedAddress !== undefined &&
    isValidTenantId(debouncedAddress.tenantId);
  const previewQuery = useGetNewTenantAddressQuery(
    {
      teamId: teamId ?? '',
      ...tenantAddressPreviewArgs(debouncedAddress),
      cell: undefined,
    },
    { skip: !shouldPreview, refetchOnMountOrArgChange: true },
  );
  const { data: preview, originalArgs: previewArgs } = previewQuery;
  // A preview is shown only while it describes the address as it is entered now.
  const addressPreview =
    shouldPreview &&
    preview &&
    previewArgs?.teamId === teamId &&
    isTenantAddressPreviewCurrent(previewArgs, addressInput)
      ? preview
      : undefined;

  if (teams.length === 0 || teamId === undefined) {
    return null;
  }

  const open = () => {
    setFormError(undefined);
    setIsOpen(true);
  };

  const handleSubmit = async (values: TenantFormValues) => {
    setFormError(undefined);
    try {
      const created = await createTenant({
        teamId,
        cell: undefined,
        body: {
          tenantId: values.tenantId,
          issuer: values.issuer.length > 0 ? values.issuer : null,
          mtlsEndpointAliasAuthority:
            values.mtlsEndpointAliasAuthority.length > 0 ? values.mtlsEndpointAliasAuthority : null,
          displayName: values.displayName,
          isDisabled: values.isDisabled,
          settings: {},
        },
      }).unwrap();
      setIsOpen(false);
      onCreated?.(created.tenantId);
    } catch (submitError) {
      setFormError(apiErrorMessage(submitError as ApiError));
    }
  };

  return (
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 2 }}>
      <TextField
        select
        size="small"
        label="Team"
        value={teamId}
        onChange={(event) => setSelectedTeamId(event.target.value)}
        sx={{ minWidth: 180 }}
        slotProps={{ htmlInput: { 'data-testid': 'cell-tenant-team' } }}
      >
        {teams.map((team) => (
          <MenuItem key={team.teamId} value={team.teamId}>
            {team.displayName}
          </MenuItem>
        ))}
      </TextField>
      <Button
        variant="contained"
        startIcon={<AddIcon />}
        onClick={open}
        data-testid="cell-create-tenant"
      >
        Create tenant
      </Button>
      <TenantFormDialog
        open={isOpen}
        title="Create tenant"
        submitLabel="Create"
        initialValues={emptyTenantFormValues}
        isSubmitting={isCreating}
        error={formError}
        addressPreview={addressPreview}
        onAddressChange={handleAddressChange}
        onSubmit={handleSubmit}
        onCancel={() => setIsOpen(false)}
      />
    </Box>
  );
}
