import { createSlice, type PayloadAction } from '@reduxjs/toolkit';
import type { RuntimeConfig } from '@ncodegroup/identity-config';

export type ConfigState = {
  readonly runtime: RuntimeConfig | null;
};

const initialState: ConfigState = { runtime: null };

export const configSlice = createSlice({
  name: 'config',
  initialState,
  reducers: {
    // Return a fresh state rather than mutating the Immer draft: RuntimeConfig is immutable and replaced wholesale, and
    // its readonly array fields don't satisfy the writable-draft type.
    runtimeConfigLoaded(_state, action: PayloadAction<RuntimeConfig>): ConfigState {
      return { runtime: action.payload };
    },
  },
});

export const { runtimeConfigLoaded } = configSlice.actions;
