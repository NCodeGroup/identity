import { useMemo, useState } from 'react';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import type { EncryptionRootKeyResource, TeamRootKeyResource } from '@ncode/api';
import {
  useListCellsQuery,
  useListTeamRootKeysQuery,
  useRemoveTeamRootKeyMutation,
  useSetTeamRootKeyMutation,
  useValidateTeamRootKeyMutation,
} from '@ncode/store';
import {
  ConfirmDialog,
  DataStateBoundary,
  EncryptionRootKeyCard,
  EncryptionRootKeyFormDialog,
  emptyEncryptionRootKeyFormValues,
  type EncryptionRootKeyFormValues,
} from '@ncode/ui';
import { rootKeyErrorMessage } from './OwnerEncryptionKeysTab.js';

export type TeamEncryptionKeysTabProps = {
  readonly teamId: string;
};

type TeamRegionRootKeySectionProps = {
  readonly teamId: string;
  readonly region: string;
  readonly rootKey: TeamRootKeyResource | undefined;
  readonly providers: readonly string[];
  readonly accessBinding: string | null;
};

/**
 * The root key a team brings for one region (ADR-1414): "Set root key" in a dialog that shows the server's error,
 * "Validate" and a confirmed "Remove". Validate and remove say what they did in a notice. The API returns no key
 * material, and neither does this view.
 */
function TeamRegionRootKeySection({
  teamId,
  region,
  rootKey,
  providers,
  accessBinding,
}: TeamRegionRootKeySectionProps) {
  const [setRootKey, { isLoading: isSetting }] = useSetTeamRootKeyMutation();
  const [removeRootKey, { isLoading: isRemoving }] = useRemoveTeamRootKeyMutation();
  const [validateRootKey, { isLoading: isValidating }] = useValidateTeamRootKeyMutation();
  const [setting, setSetting] = useState(false);
  const [setError, setSetError] = useState<string | undefined>(undefined);
  const [removing, setRemoving] = useState(false);
  const [removeError, setRemoveError] = useState<string | undefined>(undefined);
  const [validated, setValidated] = useState<EncryptionRootKeyResource | undefined>(undefined);
  const [validateError, setValidateError] = useState<string | undefined>(undefined);
  const [removal, setRemoval] = useState<
    { readonly rootKey: EncryptionRootKeyResource | null } | undefined
  >(undefined);

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
      await setRootKey({ teamId, region, body: values }).unwrap();
      clearNotices();
      closeSet();
    } catch (failure) {
      setSetError(rootKeyErrorMessage(failure));
    }
  };

  const handleRemove = async () => {
    setRemoveError(undefined);
    try {
      const remaining = await removeRootKey({ teamId, region }).unwrap();
      clearNotices();
      setRemoval({ rootKey: remaining });
      closeRemove();
    } catch (failure) {
      setRemoveError(rootKeyErrorMessage(failure));
    }
  };

  const handleValidate = async () => {
    clearNotices();
    try {
      setValidated(await validateRootKey({ teamId, region }).unwrap());
    } catch (failure) {
      setValidateError(rootKeyErrorMessage(failure));
    }
  };

  return (
    <Stack spacing={1.5} data-testid={`team-root-key-region-${region}`}>
      <Typography variant="h6" component="h4" data-testid="team-root-key-region">
        {region}
      </Typography>
      {validateError && (
        <Alert severity="error" data-testid="root-key-validate-error">
          {validateError}
        </Alert>
      )}
      {validated && (
        <Alert
          severity={validated.state === 'active' ? 'success' : 'error'}
          data-testid="team-root-key-validate-result"
        >
          {validated.failure
            ? `The root key in ${region} is ${validated.state}: ${validated.failure}`
            : `The root key in ${region} is ${validated.state}.`}
        </Alert>
      )}
      {removal && (
        <Alert severity="info" data-testid="team-root-key-remove-result">
          {removal.rootKey === null
            ? `The keys in ${region} are back under the deployment's root key.`
            : `The keys in ${region} are still being rewrapped under the deployment's root key, and the root key is removed once none remains under it.`}
        </Alert>
      )}
      <EncryptionRootKeyCard
        ownerLabel="team"
        rootKey={rootKey ?? null}
        canSet={providers.length > 0}
        isBusy={isSetting || isRemoving || isValidating}
        onSet={() => setSetting(true)}
        onValidate={() => void handleValidate()}
        onRemove={() => setRemoving(true)}
      />
      <EncryptionRootKeyFormDialog
        open={setting}
        ownerLabel="team"
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
        title={`Remove root key in ${region}`}
        message="The keys are rewrapped under the deployment's root key. Any that cannot be unwrapped yet are retried in the background, and the root key is removed once none remains under it."
        confirmLabel="Remove"
        isBusy={isRemoving}
        error={removeError}
        onConfirm={() => void handleRemove()}
        onCancel={closeRemove}
      />
    </Stack>
  );
}

/**
 * Container: the root keys of a team per region (ADR-1414). One row per distinct region of the platform's cells, sorted
 * by region, each showing that region's key, or saying that the deployment's root key wraps the team's keys there. A
 * region's key is set, validated and removed for that region only. The API returns no key material, and neither does
 * this view.
 */
export function TeamEncryptionKeysTab({ teamId }: TeamEncryptionKeysTabProps) {
  const cells = useListCellsQuery();
  const rootKeys = useListTeamRootKeysQuery({ teamId });

  const regions = useMemo(
    () =>
      [...new Set((cells.data ?? []).map((cell) => cell.region))].sort((a, b) =>
        a < b ? -1 : a > b ? 1 : 0,
      ),
    [cells.data],
  );

  const failed = cells.error || rootKeys.error;

  return (
    <Stack spacing={2}>
      <Typography variant="h6" component="h3">
        Encryption keys
      </Typography>
      <DataStateBoundary
        isLoading={cells.isLoading || rootKeys.isLoading}
        error={failed ? 'Failed to load the encryption keys of this team.' : undefined}
        isEmpty={regions.length === 0}
        emptyMessage="The platform has no regions, so this team has none to set a root key in."
      >
        {regions.map((region) => (
          <TeamRegionRootKeySection
            key={region}
            teamId={teamId}
            region={region}
            rootKey={rootKeys.data?.rootKeys.find((key) => key.region === region)}
            providers={rootKeys.data?.rootKeyProviders ?? []}
            accessBinding={rootKeys.data?.rootKeyAccessBinding ?? null}
          />
        ))}
      </DataStateBoundary>
    </Stack>
  );
}
