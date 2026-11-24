import { useMemo, useState, type ReactNode } from 'react';
import AddIcon from '@mui/icons-material/Add';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import {
  ConfirmDialog,
  DataStateBoundary,
  LocalAccountFormDialog,
  LocalAccountPasswordDialog,
  LocalAccountTable,
  LoadMoreButton,
  emptyLocalAccountFormValues,
  type LocalAccountFormValues,
} from '@ncodegroup/identity-ui';
import {
  pagedItems,
  useListConnectionsQuery,
  useListLocalAccountsInfiniteQuery,
  type LocalAccountOwner,
} from '@ncodegroup/identity-store';
import { useTenantCell } from '../tenantCell.js';
import { LocalAccountDetailPage } from './LocalAccountDetailPage.js';
import { useLocalAccountAdministration } from './useLocalAccountAdministration.js';

export type UsersPageProps = {
  /** Whose local accounts the page administers: the platform, or a tenant (in the cell of the route). */
  readonly owner:
    { readonly kind: 'platform' } | { readonly kind: 'tenant'; readonly tenantId: string };
};

/** The strategy of the connections whose accounts this page administers. */
const localStrategy = 'local';

/**
 * Container: browses and administers the local accounts of the platform or of a tenant. A tenant owner picks one of the
 * tenant's `local` connections (the one named `local` is preselected), because a local account belongs to one
 * connection; the platform owner addresses the platform tenant's default connection. A tenant owner is addressed in the
 * cell of the tenant route.
 */
export function UsersPage({ owner }: UsersPageProps) {
  if (owner.kind === 'platform') {
    return <UsersList owner={owner} heading="the platform" />;
  }
  return <TenantUsers tenantId={owner.tenantId} />;
}

function TenantUsers({ tenantId }: { readonly tenantId: string }) {
  const cell = useTenantCell();
  const { data, isLoading, error } = useListConnectionsQuery({ tenantId, cell });
  const [selectedId, setSelectedId] = useState<string>();
  const connections = (data?.items ?? []).filter((c) => c.strategy === localStrategy);
  const selected =
    connections.find((c) => c.connectionId === selectedId) ??
    connections.find((c) => c.name === 'local') ??
    connections[0];

  if (isLoading || error || !selected) {
    return (
      <>
        <Typography variant="h5" component="h2" sx={{ mb: 1 }}>
          Users
        </Typography>
        <DataStateBoundary
          isLoading={isLoading}
          error={error ? 'Failed to load connections.' : undefined}
          isEmpty
          emptyMessage="No local connections yet."
        >
          {null}
        </DataStateBoundary>
      </>
    );
  }

  return (
    <UsersList
      owner={{ kind: 'tenant', tenantId, connectionId: selected.connectionId, cell }}
      heading="this tenant"
      connectionPicker={
        <TextField
          select
          label="Connection"
          size="small"
          value={selected.connectionId}
          onChange={(event) => setSelectedId(event.target.value)}
          sx={{ mb: 2, minWidth: 240 }}
          slotProps={{
            select: { native: true },
            htmlInput: { 'data-testid': 'users-connection' },
          }}
        >
          {connections.map((c) => (
            <option key={c.connectionId} value={c.connectionId}>
              {c.displayName}
            </option>
          ))}
        </TextField>
      }
    />
  );
}

type UsersListProps = {
  readonly owner: LocalAccountOwner;
  /** What the description names as the accounts' owner. */
  readonly heading: string;
  readonly connectionPicker?: ReactNode;
};

function UsersList({ owner: storeOwner, heading, connectionPicker }: UsersListProps) {
  const { data, isLoading, error, hasNextPage, isFetchingNextPage, fetchNextPage } =
    useListLocalAccountsInfiniteQuery(storeOwner);
  const accounts = useMemo(() => pagedItems(data), [data]);
  const admin = useLocalAccountAdministration({ owner: storeOwner, accounts });
  const { dialog } = admin;
  const [selectedId, setSelectedId] = useState<string | undefined>();
  const selected = accounts.find((account) => account.localAccountId === selectedId);

  const isEditing = dialog.kind === 'edit';
  const formInitial: LocalAccountFormValues =
    dialog.kind === 'edit'
      ? {
          displayName: dialog.account.displayName,
          userName: dialog.account.userName ?? '',
          email: dialog.account.email ?? '',
          emailVerified: dialog.account.emailVerified,
          password: '',
          confirmPassword: '',
        }
      : emptyLocalAccountFormValues;

  if (selected) {
    return (
      <LocalAccountDetailPage
        account={selected}
        cell={storeOwner.kind === 'tenant' ? storeOwner.cell : undefined}
        onBack={() => setSelectedId(undefined)}
      />
    );
  }

  return (
    <>
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
        <Typography variant="h5" component="h2">
          Users
        </Typography>
        <Button
          variant="contained"
          startIcon={<AddIcon />}
          onClick={admin.openCreate}
          data-testid="create-local-account"
        >
          Create user
        </Button>
      </Box>
      <Typography variant="body2" sx={{ mb: 2 }}>
        Local accounts of {heading}. The display name is the name people and applications see.
      </Typography>
      {connectionPicker}
      {admin.toggleError && (
        <Alert severity="error" sx={{ mb: 2 }} data-testid="local-account-toggle-error">
          {admin.toggleError}
        </Alert>
      )}
      <DataStateBoundary
        isLoading={isLoading}
        error={error ? 'Failed to load users.' : undefined}
        isEmpty={accounts.length === 0}
        emptyMessage="No users yet."
      >
        <LocalAccountTable
          accounts={accounts}
          onSelect={setSelectedId}
          onEdit={admin.openEdit}
          onResetPassword={admin.openPassword}
          onToggleEnabled={(id) => void admin.toggleEnabled(id)}
          onDelete={admin.openDelete}
        />
      </DataStateBoundary>
      <LoadMoreButton
        hasMore={hasNextPage}
        isLoading={isFetchingNextPage}
        onLoadMore={() => void fetchNextPage()}
      />

      <LocalAccountFormDialog
        open={dialog.kind === 'create' || dialog.kind === 'edit'}
        mode={isEditing ? 'edit' : 'create'}
        title={isEditing ? 'Edit user' : 'Create user'}
        submitLabel={isEditing ? 'Save' : 'Create'}
        initialValues={formInitial}
        isSubmitting={admin.isSubmitting}
        error={admin.formError}
        onSubmit={(values) => void admin.handleSubmit(values)}
        onCancel={admin.close}
      />

      <LocalAccountPasswordDialog
        open={dialog.kind === 'password'}
        accountLabel={dialog.kind === 'password' ? dialog.account.displayName : ''}
        isSubmitting={admin.isResettingPassword}
        error={admin.passwordError}
        onSubmit={(password) => void admin.handlePassword(password)}
        onCancel={admin.close}
      />

      <ConfirmDialog
        open={dialog.kind === 'delete'}
        title="Delete user"
        message={
          dialog.kind === 'delete'
            ? `Delete ${dialog.account.displayName}? This ends their sessions.`
            : ''
        }
        isBusy={admin.isDeleting}
        error={admin.deleteError}
        onConfirm={() => void admin.handleDelete()}
        onCancel={admin.close}
      />
    </>
  );
}
