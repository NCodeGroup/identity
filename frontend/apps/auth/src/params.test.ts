import { describe, expect, it } from 'vitest';
import { en } from './i18n/en.js';
import {
  describeEmailCodeError,
  describeLoginError,
  describePasswordError,
  getQueryParam,
  isAccountPath,
  isApplicationsPath,
  isConsentPath,
  isEmailPath,
  isLoginCodePath,
  isLogoutConfirmPath,
  isPasswordForgotPath,
  isPasswordPath,
  isPasswordResetPath,
  isSessionsPath,
  readCookie,
  resolveSurfacePath,
} from './params.js';

const TENANT_BASE = 'https://op.example/acme/account/';

describe('getQueryParam', () => {
  it('reads a value with or without a leading question mark', () => {
    expect(getQueryParam('?ReturnUrl=%2Fauthorize%3Fx%3D1', 'ReturnUrl')).toBe('/authorize?x=1');
    expect(getQueryParam('error=invalid_credentials', 'error')).toBe('invalid_credentials');
  });

  it('returns null when absent', () => {
    expect(getQueryParam('?a=b', 'missing')).toBeNull();
  });
});

describe('readCookie', () => {
  it('reads a cookie value and url-decodes it', () => {
    expect(readCookie('a=1; XSRF-TOKEN=tok%2Fen; b=2', 'XSRF-TOKEN')).toBe('tok/en');
  });

  it('returns null when the cookie is absent', () => {
    expect(readCookie('a=1; b=2', 'XSRF-TOKEN')).toBeNull();
  });
});

describe('resolveSurfacePath', () => {
  it('resolves a surface path against the document base by default', () => {
    expect(resolveSurfacePath('me')).toBe('/account/me');
    expect(resolveSurfacePath('./')).toBe('/account/');
  });

  it('resolves a surface path beneath the tenant the base names', () => {
    expect(resolveSurfacePath('consent/details', TENANT_BASE)).toBe(
      '/acme/account/consent/details',
    );
    expect(resolveSurfacePath('./', TENANT_BASE)).toBe('/acme/account/');
  });
});

describe('surface page matching beneath a tenant', () => {
  it('matches each page beneath the tenant the base names', () => {
    expect(isConsentPath('/acme/account/consent', TENANT_BASE)).toBe(true);
    expect(isAccountPath('/acme/account', TENANT_BASE)).toBe(true);
    expect(isAccountPath('/acme/account/', TENANT_BASE)).toBe(true);
    expect(isApplicationsPath('/acme/account/applications', TENANT_BASE)).toBe(true);
    expect(isSessionsPath('/acme/account/sessions/', TENANT_BASE)).toBe(true);
    expect(isLogoutConfirmPath('/acme/account/logout/confirm', TENANT_BASE)).toBe(true);
  });

  it('does not match a page of another tenant', () => {
    expect(isConsentPath('/account/consent', TENANT_BASE)).toBe(false);
    expect(isAccountPath('/beta/account', TENANT_BASE)).toBe(false);
  });
});

describe('isConsentPath', () => {
  it('matches the consent path with trailing slashes', () => {
    expect(isConsentPath('/account/consent')).toBe(true);
    expect(isConsentPath('/account/consent/')).toBe(true);
  });

  it('does not match the login path', () => {
    expect(isConsentPath('/account/login')).toBe(false);
  });
});

describe('isAccountPath', () => {
  it('matches the account root with trailing slashes', () => {
    expect(isAccountPath('/account')).toBe(true);
    expect(isAccountPath('/account/')).toBe(true);
  });

  it('does not match the login or consent paths', () => {
    expect(isAccountPath('/account/login')).toBe(false);
    expect(isAccountPath('/account/consent')).toBe(false);
  });
});

describe('isApplicationsPath', () => {
  it('matches the applications path with trailing slashes', () => {
    expect(isApplicationsPath('/account/applications')).toBe(true);
    expect(isApplicationsPath('/account/applications/')).toBe(true);
  });

  it('does not match the account root or other paths', () => {
    expect(isApplicationsPath('/account')).toBe(false);
    expect(isApplicationsPath('/account/consent')).toBe(false);
  });
});

describe('isPasswordPath', () => {
  it('matches the password path with trailing slashes', () => {
    expect(isPasswordPath('/account/password')).toBe(true);
    expect(isPasswordPath('/account/password/')).toBe(true);
  });

  it('does not match the account root or other paths', () => {
    expect(isPasswordPath('/account')).toBe(false);
    expect(isPasswordPath('/account/sessions')).toBe(false);
  });

  it('does not match the forgot-password or reset-password paths', () => {
    expect(isPasswordPath('/account/password/forgot')).toBe(false);
    expect(isPasswordPath('/account/password/reset')).toBe(false);
  });
});

describe('isPasswordForgotPath', () => {
  it('matches the forgot-password path with trailing slashes', () => {
    expect(isPasswordForgotPath('/account/password/forgot')).toBe(true);
    expect(isPasswordForgotPath('/account/password/forgot/')).toBe(true);
    expect(isPasswordForgotPath('/acme/account/password/forgot', TENANT_BASE)).toBe(true);
  });

  it('does not match the change-password or reset-password paths', () => {
    expect(isPasswordForgotPath('/account/password')).toBe(false);
    expect(isPasswordForgotPath('/account/password/reset')).toBe(false);
  });
});

