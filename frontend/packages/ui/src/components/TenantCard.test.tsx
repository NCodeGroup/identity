import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ThemeProvider } from '@mui/material/styles';
import { deriveBrand } from '@ncodegroup/identity-brand/derive';
import { createAppTheme } from '../theme/createAppTheme.js';
import { TenantCard } from './TenantCard.js';

function rgb(hex: string): string {
  const [r, g, b] = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16));
  return `rgb(${String(r)}, ${String(g)}, ${String(b)})`;
}

describe('TenantCard', () => {
  it('shows its children on a raised card', () => {
    render(
      <ThemeProvider theme={createAppTheme()}>
        <TenantCard maxWidth={400}>Sign in</TenantCard>
      </ThemeProvider>,
    );

    const card = screen.getByText('Sign in');
    expect(card).toBeInTheDocument();
    expect(card).toHaveClass('MuiPaper-elevation4');
  });

  it('draws a brace of each size on both sides', () => {
    const { container } = render(
      <ThemeProvider theme={createAppTheme()}>
        <TenantCard maxWidth={400}>Sign in</TenantCard>
      </ThemeProvider>,
    );
    const widths = Array.from(container.querySelectorAll('svg[aria-hidden="true"]'), (s) =>
      s.getAttribute('width'),
    );

    expect(widths).toHaveLength(4);
    expect(widths.filter((w) => w === '17')).toHaveLength(2);
    expect(widths.filter((w) => w === '23')).toHaveLength(2);
  });

  it("inks the braces in the tenant's scope color", () => {
    const { container } = render(
      <ThemeProvider
        theme={createAppTheme({
          primaryColor: '#0F766E',
          secondaryColor: '#7C2D12',
          mode: 'light',
        })}
      >
        <TenantCard maxWidth={400}>Sign in</TenantCard>
      </ThemeProvider>,
    );
    const expected = rgb(deriveBrand('#0F766E', '#7C2D12').light['scope-tenant']);

    expect(getComputedStyle(container.firstElementChild as Element).color).toBe(expected);
  });
});
