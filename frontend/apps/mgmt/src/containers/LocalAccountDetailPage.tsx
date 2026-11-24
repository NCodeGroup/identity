import { useState } from 'react';
import ArrowBackIcon from '@mui/icons-material/ArrowBack';
import Button from '@mui/material/Button';
import Stack from '@mui/material/Stack';
import Tab from '@mui/material/Tab';
import Tabs from '@mui/material/Tabs';
import Typography from '@mui/material/Typography';
import type {
  LocalAccountClaim,
  LocalAccountClaimsResource,
  LocalAccountMetadataResource,
  LocalAccountResource,
} from '@ncodegroup/identity-api';
import {
  apiErrorMessage,
  useGetLocalAccountClaimsQuery,
  useGetLocalAccountMetadataQuery,
  useSetLocalAccountClaimsMutation,
  useSetLocalAccountMetadataMutation,
  type ApiError,
} from '@ncodegroup/identity-store';
import {
  DataStateBoundary,
  LocalAccountClaimsEditor,
  LocalAccountMetadataEditor,
} from '@ncodegroup/identity-ui';

export type LocalAccountDetailPageProps = {
  readonly account: LocalAccountResource;
  readonly cell: string | undefined;
  readonly onBack: () => void;
};

type LocalAccountSurface = 'claims' | 'metadata';

/**
 * Container: drills into a single local account to edit its profile claims and, when it has an owning principal, its
 * metadata. The parent supplies the account and owns the drill-in selection. Both surfaces save with the concurrency
 * token of the document they loaded, so a concurrent change surfaces as a failed save. Owns only tab and error state.
 */
export function LocalAccountDetailPage({ account, cell, onBack }: LocalAccountDetailPageProps) {
  const [surface, setSurface] = useState<LocalAccountSurface>('claims');
  const [claimsError, setClaimsError] = useState<string | undefined>();
  const [metadataError, setMetadataError] = useState<string | undefined>();
  const hasPrincipal = account.principalId !== null;
  const address = {
    tenantId: account.tenantId,
    localAccountId: account.localAccountId,
    cell,
  };

  const {
    data: claims,
    isLoading: claimsLoading,
    error: claimsLoadError,
  } = useGetLocalAccountClaimsQuery(address);
  const {
    data: metadata,
    isLoading: metadataLoading,
    error: metadataLoadError,
  } = useGetLocalAccountMetadataQuery(address, { skip: !hasPrincipal });
  const [setLocalAccountClaims, { isLoading: isSavingClaims }] = useSetLocalAccountClaimsMutation();
  const [setLocalAccountMetadata, { isLoading: isSavingMetadata }] =
    useSetLocalAccountMetadataMutation();

  const saveClaims = async (loaded: LocalAccountClaimsResource, next: LocalAccountClaim[]) => {
    setClaimsError(undefined);
    try {
      await setLocalAccountClaims({
        ...address,
        concurrencyToken: loaded.concurrencyToken,
        body: { claims: next },
      }).unwrap();
    } catch (failure) {
      setClaimsError(apiErrorMessage(failure as ApiError));
    }
  };

  const saveMetadata = async (
    loaded: LocalAccountMetadataResource,
    profileMetadata: Record<string, unknown>,
    systemMetadata: Record<string, unknown>,
  ) => {
    setMetadataError(undefined);
    try {
      await setLocalAccountMetadata({
        ...address,
        concurrencyToken: loaded.concurrencyToken,
        body: { profileMetadata, systemMetadata },
      }).unwrap();
    } catch (failure) {
      setMetadataError(apiErrorMessage(failure as ApiError));
    }
  };

  const activeSurface: LocalAccountSurface = hasPrincipal ? surface : 'claims';

  return (
    <>
      <Button
        size="small"
        startIcon={<ArrowBackIcon />}
        onClick={onBack}
        data-testid="local-account-detail-back"
        sx={{ mb: 1 }}
      >
        Users
      </Button>
      <Stack sx={{ mb: 2 }}>
        <Typography variant="h5" component="h2">
          {account.displayName}
        </Typography>
        <Typography variant="body2" sx={{ color: 'text.secondary' }}>
          {account.email ? `${account.userName} · ${account.email}` : account.userName}
        </Typography>
        {hasPrincipal ? (
          <Typography
            variant="body2"
            sx={{ color: 'text.secondary' }}
            data-testid="local-account-principal"
          >
            Principal {account.principalId}
          </Typography>
        ) : null}
      </Stack>
      <Tabs
        value={activeSurface}
        onChange={(_event, value: LocalAccountSurface) => setSurface(value)}
        sx={{ mb: 2 }}
      >
        <Tab value="claims" label="Profile claims" data-testid="local-account-tab-claims" />
        {hasPrincipal ? (
          <Tab value="metadata" label="Metadata" data-testid="local-account-tab-metadata" />
        ) : null}
      </Tabs>
      {activeSurface === 'claims' ? (
        <DataStateBoundary
          isLoading={claimsLoading}
          error={claimsLoadError ? 'Failed to load profile claims.' : undefined}
        >
          {claims ? (
            <LocalAccountClaimsEditor
              claims={claims.claims}
              isSaving={isSavingClaims}
              error={claimsError}
              onSave={(next) => void saveClaims(claims, next)}
            />
          ) : null}
        </DataStateBoundary>
      ) : (
        <DataStateBoundary
          isLoading={metadataLoading}
          error={metadataLoadError ? 'Failed to load metadata.' : undefined}
        >
          {metadata ? (
            <LocalAccountMetadataEditor
              profileJson={JSON.stringify(metadata.profileMetadata ?? {}, null, 2)}
              systemJson={JSON.stringify(metadata.systemMetadata ?? {}, null, 2)}
              isSaving={isSavingMetadata}
              error={metadataError}
              onSave={(profile, system) => void saveMetadata(metadata, profile, system)}
            />
          ) : null}
        </DataStateBoundary>
      )}
    </>
  );
}
