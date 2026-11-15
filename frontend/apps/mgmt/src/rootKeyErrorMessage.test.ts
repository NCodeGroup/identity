import { describe, expect, it } from 'vitest';
import { apiErrorMessage, type ApiError } from '@ncode/store';
import { rootKeyErrorMessage } from './rootKeyErrorMessage.js';

describe('rootKeyErrorMessage', () => {
  it('joins the validation messages of the rejected fields into one message', () => {
    const failure = {
      status: 400,
      data: {
        detail: 'One or more validation errors occurred.',
        errors: {
          provider: ['The provider is not supported.'],
          reference: ['The reference is required.', 'The reference is too long.'],
        },
      },
    };

    expect(rootKeyErrorMessage(failure)).toBe(
      'The provider is not supported. The reference is required. The reference is too long.',
    );
  });

  it('falls back to the problem detail of the server when no field message is present', () => {
    const failure = { status: 422, data: { detail: 'The probe could not decrypt with the key.' } };

    expect(rootKeyErrorMessage(failure)).toBe('The probe could not decrypt with the key.');
    expect(rootKeyErrorMessage(failure)).toBe(apiErrorMessage(failure as ApiError));
  });

  it('falls back to the status message when the errors hold no message', () => {
    const failure = { status: 400, data: { errors: {} } };

    expect(rootKeyErrorMessage(failure)).toBe(apiErrorMessage(failure as ApiError));
  });

  it('ignores an entry among the field messages that is not a string', () => {
    const failure = {
      status: 400,
      data: { errors: { reference: ['The reference is required.', 7, null, { text: 'x' }] } },
    };

    expect(rootKeyErrorMessage(failure)).toBe('The reference is required.');
  });

  it('ignores a field whose messages are not a list', () => {
    const failure = {
      status: 400,
      data: { errors: { provider: 'The provider is not supported.', reference: ['Required.'] } },
    };

    expect(rootKeyErrorMessage(failure)).toBe('Required.');
  });

  it('answers the generic message for a missing failure', () => {
    expect(rootKeyErrorMessage(undefined)).toBe(apiErrorMessage(undefined));
  });
});