describe('isPasswordResetPath', () => {
  it('matches the reset-password path with trailing slashes', () => {
    expect(isPasswordResetPath('/account/password/reset')).toBe(true);
    expect(isPasswordResetPath('/account/password/reset/')).toBe(true);
    expect(isPasswordResetPath('/acme/account/password/reset', TENANT_BASE)).toBe(true);
  });

  it('does not match the change-password or forgot-password paths', () => {
    expect(isPasswordResetPath('/account/password')).toBe(false);
    expect(isPasswordResetPath('/account/password/forgot')).toBe(false);
  });
});

describe('isLoginCodePath', () => {
  it('matches the sign-in code path with trailing slashes', () => {
    expect(isLoginCodePath('/account/login/code')).toBe(true);
    expect(isLoginCodePath('/account/login/code/')).toBe(true);
    expect(isLoginCodePath('/acme/account/login/code', TENANT_BASE)).toBe(true);
  });

  it('does not match the login path or other paths', () => {
    expect(isLoginCodePath('/account/login')).toBe(false);
    expect(isLoginCodePath('/account/email')).toBe(false);
  });
});

describe('isEmailPath', () => {
  it('matches the email path with trailing slashes', () => {
    expect(isEmailPath('/account/email')).toBe(true);
    expect(isEmailPath('/account/email/')).toBe(true);
    expect(isEmailPath('/acme/account/email', TENANT_BASE)).toBe(true);
  });

  it('does not match the account root or the email endpoints', () => {
    expect(isEmailPath('/account')).toBe(false);
    expect(isEmailPath('/account/email/status')).toBe(false);
  });
});

describe('isLogoutConfirmPath', () => {
  it('matches the logout confirmation path with trailing slashes', () => {
    expect(isLogoutConfirmPath('/account/logout/confirm')).toBe(true);
    expect(isLogoutConfirmPath('/account/logout/confirm/')).toBe(true);
  });

  it('does not match the logout action or other paths', () => {
    expect(isLogoutConfirmPath('/account/logout')).toBe(false);
    expect(isLogoutConfirmPath('/account')).toBe(false);
  });
});

describe('describeLoginError', () => {
  it('maps known codes and defaults unknown ones', () => {
    expect(describeLoginError(null, en)).toBeNull();
    expect(describeLoginError('invalid_credentials', en)).toMatch(/incorrect/i);
    expect(describeLoginError('invalid_request', en)).toMatch(/username and a password/i);
    expect(describeLoginError('access_denied', en)).toMatch(/cannot sign in/i);
    expect(describeLoginError('too_many_codes', en)).toBe(en.emailCodeErrors.tooManyCodes);
    expect(describeLoginError('boom', en)).toMatch(/failed/i);
  });
});

describe('describePasswordError', () => {
  it('maps known codes and defaults unknown ones', () => {
    expect(describePasswordError(null, en)).toBeNull();
    expect(describePasswordError('invalid_request', en)).toBe(
      'Enter your current password and a new password.',
    );
    expect(describePasswordError('password_mismatch', en)).toBe('The new passwords do not match.');
    expect(describePasswordError('invalid_current_password', en)).toBe(
      'The current password is incorrect.',
    );
    expect(describePasswordError('not_supported', en)).toBe(
      'Your account has no password to change here.',
    );
    expect(describePasswordError('boom', en)).toBe(
      'The password could not be changed. Please try again.',
    );
  });

  it('maps the password policy codes', () => {
    expect(describePasswordError('password_too_short', en)).toBe(
      'The new password is too short. A passphrase of several words is long enough and easy to remember.',
    );
    expect(describePasswordError('password_too_long', en)).toBe(
      'The new password is too long. Choose a shorter one.',
    );
    expect(describePasswordError('password_blocklisted', en)).toBe(
      'The new password is too common or is known to be compromised. Choose one you do not use anywhere else.',
    );
  });
});

describe('describeEmailCodeError', () => {
  it('maps the email-code codes', () => {
    expect(describeEmailCodeError(null, en)).toBeNull();
    expect(describeEmailCodeError('invalid_code', en)).toBe(en.emailCodeErrors.invalidCode);
    expect(describeEmailCodeError('too_many_codes', en)).toBe(en.emailCodeErrors.tooManyCodes);
    expect(describeEmailCodeError('password_mismatch', en)).toBe(
      en.emailCodeErrors.passwordMismatch,
    );
    expect(describeEmailCodeError('no_email', en)).toBe(en.emailCodeErrors.noEmail);
    expect(describeEmailCodeError('already_verified', en)).toBe(en.emailCodeErrors.alreadyVerified);
  });

  it('maps a missing field to the login message for it', () => {
    expect(describeEmailCodeError('invalid_request', en)).toBe(en.loginErrors.invalidRequest);
  });

  it('maps the password policy codes through the change-password messages', () => {
    expect(describeEmailCodeError('password_too_short', en)).toBe(
      en.passwordErrors.passwordTooShort,
    );
    expect(describeEmailCodeError('password_too_long', en)).toBe(en.passwordErrors.passwordTooLong);
    expect(describeEmailCodeError('password_blocklisted', en)).toBe(
      en.passwordErrors.passwordBlocklisted,
    );
  });

  it('defaults an unknown code', () => {
    expect(describeEmailCodeError('boom', en)).toBe(en.emailCodeErrors.fallback);
  });
});
