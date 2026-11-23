import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CardContent from '@mui/material/CardContent';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { TenantCard } from '@ncodegroup/identity-ui';
import { ConnectionField } from './LoginForm.js';
import type { ServerConnection } from './serverPage.js';
import { useLocale } from './i18n/LocaleContext.js';
import {
  ANTIFORGERY_FIELD,
  PASSWORD_FORGOT_PATH,
  RETURN_URL_FIELD,
  UI_LOCALES_FIELD,
  resolveSurfacePath,
} from './params.js';

export type ForgotPasswordPageProps = {
  /** The antiforgery request token echoed from the host-set cookie. */
  readonly antiforgeryToken: string;
  /** The validated local return URL the server redirects to after the end-user signs in again. */
  readonly returnUrl: string;
  /** A human-readable error to show above the form, or `null` when none. */
  readonly errorMessage?: string | null;
  /** The connections the form offers: a picker when several, a hidden field when one, nothing when none. */
  readonly connections?: readonly ServerConnection[];
  /** The name of the connection the form preselects, or `null` for none. */
  readonly selectedConnection?: string | null;
};

/**
 * The forgot-password page, where an end-user who is not signed in asks for a reset code. It submits as a native HTML
 * form POST to the same path, carrying the antiforgery token and the return URL; the server answers with the
 * reset-password page whether or not the account exists. The stable element ids (`username`, `send-code`) are the
 * automation contract.
 */
export function ForgotPasswordPage({
  antiforgeryToken,
  returnUrl,
  errorMessage,
  connections = [],
  selectedConnection = null,
}: ForgotPasswordPageProps) {
  const { pageLanguage, messages } = useLocale();
  const text = messages.forgotPassword;
  return (
    <Box
      sx={{
        minHeight: '100vh',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        p: 2,
      }}
    >
      <TenantCard maxWidth={400}>
        <CardContent>
          <Stack
            spacing={3}
            component="form"
            method="post"
            action={resolveSurfacePath(PASSWORD_FORGOT_PATH)}
          >
            <Typography variant="h5" component="h1">
              {text.title}
            </Typography>

            {errorMessage ? <Alert severity="error">{errorMessage}</Alert> : null}

            <input type="hidden" name={RETURN_URL_FIELD} value={returnUrl} />
            <input type="hidden" name={ANTIFORGERY_FIELD} value={antiforgeryToken} />
            {pageLanguage ? (
              <input type="hidden" name={UI_LOCALES_FIELD} value={pageLanguage} />
            ) : null}

            <Typography variant="body2">{text.prompt}</Typography>

            <ConnectionField connections={connections} selectedConnection={selectedConnection} />
            <TextField
              id="username"
              name="username"
              label={messages.login.username}
              autoComplete="username"
              autoFocus
              required
              fullWidth
            />

            <Button id="send-code" type="submit" variant="contained" fullWidth>
              {text.submit}
            </Button>
          </Stack>
        </CardContent>
      </TenantCard>
    </Box>
  );
}
