import { useMemo, useState } from 'react';
import AddIcon from '@mui/icons-material/Add';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Typography from '@mui/material/Typography';
import {
  ConfirmDialog,
  ConnectionFormDialog,
  ConnectionTable,
  DataStateBoundary,
  LoadMoreButton,
  emptyConnectionFormValues,
  type ConnectionFormValues,
} from '@ncodegroup/identity-ui';
import type { ConnectionResource } from '@ncodegroup/identity-api';
import {
  apiErrorMessage,
  pagedItems,
  useCreateConnectionMutation,
  useDeleteConnectionMutation,
  useListConnectionsInfiniteQuery,
  useUpdateConnectionMutation,
  type ApiError,
} from '@ncodegroup/identity-store';
import { useTenantCell } from '../tenantCell.js';
import { ConnectionDetailPage, connectionStore } from './ConnectionDetailPage.js';

export type ConnectionsPageProps = {
  readonly tenantId: string;
};

type DialogState =
  | { readonly kind: 'none' }
  | { readonly kind: 'create' }
  | { readonly kind: 'edit'; readonly connection: ConnectionResource }
  | { readonly kind: 'delete'; readonly connection: ConnectionResource };

const closed: DialogState = { kind: 'none' };

/**
 * Container: browses and administers the connections of a tenant. Wires the RTK Query connection list and mutations to
 * the stateless {@link ConnectionTable}, {@link ConnectionFormDialog} and {@link ConfirmDialog}; owns only dialog,
 * error and drill-in state. A delete the server refuses (a connection that still has dependents answers 409) keeps the
 * confirm dialog open and shows the server's detail.
 */
export function ConnectionsPage({ tenantId }: ConnectionsPageProps) {
  const cell = useTenantCell();
  const { data, isLoading, error, hasNextPage, isFetchingNextPage, fetchNextPage } =
    useListConnectionsInfiniteQuery({ tenantId, cell });
  const connections = useMemo(() => pagedItems(data), [data]);

  const [createConnection, { isLoading: isCreating }] = useCreateConnectionMutation();
  const [updateConnection, { isLoading: isUpdating }] = useUpdateConnectionMutation();
  const [deleteConnection, { isLoading: isDeleting }] = useDeleteConnectionMutation();

  const [selectedId, setSelectedId] = useState<string>();
  const [dialog, setDialog] = useState<DialogState>(closed);
  const [formError, setFormError] = useState<string>();
  const [deleteError, setDeleteError] = useState<string>();

  const selected = connections.find((c) => c.connectionId === selectedId);
  if (selected) {
    return (
      <ConnectionDetailPage
        tenantId={tenantId}
        connection={selected}
        onBack={() => setSelectedId(undefined)}
      />
    );
  }

  const openCreate = () => {
    setFormError(undefined);
    setDialog({ kind: 'create' });
  };
  const openEdit = (connectionId: string) => {
    const connection = connections.find((c) => c.connectionId === connectionId);
    if (!connection) {
      return;
    }
    setFormError(undefined);
    setDialog({ kind: 'edit', connection });
  };
  const openDelete = (connectionId: string) => {
    const connection = connections.find((c) => c.connectionId === connectionId);
    if (!connection) {
      return;
    }
    setDeleteError(undefined);
    setDialog({ kind: 'delete', connection });
  };

  const formOpen = dialog.kind === 'create' || dialog.kind === 'edit';
  const formInitial: ConnectionFormValues =
    dialog.kind === 'edit'
      ? {
          name: dialog.connection.name,
          displayName: dialog.connection.displayName,
          strategy: dialog.connection.strategy,
          store: connectionStore(dialog.connection),
          isEnabled: dialog.connection.isEnabled,
        }
      : emptyConnectionFormValues;

  const handleSubmit = async (values: ConnectionFormValues) => {
    setFormError(undefined);
    try {
      if (dialog.kind === 'create') {
        await createConnection({
          tenantId,
          body: {
            name: values.name,
            displayName: values.displayName,
            strategy: values.strategy,
            isEnabled: values.isEnabled,
            options: { store: values.store },
          },
          cell,
        }).unwrap();
      } else if (dialog.kind === 'edit') {
        await updateConnection({
          tenantId,
          connectionId: dialog.connection.connectionId,
          concurrencyToken: dialog.connection.concurrencyToken,
          fields: {
            displayName: values.displayName,
            isEnabled: values.isEnabled,
            options: dialog.connection.options,
          },
          cell,
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
      await deleteConnection({
        tenantId,
        connectionId: dialog.connection.connectionId,
        cell,
      }).unwrap();
      setDialog(closed);
    } catch (removeError) {
      setDeleteError(apiErrorMessage(removeError as ApiError));
    }
  };

  return (
    <>
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
        <Typography variant="h5" component="h2">
          Connections
        </Typography>
        <Button
          variant="contained"
          startIcon={<AddIcon />}
          onClick={openCreate}
          data-testid="create-connection"
        >
          Create connection
        </Button>
      </Box>
      <DataStateBoundary
        isLoading={isLoading}
        error={error ? 'Failed to load connections.' : undefined}
        isEmpty={connections.length === 0}
        emptyMessage="No connections in this tenant yet."
      >
        <ConnectionTable
          connections={connections.map((c) => ({
            connectionId: c.connectionId,
            name: c.name,
            displayName: c.displayName,
            strategy: c.strategy,
            isSystem: c.isSystem,
            isEnabled: c.isEnabled,
          }))}
          onSelect={setSelectedId}
          onEdit={openEdit}
          onDelete={openDelete}
        />
      </DataStateBoundary>
      <LoadMoreButton
        hasMore={hasNextPage}
        isLoading={isFetchingNextPage}
        onLoadMore={() => void fetchNextPage()}
      />

      <ConnectionFormDialog
        open={formOpen}
        title={dialog.kind === 'edit' ? 'Edit connection' : 'Create connection'}
        submitLabel={dialog.kind === 'edit' ? 'Save' : 'Create'}
        nameEditable={dialog.kind !== 'edit'}
        initialValues={formInitial}
        isSubmitting={isCreating || isUpdating}
        error={formError}
        onSubmit={handleSubmit}
        onCancel={() => setDialog(closed)}
      />

      <ConfirmDialog
        open={dialog.kind === 'delete'}
        title="Delete connection"
        message={
          dialog.kind === 'delete'
            ? `Delete “${dialog.connection.name}”? This cannot be undone.`
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
