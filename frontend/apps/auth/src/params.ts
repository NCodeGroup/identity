/**
 * UI-free helpers for the interactive login/consent surface: reading the return URL, the antiforgery
 * request token (echoed from a cookie the host sets), the error code, and which page to render. Every
 * path is relative to the surface: the host serves the bundle beneath the tenant's route and sets the
 * document's base to the surface there (`<base href="/{tenant}/account/">`), so the bundle resolves its
 * paths against `document.baseURI` and never assumes where it is mounted (ADR-1193). The helpers take
 * the base as a parameter, defaulting to the document's, so they are unit-testable.
 */

import type { Messages } from './i18n/messages.js';

/** The form field and query parameter that carry the return URL the end user continues to after the page. */
export const RETURN_URL_FIELD = 'ReturnUrl';

/**
 * The form field that carries the language tag the host selected for the page back to it, so a page the host
 * redirects to after a failed sign-in renders in the same language; the authorization request's parameter name.
 */
export const UI_LOCALES_FIELD = 'ui_locales';

/** The server-set, JavaScript-readable cookie whose value is echoed back as the antiforgery token. */
export const ANTIFORGERY_COOKIE = '__Host-XSRF-TOKEN';

/** The antiforgery form field the ASP.NET Core antiforgery middleware validates on POST. */
export const ANTIFORGERY_FIELD = '__RequestVerificationToken';

/** The form field and query parameter that carry the handle of an email-code flow back to the host. */
export const FLOW_FIELD = 'flow';

/** The surface path the login form posts credentials to. */
export const LOGIN_ACTION = 'login';

/** The surface path the consent form posts its outcome to. */
export const CONSENT_ACTION = 'consent';

/** The surface JSON endpoint returning the pending authorization request the consent page presents. */
export const CONSENT_DETAILS_ENDPOINT = 'consent/details';

/** The surface path the account page's sign-out form posts to (first-party RP-Initiated Logout). */
export const LOGOUT_ACTION = 'logout';

/** The logout confirmation page, which an end-session request that needs the end-user's confirmation redirects to; its form posts to the same path. */
export const LOGOUT_CONFIRM_PATH = 'logout/confirm';

/** The account/session ("me") page, served at the interactive surface root. */
export const ACCOUNT_PATH = './';

/** The surface JSON endpoint returning the current end-user's session for the account page. */
export const ME_ENDPOINT = 'me';

/** The self-service "authorized applications" page, where the end-user reviews and revokes granted access. */
export const APPLICATIONS_PATH = 'applications';

/** The surface JSON endpoint returning the end-user's authorized applications. */
export const APPLICATIONS_LIST_ENDPOINT = 'applications/list';

/** The surface path the authorized-applications page posts to in order to revoke a single application. */
export const APPLICATIONS_REVOKE_ACTION = 'applications/revoke';

/** The self-service "active sessions" page, where the end-user reviews and signs out of their sessions. */
export const SESSIONS_PATH = 'sessions';

/** The surface JSON endpoint returning the end-user's active sessions. */
export const SESSIONS_LIST_ENDPOINT = 'sessions/list';

/** The surface path the active-sessions page posts to in order to revoke a single session by its `sid`. */
export const SESSIONS_REVOKE_ACTION = 'sessions/revoke';

/** The surface path the active-sessions page posts to in order to revoke every session (optionally the others only). */
export const SESSIONS_REVOKE_ALL_ACTION = 'sessions/revoke-all';

/** The self-service change-password page; its form posts to the same path. */
export const PASSWORD_PATH = 'password';

/** The forgot-password page, where an end-user who is not signed in asks for a reset code; its form posts to it. */
export const PASSWORD_FORGOT_PATH = 'password/forgot';

/**
 * The reset-password page, where an end-user presents the reset code and chooses a new password; its form posts to it.
 */
export const PASSWORD_RESET_PATH = 'password/reset';

/**
 * The sign-in code page, where an end-user whose password is verified presents the code mailed to the account's
 * address; its form posts to it.
 */
export const LOGIN_CODE_PATH = 'login/code';

/** The self-service email page, where the end-user verifies the account's email address. */
export const EMAIL_PATH = 'email';

/** The surface JSON endpoint returning the account's email address and whether it is verified. */
export const EMAIL_STATUS_ENDPOINT = 'email/status';

/** The surface path the email page posts to in order to mail a verification code. */
export const EMAIL_SEND_ACTION = 'email/send';

/** The surface path the email page posts the flow and the mailed code to in order to verify the address. */
export const EMAIL_VERIFY_ACTION = 'email/verify';

/** The path that renders the consent page (every other path renders login). */
export const CONSENT_PATH = 'consent';

/**
 * Resolves a surface path to the same-origin absolute path the host serves it at, for example `me` to
 * `/acme/account/me` beneath the `acme` tenant.
 */
export function resolveSurfacePath(
  surfacePath: string,
  baseUri: string = document.baseURI,
): string {
  return new URL(surfacePath, baseUri).pathname;
}

/** True when the given absolute path is the given surface path, ignoring a trailing slash. */
function isSurfacePath(pathname: string, surfacePath: string, baseUri: string): boolean {
  const trim = (path: string) => path.replace(/\/+$/, '');
  return trim(pathname) === trim(resolveSurfacePath(surfacePath, baseUri));
}

