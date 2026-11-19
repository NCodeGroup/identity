// NCode Brand's two typefaces, so every story draws them as the apps do.
import '@ncodegroup/identity-brand/fonts.css';

import type { Preview } from '@storybook/react-vite';
import type { BrandingConfig } from '@ncodegroup/identity-config';
import { AppThemeProvider } from '../src/theme/AppThemeProvider.js';

// The tenants of NCode Brand's derivation vectors, so each story shows a tenant's colors as well as NCode's.
const TENANTS: Readonly<Record<string, BrandingConfig>> = {
  ncode: {},
  teal: { primaryColor: '#0F766E', secondaryColor: '#7C2D12' },
  yellow: { primaryColor: '#FACC15' },
};

const preview: Preview = {
  parameters: {
    controls: { matchers: { color: /(background|color)$/i, date: /Date$/i } },
  },
  globalTypes: {
    mode: {
      description: 'Color mode',
      toolbar: { title: 'Mode', icon: 'mirror', items: ['light', 'dark'], dynamicTitle: true },
    },
    tenant: {
      description: 'Tenant colors',
      toolbar: {
        title: 'Tenant',
        icon: 'paintbrush',
        items: [
          { value: 'ncode', title: 'NCode' },
          { value: 'teal', title: 'Teal, secondary rust' },
          { value: 'yellow', title: 'Yellow' },
        ],
        dynamicTitle: true,
      },
    },
  },
  initialGlobals: { mode: 'light', tenant: 'ncode' },
  decorators: [
    (Story, { globals }) => (
      <AppThemeProvider
        branding={{
          ...(TENANTS[String(globals['tenant'])] ?? {}),
          mode: globals['mode'] === 'dark' ? 'dark' : 'light',
        }}
      >
        <Story />
      </AppThemeProvider>
    ),
  ],
};

export default preview;
