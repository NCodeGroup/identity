import { useEffect, useRef, useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import { inputBaseClasses } from '@mui/material/InputBase';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';

export type LocalAccountMetadataEditorProps = {
  /** The persisted profile metadata as JSON text; reseeds the editor when it changes. */
  readonly profileJson: string;
  /** The persisted system metadata as JSON text; reseeds the editor when it changes. */
  readonly systemJson: string;
  readonly isSaving?: boolean;
  /** A server-side error surfaced after a failed save. */
  readonly error?: string | undefined;
  readonly onSave: (profile: Record<string, unknown>, system: Record<string, unknown>) => void;
};

const notObjectMessage = 'Must be a JSON object.';

/** Parses text into a plain JSON object, or returns undefined when it is not one. */
function parseObject(text: string): Record<string, unknown> | undefined {
  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch {
    return undefined;
  }
  if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed)) {
    return undefined;
  }
  return parsed as Record<string, unknown>;
}

/**
 * Stateless local-account metadata editor. Both documents are opaque JSON objects edited as text; the text reseeds when
 * {@link LocalAccountMetadataEditorProps.profileJson} or {@link LocalAccountMetadataEditorProps.systemJson} changes
 * after a save, and {@link LocalAccountMetadataEditorProps.onSave} receives both parsed objects.
 */
export function LocalAccountMetadataEditor({
  profileJson,
  systemJson,
  isSaving = false,
  error,
  onSave,
}: LocalAccountMetadataEditorProps) {
  const [profileText, setProfileText] = useState(profileJson);
  const [systemText, setSystemText] = useState(systemJson);
  const previousRef = useRef({ profileJson, systemJson });

  useEffect(() => {
    if (
      profileJson !== previousRef.current.profileJson ||
      systemJson !== previousRef.current.systemJson
    ) {
      setProfileText(profileJson);
      setSystemText(systemJson);
      previousRef.current = { profileJson, systemJson };
    }
  }, [profileJson, systemJson]);

  const profile = parseObject(profileText);
  const system = parseObject(systemText);
  const isDirty = profileText !== profileJson || systemText !== systemJson;

  const handleSave = () => {
    if (!profile || !system || !isDirty || isSaving) {
      return;
    }
    onSave(profile, system);
  };

  const monospace = { [`& .${inputBaseClasses.input}`]: { typography: 'code' } };

  return (
    <Stack spacing={2}>
      {error && (
        <Alert severity="error" data-testid="metadata-error">
          {error}
        </Alert>
      )}
      <TextField
        label="Profile metadata"
        value={profileText}
        onChange={(event) => setProfileText(event.target.value)}
        error={!profile}
        helperText={profile ? undefined : notObjectMessage}
        multiline
        minRows={4}
        fullWidth
        sx={monospace}
        slotProps={{ htmlInput: { 'data-testid': 'metadata-profile', spellCheck: false } }}
      />
      <TextField
        label="System metadata"
        value={systemText}
        onChange={(event) => setSystemText(event.target.value)}
        error={!system}
        helperText={system ? 'Server-controlled; never writable by the user.' : notObjectMessage}
        multiline
        minRows={4}
        fullWidth
        sx={monospace}
        slotProps={{ htmlInput: { 'data-testid': 'metadata-system', spellCheck: false } }}
      />
      <Box sx={{ display: 'flex', justifyContent: 'flex-end' }}>
        <Button
          variant="contained"
          onClick={handleSave}
          disabled={!profile || !system || !isDirty || isSaving}
          data-testid="metadata-save"
        >
          {isSaving ? 'Saving…' : 'Save metadata'}
        </Button>
      </Box>
    </Stack>
  );
}
