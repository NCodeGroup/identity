import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CardContent from '@mui/material/CardContent';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { TenantCard } from '@ncodegroup/identity-ui';
import { useLocale } from './i18n/LocaleContext.js';
import { ANTIFORGERY_FIELD, LOGIN_CODE_PATH, resolveSurfacePath } from './params.js';

export type SignInCodePageProps = {
  /** The antiforgery request token echoed from the host-set cookie. */
  readonly antiforgeryToken: string;
  /** A human-readable error to show above the form, or `null` when none. */
  readonly errorMessage?: string | null;
  /** Whether the host just mailed a new code, which the page confirms above the form. */
  readonly sent?: boolean;
};

/**
 * The sign-in code page, where an end-user whose password is verified presents the code mailed to the account's
 * address. It submits as a native HTML form POST to the same path carrying the antiforgery token; the `verify` action
 * checks the code and completes the sign-in, and the `resend` action, which skips the form's validation, mails a new
 * code. The stable element ids (`code`, `verify`, `resend`) are the automation contract, the same ids the host's page
 * writes.
 */
export function SignInCodePage({ antiforgeryToken, errorMessage, sent }: SignInCodePageProps) {
  const { messages } = useLocale();
  const text = messages.signInCode;
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
            action={resolveSurfacePath(LOGIN_CODE_PATH)}
          >
            <Typography variant="h5" component="h1">
              {text.title}
            </Typography>

            {sent ? <Alert severity="success">{text.sent}</Alert> : null}

            {errorMessage ? <Alert severity="error">{errorMessage}</Alert> : null}

            <input type="hidden" name={ANTIFORGERY_FIELD} value={antiforgeryToken} />

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

            <Button
              id="verify"
              name="action"
              value="verify"
              type="submit"
              variant="contained"
              fullWidth
            >
              {text.verify}
            </Button>
            <Button
              id="resend"
              name="action"
              value="resend"
              type="submit"
              variant="text"
              formNoValidate
              fullWidth
            >
              {text.resend}
            </Button>
          </Stack>
        </CardContent>
      </TenantCard>
    </Box>
  );
}
