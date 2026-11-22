import { useState } from 'react';
import type { LocalAccountFormValues } from '@ncodegroup/identity-ui';
import type { LocalAccountResource } from '@ncodegroup/identity-api';
import {
  apiErrorMessage,
  useCreateLocalAccountMutation,
  useDeleteLocalAccountMutation,
  useDisableLocalAccountMutation,
  useEnableLocalAccountMutation,
  useResetLocalAccountPasswordMutation,
  useUpdateLocalAccountMutation,
  type ApiError,
  type LocalAccountOwner,
} from '@ncodegroup/identity-store';

type DialogState =
  | { readonly kind: 'none' }
  | { readonly kind: 'create' }
  | { readonly kind: 'edit'; readonly account: LocalAccountResource }
  | { readonly kind: 'password'; readonly account: LocalAccountResource }
  | { readonly kind: 'delete'; readonly account: LocalAccountResource };

const closed: DialogState = { kind: 'none' };

export type UseLocalAccountAdministrationOptions = {
  /** The owner whose accounts the page lists: the platform, or a tenant in its cell. */
  readonly owner: LocalAccountOwner;
  /** The accounts the page lists; a row's id resolves to one of these. */
  readonly accounts: readonly LocalAccountResource[];
};

/**
 * Hook: the local-account administration both account pages share — the create/edit form dialog, the password-reset and
 * delete dialogs, the enable/disable toggle, and their error state. An account's own routes address it through its
 * `tenantId`, in the cell the owner names; the platform owner has none.
 */
export function useLocalAccountAdministration({
  owner,
  accounts,
}: UseLocalAccountAdministrationOptions) {
  const cell = owner.kind === 'tenant' ? owner.cell : undefined;
  const [createLocalAccount] = useCreateLocalAccountMutation();
  const [updateLocalAccount] = useUpdateLocalAccountMutation();
  const [deleteLocalAccount, { isLoading: isDeleting }] = useDeleteLocalAccountMutation();
  const [enableLocalAccount] = useEnableLocalAccountMutation();
  const [disableLocalAccount] = useDisableLocalAccountMutation();
  const [resetPassword, { isLoading: isResettingPassword }] =
    useResetLocalAccountPasswordMutation();

  const [dialog, setDialog] = useState<DialogState>(closed);
  const [formError, setFormError] = useState<string>();
  const [passwordError, setPasswordError] = useState<string>();
  const [deleteError, setDeleteError] = useState<string>();
  const [toggleError, setToggleError] = useState<string>();
  const [isSubmitting, setIsSubmitting] = useState(false);

  const openCreate = () => {
    setFormError(undefined);
    setDialog({ kind: 'create' });
  };
  const openEdit = (localAccountId: string) => {
    const account = accounts.find((a) => a.localAccountId === localAccountId);
    if (!account) {
      return;
    }
    setFormError(undefined);
    setDialog({ kind: 'edit', account });
  };
  const openPassword = (localAccountId: string) => {
    const account = accounts.find((a) => a.localAccountId === localAccountId);
    if (!account) {
      return;
    }
    setPasswordError(undefined);
    setDialog({ kind: 'password', account });
  };
  const openDelete = (localAccountId: string) => {
    const account = accounts.find((a) => a.localAccountId === localAccountId);
    if (!account) {
      return;
    }
    setDeleteError(undefined);
    setDialog({ kind: 'delete', account });
  };
  const close = () => setDialog(closed);

  const handleSubmit = async (values: LocalAccountFormValues) => {
    setFormError(undefined);
    setIsSubmitting(true);
    try {
      if (dialog.kind === 'create') {
        await createLocalAccount({
          owner,
          body: {
            displayName: values.displayName,
            userName: values.userName,
            password: values.password,
            emailVerified: values.emailVerified,
            ...(values.email === '' ? {} : { email: values.email }),
          },
        }).unwrap();
      } else if (dialog.kind === 'edit') {
        await updateLocalAccount({
          tenantId: dialog.account.tenantId,
          localAccountId: dialog.account.localAccountId,
          concurrencyToken: dialog.account.concurrencyToken,
          fields: {
            displayName: values.displayName,
            userName: values.userName,
            email: values.email === '' ? null : values.email,
            emailVerified: values.emailVerified,
          },
          cell,
        }).unwrap();
      }
      setDialog(closed);
    } catch (submitError) {
      setFormError(apiErrorMessage(submitError as ApiError));
    } finally {
      setIsSubmitting(false);
    }
  };

  const handlePassword = async (password: string) => {
    if (dialog.kind !== 'password') {
      return;
    }
    setPasswordError(undefined);
    try {
      await resetPassword({
        tenantId: dialog.account.tenantId,
        localAccountId: dialog.account.localAccountId,
        body: { password },
        cell,
      }).unwrap();
      setDialog(closed);
    } catch (resetError) {
      setPasswordError(apiErrorMessage(resetError as ApiError));
    }
  };

  const handleDelete = async () => {
    if (dialog.kind !== 'delete') {
      return;
    }
    setDeleteError(undefined);
    try {
      await deleteLocalAccount({
        tenantId: dialog.account.tenantId,
        localAccountId: dialog.account.localAccountId,
        cell,
      }).unwrap();
      setDialog(closed);
    } catch (removeError) {
      setDeleteError(apiErrorMessage(removeError as ApiError));
    }
  };

  const toggleEnabled = async (localAccountId: string) => {
    const account = accounts.find((a) => a.localAccountId === localAccountId);
    if (!account) {
      return;
    }
    setToggleError(undefined);
    try {
      const toggle = account.isEnabled ? disableLocalAccount : enableLocalAccount;
      await toggle({ tenantId: account.tenantId, localAccountId, cell }).unwrap();
    } catch (toggleFailure) {
      setToggleError(apiErrorMessage(toggleFailure as ApiError));
    }
  };

  return {
    openCreate,
    openEdit,
    openPassword,
    openDelete,
    toggleEnabled,
    close,
    dialog,
    formError,
    passwordError,
    deleteError,
    toggleError,
    handleSubmit,
    handlePassword,
    handleDelete,
    isSubmitting,
    isResettingPassword,
    isDeleting,
  };
}
