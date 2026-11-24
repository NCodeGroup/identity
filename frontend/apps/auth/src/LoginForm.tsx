import { useCallback } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CardContent from '@mui/material/CardContent';
import Link from '@mui/material/Link';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { TenantCard } from '@ncodegroup/identity-ui';
import { useLocale } from './i18n/LocaleContext.js';
import {
  ANTIFORGERY_FIELD,
  LOGIN_ACTION,
  RETURN_URL_FIELD,
  UI_LOCALES_FIELD,
  resolveSurfacePath,
} from './params.js';
import { writeCarriedValue, type ServerConnection } from './serverPage.js';

export type LoginFormProps = {
  /** The validated local return URL the server redirects to after a successful sign-in. */
  readonly returnUrl: string;
  /** The antiforgery request token echoed from the host-set cookie. */
  readonly antiforgeryToken: string;
  /** A human-readable error to show above the form, or `null` when none. */
  readonly errorMessage?: string | null;
  /** A human-readable status to show above the form, such as a completed password reset, or `null` when none. */
  readonly statusMessage?: string | null;
  /** The address of the forgot-password page to link to, or `null` when the tenant offers no password reset. */
  readonly forgotPasswordHref?: string | null;
  /** The product/tenant display name shown in the heading. */
  readonly productName?: string;
  /**
   * The field values typed or autofilled into the server-rendered form before the bundle mounted, by field id.
   */
  readonly carriedValues?: Readonly<Record<string, string>>;
  /** The id of the field that had focus before the bundle mounted, which keeps it. */
  readonly focusedFieldId?: string | null;
  /** The connections the form offers: a picker when several, a hidden field when one, nothing when none. */
  readonly connections?: readonly ServerConnection[];
  /** The name of the connection the form preselects, or `null` for none. */
  readonly selectedConnection?: string | null;
};

/**
 * The field that carries the connection a form signs in through: a select (`id="connection"`) when several connections
 * are offered, a hidden input when one is, and nothing when none is.
 */
export function ConnectionField({
  connections,
  selectedConnection,
}: {
  readonly connections: readonly ServerConnection[];
  readonly selectedConnection: string | null;
}) {
  const { messages } = useLocale();
  const [only] = connections;
  if (only === undefined) {
    return null;
  }
  if (connections.length === 1) {
    return <input type="hidden" name="connection" value={only.name} />;
  }
  return (
    <TextField
      id="connection"
      name="connection"
      label={messages.login.connection}
      select
      defaultValue={selectedConnection ?? ''}
      slotProps={{ select: { native: true } }}
      fullWidth
    >
      {selectedConnection === null ? <option value="" /> : null}
      {connections.map((connection) => (
        <option key={connection.name} value={connection.name}>
          {connection.displayName}
        </option>
      ))}
    </TextField>
  );
}

/**
 * The stateless login form. It submits as a native HTML form POST so the browser follows the cookie
 * session and the server-validated return-URL redirect; the stable element ids (`username`, `password`,
 * `login`) are the automation contract and are set explicitly.
 */
export function LoginForm({
  returnUrl,
  antiforgeryToken,
  errorMessage,
  statusMessage,
  forgotPasswordHref,
  productName,
  carriedValues,
  focusedFieldId,
  connections = [],
  selectedConnection = null,
}: LoginFormProps) {
  const { pageLanguage, messages } = useLocale();
  const carriedUsername = carriedValues?.['username'];
  const carriedPassword = carriedValues?.['password'];
  // A carried value is written to the DOM property, never to React's `defaultValue`, which would put a password in the
  // `value` attribute (ADR-1253).
  const carryUsername = useCallback(
    (input: HTMLInputElement | null) => writeCarriedValue(input, carriedUsername),
    [carriedUsername],
  );
  const carryPassword = useCallback(
    (input: HTMLInputElement | null) => writeCarriedValue(input, carriedPassword),
    [carriedPassword],
  );

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
            action={resolveSurfacePath(LOGIN_ACTION)}
          >
            <Typography variant="h5" component="h1">
              {productName ? messages.login.titleFor(productName) : messages.login.title}
            </Typography>

            {statusMessage ? <Alert severity="success">{statusMessage}</Alert> : null}

            {errorMessage ? <Alert severity="error">{errorMessage}</Alert> : null}

            <input type="hidden" name={RETURN_URL_FIELD} value={returnUrl} />
            <input type="hidden" name={ANTIFORGERY_FIELD} value={antiforgeryToken} />
            {pageLanguage ? (
              <input type="hidden" name={UI_LOCALES_FIELD} value={pageLanguage} />
            ) : null}

            <ConnectionField connections={connections} selectedConnection={selectedConnection} />
            <TextField
              id="username"
              name="username"
              label={messages.login.username}
              autoComplete="username"
              autoFocus={focusedFieldId !== 'password'}
              inputRef={carryUsername}
              required
              fullWidth
            />
            <TextField
              id="password"
              name="password"
              label={messages.login.password}
              type="password"
              autoComplete="current-password"
              autoFocus={focusedFieldId === 'password'}
              inputRef={carryPassword}
              required
              fullWidth
            />

            <Button
              id="login"
              name="login"
              value="login"
              type="submit"
              variant="contained"
              fullWidth
            >
              {messages.login.submit}
            </Button>

            {forgotPasswordHref ? (
              <Link id="forgot-password-link" href={forgotPasswordHref}>
                {messages.login.forgotPassword}
              </Link>
            ) : null}
          </Stack>
        </CardContent>
      </TenantCard>
    </Box>
  );
}
