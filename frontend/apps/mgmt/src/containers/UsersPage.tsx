import AddIcon from '@mui/icons-material/Add';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Typography from '@mui/material/Typography';
import {
  ConfirmDialog,
  DataStateBoundary,
  LocalAccountFormDialog,
  LocalAccountPasswordDialog,
  LocalAccountTable,
  emptyLocalAccountFormValues,
  type LocalAccountFormValues,
} from '@ncodegroup/identity-ui';
import { useListLocalAccountsQuery, type LocalAccountOwner } from '@ncodegroup/identity-store';
import { useTenantCell } from '../tenantCell.js';
import { useLocalAccountAdministration } from './useLocalAccountAdministration.js';

export type UsersPageProps = {
  /** Whose local accounts the page administers: the platform, or a tenant (in the cell of the route). */
  readonly owner:
    { readonly kind: 'platform' } | { readonly kind: 'tenant'; readonly tenantId: string };
};

/**
 * Container: browses and administers the local accounts of the platform or of a tenant. A tenant owner is
 * addressed in the cell of the tenant route. Wires the RTK Query account list to the stateless
 * {@link LocalAccountTable}, {@link LocalAccountFormDialog}, {@link LocalAccountPasswordDialog} and
 * {@link ConfirmDialog}; the dialog and error state live in {@link useLocalAccountAdministration}.
 */
export function UsersPage({ owner }: UsersPageProps) {
  const cell = useTenantCell();
  const storeOwner: LocalAccountOwner =
    owner.kind === 'tenant' ? { kind: 'tenant', tenantId: owner.tenantId, cell } : owner;
  const { data, isLoading, error } = useListLocalAccountsQuery(storeOwner);
  const accounts = data?.items ?? [];
  const admin = useLocalAccountAdministration({ owner: storeOwner, accounts });
  const { dialog } = admin;

  const isEditing = dialog.kind === 'edit';
  const formInitial: LocalAccountFormValues =
    dialog.kind === 'edit'
      ? {
          displayName: dialog.account.displayName,
          userName: dialog.account.userName,
          email: dialog.account.email ?? '',
          emailVerified: dialog.account.emailVerified,
          password: '',
          confirmPassword: '',
        }
      : emptyLocalAccountFormValues;

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
        Local accounts of {owner.kind === 'platform' ? 'the platform' : 'this tenant'}. The display
        name is the name people and applications see.
      </Typography>
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
          onEdit={admin.openEdit}
          onResetPassword={admin.openPassword}
          onToggleEnabled={(id) => void admin.toggleEnabled(id)}
          onDelete={admin.openDelete}
        />
      </DataStateBoundary>

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
