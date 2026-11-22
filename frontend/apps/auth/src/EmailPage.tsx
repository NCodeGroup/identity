import { useEffect, useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import {
  ACCOUNT_PATH,
  ANTIFORGERY_FIELD,
  EMAIL_PATH,
  EMAIL_SEND_ACTION,
  EMAIL_STATUS_ENDPOINT,
  EMAIL_VERIFY_ACTION,
  resolveSurfacePath,
} from './params.js';
import { useLocale } from './i18n/LocaleContext.js';
import { fetchSurfaceJson } from './surfaceFetch.js';

/** The end-user's email address and whether it is verified (mirrors the OP's `/account/email/status` payload). */
export type AccountEmail = {
  readonly email: string;
  readonly verified: boolean;
};

export type EmailPageProps = {
  /** The antiforgery request token echoed from the host-set cookie, posted by the send and verify forms. */
  readonly antiforgeryToken: string;
  /** The handle of the verification flow the host started by mailing a code, which the verify form carries back. */
  readonly flow?: string | null;
  /** The `status` the host redirected back with, such as `verified`. */
  readonly status?: string | null;
  /** A human-readable error to show above the page, or `null` when none. */
  readonly errorMessage?: string | null;
  /** The product/tenant display name shown in the heading. */
  readonly productName?: string;
  /**
   * Loads the address and its state, or `null` when the end-user has no address to verify or the tenant does not offer
   * verification; defaults to a same-origin fetch of the email status endpoint.
   */
  readonly loadEmail?: () => Promise<AccountEmail | null>;
};

/**
 * Default loader: loads the OP's email status endpoint. The login redirect on a `401` comes from `fetchSurfaceJson`,
 * and any other refusal, such as a `404`, yields `null`.
 */
async function fetchEmail(): Promise<AccountEmail | null> {
  return fetchSurfaceJson<AccountEmail>(EMAIL_STATUS_ENDPOINT, EMAIL_PATH);
}

/**
 * The self-service email page, where the signed-in end-user verifies the account's email address. It loads the address
 * and shows whether it is verified. An unverified address offers a form that mails a code; once the host has mailed one
 * and redirected back with its flow handle, the page offers a form that presents the code. Both forms are native HTML
 * form POSTs carrying the antiforgery token, so the server runs the full verification seam and redirects.
 */
export function EmailPage({
  antiforgeryToken,
  flow,
  status,
  errorMessage,
  productName,
  loadEmail = fetchEmail,
}: EmailPageProps) {
  const { messages } = useLocale();
  const text = messages.email;
  // `undefined` while loading, `null` when there is nothing to verify.
  const [email, setEmail] = useState<AccountEmail | null | undefined>(undefined);

  useEffect(() => {
    let active = true;
    loadEmail()
      .then((loaded) => {
        if (active) {
          setEmail(loaded);
        }
      })
      .catch(() => {
        /* a failed load leaves the page in its loading state */
      });
    return () => {
      active = false;
    };
  }, [loadEmail]);

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
      <Card sx={{ width: '100%', maxWidth: 440 }}>
        <CardContent>
          <Stack spacing={3}>
            <Typography variant="h5" component="h1">
              {productName ? text.titleFor(productName) : text.title}
            </Typography>

            {status === 'verified' ? (
              <Alert severity="success" data-testid="email-verified-status">
                {text.verifiedStatus}
              </Alert>
            ) : null}

            {errorMessage ? <Alert severity="error">{errorMessage}</Alert> : null}

            {email === undefined ? (
              <Typography data-testid="email-loading" sx={{ color: 'text.secondary' }}>
                {text.loading}
              </Typography>
            ) : email === null ? (
              <Typography data-testid="email-unavailable" sx={{ color: 'text.secondary' }}>
                {text.unavailable}
              </Typography>
            ) : (
              <Stack spacing={1} data-testid="email-details">
                <Stack direction="row" spacing={2} sx={{ justifyContent: 'space-between' }}>
                  <Typography sx={{ color: 'text.secondary' }}>{text.address}</Typography>
                  <Typography
                    sx={{ wordBreak: 'break-all', textAlign: 'right' }}
                    data-testid="email-address"
                  >
                    {email.email}
                  </Typography>
                </Stack>
                <Typography
                  data-testid="email-state"
                  sx={{ color: email.verified ? 'success.main' : 'text.secondary' }}
                >
                  {email.verified ? text.verified : text.unverified}
                </Typography>
              </Stack>
            )}

            {email && !email.verified && !flow ? (
              <Stack
                component="form"
                method="post"
                action={resolveSurfacePath(EMAIL_SEND_ACTION)}
                data-testid="email-send"
              >
                <input type="hidden" name={ANTIFORGERY_FIELD} value={antiforgeryToken} />
                <Button id="send-email-code" type="submit" variant="contained" fullWidth>
                  {text.sendCode}
                </Button>
              </Stack>
            ) : null}

            {email && !email.verified && flow ? (
              <Stack
                spacing={2}
                component="form"
                method="post"
                action={resolveSurfacePath(EMAIL_VERIFY_ACTION)}
                data-testid="email-verify"
              >
                <input type="hidden" name={ANTIFORGERY_FIELD} value={antiforgeryToken} />
                <input type="hidden" name="flow" value={flow} />
                <Typography variant="body2">{text.codePrompt}</Typography>
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
                <Button id="verify-email" type="submit" variant="contained" fullWidth>
                  {text.verify}
                </Button>
              </Stack>
            ) : null}

            <Button href={resolveSurfacePath(ACCOUNT_PATH)} variant="text">
              {text.back}
            </Button>
          </Stack>
        </CardContent>
      </Card>
    </Box>
  );
}
