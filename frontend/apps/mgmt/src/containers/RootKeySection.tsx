import { useState } from 'react';
import Alert from '@mui/material/Alert';
import type { EncryptionRootKeyResource } from '@ncode/api';
import { apiErrorMessage, type ApiError } from '@ncode/store';
import {
  ConfirmDialog,
  EncryptionRootKeyCard,
  EncryptionRootKeyFormDialog,
  emptyEncryptionRootKeyFormValues,
  type EncryptionRootKeyFormValues,
  type EncryptionRootKeyRow,
} from '@ncode/ui';

export type RootKeySectionProps = {
  /** The noun for the owner that brings the root key, such as `tenant` or `team`. */
  readonly ownerLabel: string;
  /**
   * The region the root key serves, or absent when it serves the owner as a whole; a notice and the remove dialog name
   * it.
   */
  readonly region?: string | undefined;
  /** The root key the owner brings, or `null` when the deployment's root key wraps its keys. */
  readonly rootKey: EncryptionRootKeyRow | null;
  readonly providers: readonly string[];
  readonly accessBinding: string | null;
  readonly isSetting: boolean;
  readonly isRemoving: boolean;
  readonly isValidating: boolean;
  /** Sets the root key, and rejects with the server's error. */
  readonly onSet: (values: EncryptionRootKeyFormValues) => Promise<unknown>;
  /**
   * Removes the root key and resolves the key still being removed, or `null` once none remains; rejects with the
   * server's error.
   */
  readonly onRemove: () => Promise<EncryptionRootKeyResource | null>;
  /** Validates the root key and resolves its state; rejects with the server's error. */
  readonly onValidate: () => Promise<EncryptionRootKeyResource>;
};

/**
 * The root key an owner brings, for a tenant or for one region of a team (ADR-1414): "Set root key" in a dialog that
 * shows the server's error, "Validate" and a confirmed "Remove". Validate and remove say what they did in a notice. The
 * API returns no key material, and neither does this view.
 */
export function RootKeySection({
  ownerLabel,
  region,
  rootKey,
  providers,
  accessBinding,
  isSetting,
  isRemoving,
  isValidating,
  onSet,
  onRemove,
  onValidate,
}: RootKeySectionProps) {
  const [setting, setSetting] = useState(false);
  const [setError, setSetError] = useState<string | undefined>(undefined);
  const [removing, setRemoving] = useState(false);
  const [removeError, setRemoveError] = useState<string | undefined>(undefined);
  const [validated, setValidated] = useState<EncryptionRootKeyResource | undefined>(undefined);
  const [validateError, setValidateError] = useState<string | undefined>(undefined);
  const [removal, setRemoval] = useState<
    { readonly rootKey: EncryptionRootKeyResource | null } | undefined
  >(undefined);

  const where = region === undefined ? '' : ` in ${region}`;

  const clearNotices = () => {
    setValidated(undefined);
    setValidateError(undefined);
    setRemoval(undefined);
  };

  const closeSet = () => {
    setSetting(false);
    setSetError(undefined);
  };

  const closeRemove = () => {
    setRemoving(false);
    setRemoveError(undefined);
  };

  const handleSet = async (values: EncryptionRootKeyFormValues) => {
    setSetError(undefined);
    try {
      await onSet(values);
      clearNotices();
      closeSet();
    } catch (failure) {
      setSetError(apiErrorMessage(failure as ApiError));
    }
  };

  const handleRemove = async () => {
    setRemoveError(undefined);
    try {
      const remaining = await onRemove();
      clearNotices();
      setRemoval({ rootKey: remaining });
      closeRemove();
    } catch (failure) {
      setRemoveError(apiErrorMessage(failure as ApiError));
    }
  };

  const handleValidate = async () => {
    clearNotices();
    try {
      setValidated(await onValidate());
    } catch (failure) {
      setValidateError(apiErrorMessage(failure as ApiError));
    }
  };

  return (
    <>
      {validateError && (
        <Alert severity="error" data-testid="root-key-validate-error">
          {validateError}
        </Alert>
      )}
      {validated && (
        <Alert
          severity={validated.state === 'active' ? 'success' : 'error'}
          data-testid="root-key-validate-result"
        >
          {validated.failure
            ? `The root key${where} is ${validated.state}: ${validated.failure}`
            : `The root key${where} is ${validated.state}.`}
        </Alert>
      )}
      {removal && (
        <Alert severity="info" data-testid="root-key-remove-result">
          {removal.rootKey === null
            ? `The keys${where} are back under the deployment's root key.`
            : `The keys${where} are still being rewrapped under the deployment's root key, and the root key is removed once none remains under it.`}
        </Alert>
      )}
      <EncryptionRootKeyCard
        ownerLabel={ownerLabel}
        rootKey={rootKey}
        canSet={providers.length > 0}
        isBusy={isSetting || isRemoving || isValidating}
        onSet={() => setSetting(true)}
        onValidate={() => void handleValidate()}
        onRemove={() => setRemoving(true)}
      />
      <EncryptionRootKeyFormDialog
        open={setting}
        ownerLabel={ownerLabel}
        initialValues={{
          ...emptyEncryptionRootKeyFormValues,
          provider: rootKey?.provider ?? emptyEncryptionRootKeyFormValues.provider,
          keyReference: rootKey?.keyReference ?? emptyEncryptionRootKeyFormValues.keyReference,
          accessIdentity:
            rootKey?.accessIdentity ?? emptyEncryptionRootKeyFormValues.accessIdentity,
        }}
        providers={providers}
        accessBinding={accessBinding}
        isSubmitting={isSetting}
        error={setError}
        onSubmit={(values) => void handleSet(values)}
        onCancel={closeSet}
      />
      <ConfirmDialog
        open={removing}
        title={`Remove root key${where}`}
        message="The keys are rewrapped under the deployment's root key. Any that cannot be unwrapped yet are retried in the background, and the root key is removed once none remains under it."
        confirmLabel="Remove"
        isBusy={isRemoving}
        error={removeError}
        onConfirm={() => void handleRemove()}
        onCancel={closeRemove}
      />
    </>
  );
}
