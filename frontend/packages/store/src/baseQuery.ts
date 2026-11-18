import {
  fetchBaseQuery,
  type BaseQueryFn,
  type FetchArgs,
  type FetchBaseQueryError,
} from '@reduxjs/toolkit/query';
import type { RuntimeConfig } from '@ncodegroup/identity-config';
import { sessionEnded } from './sessionSlice.js';

/**
 * The slice of root state the API client depends on. The client reads the runtime configuration so
 * it can be tier-agnostic (base URL and anti-forgery header from the BFF-served bootstrap document)
 * without baking anything at build time.
 */
export type ApiRootState = {
  readonly config: { readonly runtime: RuntimeConfig | null };
};

/**
 * The arguments of a call to the management API. The `url` is relative to the API's base (`api/…`). With a `cell` the
 * call goes to that cell's management API through the console's per-cell forwarder (`{apiBaseUrl}/cells/{cell}/api/…`);
 * without one it goes to the control plane (`{apiBaseUrl}/api/…`).
 */
export type CellFetchArgs = FetchArgs & {
  /** The name of the cell the call addresses; absent for the control plane. */
  readonly cell?: string | undefined;
};

/**
 * A base query whose base URL is resolved at request time from the loaded {@link RuntimeConfig}. The
 * session cookie and the BFF's anti-forgery header authenticate each call; the browser holds no token
 * and sends no `Authorization` header. A `401` answer means the BFF session has ended, so the query
 * dispatches `sessionEnded()`.
 */
export const dynamicBaseQuery: BaseQueryFn<
  string | CellFetchArgs,
  unknown,
  FetchBaseQueryError
> = async (args, api, extraOptions) => {
  const state = api.getState() as ApiRootState;
  const runtime = state.config.runtime;
  if (!runtime) {
    return {
      error: {
        status: 'CUSTOM_ERROR',
        error: 'Runtime configuration has not been loaded.',
      } satisfies FetchBaseQueryError,
    };
  }

  if (runtime.kind !== 'bff') {
    return {
      error: {
        status: 'CUSTOM_ERROR',
        error: 'The management API is called only from a surface its BFF serves.',
      } satisfies FetchBaseQueryError,
    };
  }

  const { name, value } = runtime.bff.antiforgeryHeader;
  const rawBaseQuery = fetchBaseQuery({
    baseUrl: `${runtime.apiBaseUrl}/`,
    credentials: 'same-origin',
    prepareHeaders: (headers) => {
      headers.set(name, value);
      return headers;
    },
  });

  const { cell, ...fetchArgs } = typeof args === 'string' ? { url: args } : args;
  const result = await rawBaseQuery(
    cell ? { ...fetchArgs, url: `cells/${encodeURIComponent(cell)}/${fetchArgs.url}` } : fetchArgs,
    api,
    extraOptions,
  );
  if (result.error?.status === 401) {
    api.dispatch(sessionEnded());
  }
  return result;
};
