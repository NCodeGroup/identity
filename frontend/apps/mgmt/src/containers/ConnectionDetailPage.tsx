import { useState } from 'react';
import ArrowBackIcon from '@mui/icons-material/ArrowBack';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import FormControlLabel from '@mui/material/FormControlLabel';
import Stack from '@mui/material/Stack';
import Switch from '@mui/material/Switch';
import Tab from '@mui/material/Tab';
import Tabs from '@mui/material/Tabs';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import type { ConnectionResource } from '@ncodegroup/identity-api';
import {
  apiErrorMessage,
  useGetConnectionEffectiveSettingsQuery,
  useGetConnectionSettingLimitsQuery,
  useGetConnectionSettingsQuery,
  useUpdateConnectionMutation,
  useUpdateConnectionSettingsMutation,
  type ApiError,
} from '@ncodegroup/identity-store';
import { useTenantCell } from '../tenantCell.js';
import { SettingsManager } from './SettingsManager.js';

export type ConnectionDetailPageProps = {
  readonly tenantId: string;
  readonly connection: ConnectionResource;
  readonly onBack: () => void;
};

type ConnectionSurface = 'general' | 'settings';

/** The named local-account store a connection's options carry, or the default when they name none. */
export function connectionStore(connection: ConnectionResource): string {
  const store = connection.options['store'];
  return typeof store === 'string' && store.length > 0 ? store : 'default';
}

/**
 * Container: drills into one connection to edit its display name and enabled flag (General, which shows a local
 * connection's store read-only) and its `connection`-scope settings (Settings). The parent
 * {@link import('./ConnectionsPage.js').ConnectionsPage} supplies the connection and owns the drill-in selection.
 * Settings reuse the shared {@link SettingsManager} over the connection's own settings document, effective settings and
 * limits. Owns only tab and form state.
 */
export function ConnectionDetailPage({ tenantId, connection, onBack }: ConnectionDetailPageProps) {
  const [surface, setSurface] = useState<ConnectionSurface>('general');

  return (
    <>
      <Button
        size="small"
        startIcon={<ArrowBackIcon />}
        onClick={onBack}
        data-testid="connection-detail-back"
        sx={{ mb: 1 }}
      >
        Connections
      </Button>
      <Stack sx={{ mb: 2 }}>
        <Typography variant="h5" component="h2">
          {connection.displayName}
        </Typography>
        <Typography variant="body2" sx={{ color: 'text.secondary' }}>
          Connection · {connection.name} · {connection.strategy}
        </Typography>
      </Stack>
      <Tabs
        value={surface}
        onChange={(_event, value: ConnectionSurface) => setSurface(value)}
        sx={{ mb: 2 }}
      >
        <Tab value="general" label="General" data-testid="connection-tab-general" />
        <Tab value="settings" label="Settings" data-testid="connection-tab-settings" />
      </Tabs>
      {surface === 'general' ? (
        <ConnectionGeneralTab tenantId={tenantId} connection={connection} />
      ) : (
        <ConnectionSettingsTab tenantId={tenantId} connectionId={connection.connectionId} />
      )}
    </>
  );
}

function ConnectionGeneralTab({
  tenantId,
  connection,
}: {
  readonly tenantId: string;
  readonly connection: ConnectionResource;
}) {
  const cell = useTenantCell();
  const [updateConnection, { isLoading }] = useUpdateConnectionMutation();
  const [displayName, setDisplayName] = useState(connection.displayName);
  const [isEnabled, setIsEnabled] = useState(connection.isEnabled);
  const [error, setError] = useState<string>();
  const [saved, setSaved] = useState(false);

  const canSave = displayName.trim().length > 0 && !isLoading;

  const handleSave = async () => {
    setError(undefined);
    setSaved(false);
    try {
      await updateConnection({
        tenantId,
        connectionId: connection.connectionId,
        concurrencyToken: connection.concurrencyToken,
        fields: {
          displayName: displayName.trim(),
          isEnabled,
          options: connection.options,
        },
        cell,
      }).unwrap();
      setSaved(true);
    } catch (submitError) {
      setError(apiErrorMessage(submitError as ApiError));
    }
  };

  return (
    <Stack spacing={2} sx={{ maxWidth: 480 }}>
      {error && (
        <Alert severity="error" data-testid="connection-general-error">
          {error}
        </Alert>
      )}
      {saved && (
        <Alert severity="success" data-testid="connection-general-saved">
          Saved.
        </Alert>
      )}
      <TextField
        label="Display name"
        value={displayName}
        onChange={(event) => setDisplayName(event.target.value)}
        required
        fullWidth
        slotProps={{ htmlInput: { 'data-testid': 'connection-detail-display-name' } }}
      />
      {connection.strategy === 'local' && (
        <TextField
          label="Store"
          value={connectionStore(connection)}
          disabled
          fullWidth
          helperText="The named local-account store that holds this connection's accounts. It is fixed when the connection is created."
          slotProps={{ htmlInput: { 'data-testid': 'connection-detail-store' } }}
        />
      )}
      <FormControlLabel
        control={
          <Switch checked={isEnabled} onChange={(event) => setIsEnabled(event.target.checked)} />
        }
        label="Enabled"
      />
      <div>
        <Button
          variant="contained"
          onClick={handleSave}
          disabled={!canSave}
          data-testid="connection-general-save"
        >
          {isLoading ? 'Saving…' : 'Save'}
        </Button>
      </div>
    </Stack>
  );
}

function ConnectionSettingsTab({
  tenantId,
  connectionId,
}: {
  readonly tenantId: string;
  readonly connectionId: string;
}) {
  const cell = useTenantCell();
  const args = { tenantId, connectionId, cell };
  const { data: settings, isLoading, error } = useGetConnectionSettingsQuery(args);
  const { data: effective } = useGetConnectionEffectiveSettingsQuery(args);
  const {
    data: limits,
    isLoading: limitsLoading,
    error: limitsError,
  } = useGetConnectionSettingLimitsQuery(args);
  const [updateSettings, { isLoading: isSaving }] = useUpdateConnectionSettingsMutation();

  return (
    <SettingsManager
      settings={settings}
      effective={effective}
      limits={limits}
      isLoading={isLoading || limitsLoading}
      loadError={Boolean(error) || Boolean(limitsError)}
      isSaving={isSaving}
      onSave={async ({ concurrencyToken, original, desired }) => {
        await updateSettings({
          tenantId,
          connectionId,
          concurrencyToken,
          original,
          desired,
          cell,
        }).unwrap();
      }}
    />
  );
}
