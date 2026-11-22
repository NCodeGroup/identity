import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CardContent from '@mui/material/CardContent';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { TenantCard } from '@ncodegroup/identity-ui';
import { useLocale } from './i18n/LocaleContext.js';
import {
  ANTIFORGERY_FIELD,
  PASSWORD_RESET_PATH,
  UI_LOCALES_FIELD,
  resolveSurfacePath,
} from './params.js';

export type ResetPasswordPageProps = {
  /** The antiforgery request token echoed from the host-set cookie. */
  readonly antiforgeryToken: string;
  /** The validated local return URL the server redirects to after the end-user signs in again. */
  readonly returnUrl: string;
  /** The handle of the reset flow the forgot-password page started, which the form carries back. */
  readonly flow: string;
  /** A human-readable error to show above the form, or `null` when none. */
  readonly errorMessage?: string | null;
};

/**
 * The reset-password page, where an end-user who is not signed in presents the code from the email and chooses a new
 * password. It submits as a native HTML form POST to the same path, carrying the antiforgery token, the return URL and
 * the flow handle; the server redirects to the login page, or back with an error code that keeps the flow. The stable
 * element ids (`code`, `new_password`, `confirm_password`, `reset-password`) are the automation contract.
 */
export function ResetPasswordPage({
  antiforgeryToken,
  returnUrl,
  flow,
  errorMessage,
}: ResetPasswordPageProps) {
  const { pageLanguage, messages } = useLocale();
  const text = messages.resetPassword;
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
            action={resolveSurfacePath(PASSWORD_RESET_PATH)}
          >
            <Typography variant="h5" component="h1">
              {text.title}
            </Typography>

            {errorMessage ? <Alert severity="error">{errorMessage}</Alert> : null}

            <input type="hidden" name="ReturnUrl" value={returnUrl} />
            <input type="hidden" name={ANTIFORGERY_FIELD} value={antiforgeryToken} />
            <input type="hidden" name="flow" value={flow} />
            {pageLanguage ? (
              <input type="hidden" name={UI_LOCALES_FIELD} value={pageLanguage} />
            ) : null}

            <Typography variant="body2">{text.prompt}</Typography>

            <TextField
              id="code"
              name="code"
              label={text.code}
              autoComplete="one-time-code"
              autoFocus
              required
              fullWidth
              slotProps={{ htmlInput: { inputMode: 'numeric' } }}
            />
            <TextField
              id="new_password"
              name="new_password"
              label={text.newPassword}
              type="password"
              autoComplete="new-password"
              required
              fullWidth
            />
            <TextField
              id="confirm_password"
              name="confirm_password"
              label={text.confirmPassword}
              type="password"
              autoComplete="new-password"
              required
              fullWidth
            />

            <Button id="reset-password" type="submit" variant="contained" fullWidth>
              {text.submit}
            </Button>
          </Stack>
        </CardContent>
      </TenantCard>
    </Box>
  );
}
