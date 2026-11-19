import { describe, expect, it } from 'vitest';
import { render } from '@testing-library/react';
import { AppThemeProvider } from '@ncodegroup/identity-ui';
import { App } from './App.js';
import { EnvironmentBanner } from './EnvironmentBanner.js';

describe('EnvironmentBanner', () => {
  it('shows the environment name when present', () => {
    const { getByText } = render(
      <AppThemeProvider>
        <EnvironmentBanner environment={{ type: 'staging', name: 'Pre-prod' }} />
      </AppThemeProvider>,
    );

    expect(getByText('Pre-prod environment — not for production use')).toBeInTheDocument();
  });

  it('falls back to the environment type without a name', () => {
    const { getByText } = render(
      <AppThemeProvider>
        <EnvironmentBanner environment={{ type: 'staging' }} />
      </AppThemeProvider>,
    );

    expect(getByText('staging environment — not for production use')).toBeInTheDocument();
  });

  it('draws the banner as a hazard band', () => {
    const { getByText } = render(
      <AppThemeProvider>
        <EnvironmentBanner environment={{ type: 'staging' }} />
      </AppThemeProvider>,
    );

    const band = getByText('staging environment — not for production use').parentElement;
    expect(band).not.toBeNull();
    if (!band) return;
    expect(getComputedStyle(band).backgroundColor).toBe('rgb(244, 173, 29)');
  });
});

describe('App environment banner', () => {
  it('shows the banner for a tenant with a non-production environment', () => {
    const { getByText } = render(
      <AppThemeProvider>
        <App
          pathname="/account/login"
          search=""
          cookie=""
          branding={{ environment: { type: 'development' } }}
        />
      </AppThemeProvider>,
    );

    expect(getByText('development environment — not for production use')).toBeInTheDocument();
  });
});
