import { apiErrorMessage, type ApiError } from '@ncode/store';

/**
 * The message of a failed root key request: the validation messages of a rejected field, else the server's problem
 * detail or title (such as the failure of the probe that validates the key).
 */
export function rootKeyErrorMessage(failure: unknown): string {
  const data = (failure as { data?: unknown } | undefined)?.data;
  const errors =
    data && typeof data === 'object' ? (data as { errors?: unknown }).errors : undefined;
  if (errors && typeof errors === 'object') {
    const messages = Object.values(errors).flatMap((value) =>
      Array.isArray(value) ? value.filter((m): m is string => typeof m === 'string') : [],
    );
    if (messages.length > 0) {
      return messages.join(' ');
    }
  }
  return apiErrorMessage(failure as ApiError);
}