/** Reads a single query-string parameter, returning `null` when absent. */
export function getQueryParam(search: string, name: string): string | null {
  const params = new URLSearchParams(search.startsWith('?') ? search.slice(1) : search);
  return params.get(name);
}

/** Reads a cookie value from a `document.cookie`-style string, returning `null` when absent. */
export function readCookie(cookieString: string, name: string): string | null {
  const prefix = `${name}=`;
  for (const part of cookieString.split(';')) {
    const trimmed = part.trim();
    if (trimmed.startsWith(prefix)) {
      return decodeURIComponent(trimmed.slice(prefix.length));
    }
  }
  return null;
}

/** True when the given path should render the consent page rather than the login page. */
export function isConsentPath(pathname: string, baseUri: string = document.baseURI): boolean {
  return isSurfacePath(pathname, CONSENT_PATH, baseUri);
}

/** True when the given path should render the account/session ("me") page. */
export function isAccountPath(pathname: string, baseUri: string = document.baseURI): boolean {
  return isSurfacePath(pathname, ACCOUNT_PATH, baseUri);
}

/** True when the given path should render the authorized-applications page. */
export function isApplicationsPath(pathname: string, baseUri: string = document.baseURI): boolean {
  return isSurfacePath(pathname, APPLICATIONS_PATH, baseUri);
}

/** True when the given path should render the active-sessions page. */
export function isSessionsPath(pathname: string, baseUri: string = document.baseURI): boolean {
  return isSurfacePath(pathname, SESSIONS_PATH, baseUri);
}

/** True when the given path should render the change-password page. */
export function isPasswordPath(pathname: string, baseUri: string = document.baseURI): boolean {
  return isSurfacePath(pathname, PASSWORD_PATH, baseUri);
}

/** True when the given path should render the forgot-password page. */
export function isPasswordForgotPath(
  pathname: string,
  baseUri: string = document.baseURI,
): boolean {
  return isSurfacePath(pathname, PASSWORD_FORGOT_PATH, baseUri);
}

/** True when the given path should render the reset-password page. */
export function isPasswordResetPath(pathname: string, baseUri: string = document.baseURI): boolean {
  return isSurfacePath(pathname, PASSWORD_RESET_PATH, baseUri);
}

/** True when the given path should render the sign-in code page. */
export function isLoginCodePath(pathname: string, baseUri: string = document.baseURI): boolean {
  return isSurfacePath(pathname, LOGIN_CODE_PATH, baseUri);
}

/** True when the given path should render the email page. */
export function isEmailPath(pathname: string, baseUri: string = document.baseURI): boolean {
  return isSurfacePath(pathname, EMAIL_PATH, baseUri);
}

/** True when the given path should render the logout confirmation page. */
export function isLogoutConfirmPath(pathname: string, baseUri: string = document.baseURI): boolean {
  return isSurfacePath(pathname, LOGOUT_CONFIRM_PATH, baseUri);
}

/** Maps a login error code to the page's message for it. */
export function describeLoginError(error: string | null, messages: Messages): string | null {
  switch (error) {
    case null:
      return null;
    case 'invalid_credentials':
      return messages.loginErrors.invalidCredentials;
    case 'invalid_request':
      return messages.loginErrors.invalidRequest;
    case 'connection_required':
      return messages.loginErrors.connectionRequired;
    case 'connection_unavailable':
      return messages.loginErrors.connectionUnavailable;
    case 'access_denied':
      return messages.loginErrors.accessDenied;
    case 'too_many_codes':
      return messages.emailCodeErrors.tooManyCodes;
    default:
      return messages.loginErrors.fallback;
  }
}

/** Maps a change-password error code to the page's message for it. */
export function describePasswordError(error: string | null, messages: Messages): string | null {
  switch (error) {
    case null:
      return null;
    case 'invalid_request':
      return messages.passwordErrors.invalidRequest;
    case 'password_mismatch':
      return messages.passwordErrors.passwordMismatch;
    case 'invalid_current_password':
      return messages.passwordErrors.invalidCurrentPassword;
    case 'not_supported':
      return messages.passwordErrors.notSupported;
    case 'password_too_short':
      return messages.passwordErrors.passwordTooShort;
    case 'password_too_long':
      return messages.passwordErrors.passwordTooLong;
    case 'password_blocklisted':
      return messages.passwordErrors.passwordBlocklisted;
    default:
      return messages.passwordErrors.fallback;
  }
}

/** Maps an email-code error code of the reset and sign-in-code pages to the page's message for it. */
export function describeEmailCodeError(error: string | null, messages: Messages): string | null {
  switch (error) {
    case null:
      return null;
    case 'invalid_code':
      return messages.emailCodeErrors.invalidCode;
    case 'too_many_codes':
      return messages.emailCodeErrors.tooManyCodes;
    case 'password_mismatch':
      return messages.emailCodeErrors.passwordMismatch;
    case 'no_email':
      return messages.emailCodeErrors.noEmail;
    case 'already_verified':
      return messages.emailCodeErrors.alreadyVerified;
    case 'invalid_request':
      return messages.emailCodeErrors.missingFields;
    case 'connection_required':
      return messages.loginErrors.connectionRequired;
    case 'connection_unavailable':
      return messages.loginErrors.connectionUnavailable;
    case 'password_too_short':
    case 'password_too_long':
    case 'password_blocklisted':
      return describePasswordError(error, messages);
    default:
      return messages.emailCodeErrors.fallback;
  }
}
