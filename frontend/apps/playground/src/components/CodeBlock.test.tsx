import type { ReactElement } from 'react';
import { render } from '@testing-library/react';
import { ThemeProvider } from '@mui/material/styles';
import { createAppTheme } from '@ncodegroup/identity-ui';
import { describe, expect, it } from 'vitest';
import { CodeBlock } from './CodeBlock.js';

const theme = createAppTheme();

function renderBlock(element: ReactElement) {
  return render(<ThemeProvider theme={theme}>{element}</ThemeProvider>);
}

describe('CodeBlock', () => {
  it('renders the value and an optional label', () => {
    const { getByText, getByTestId } = renderBlock(
      <CodeBlock label="Authorize URL" value="https://op.test/authorize?x=1" />,
    );
    expect(getByText('Authorize URL')).toBeInTheDocument();
    expect(getByTestId('code-block').textContent).toBe('https://op.test/authorize?x=1');
  });

  it('renders only the block when no label is given', () => {
    const { getByTestId } = renderBlock(<CodeBlock value="opaque-token" />);
    const block = getByTestId('code-block');
    expect(block).toHaveTextContent('opaque-token');
    expect(block.parentElement?.children).toHaveLength(1);
  });

  it('tints a sensitive value with the warning wash and border', () => {
    const { getByTestId } = renderBlock(<CodeBlock value="refresh-token" sensitive />);
    expect(getByTestId('code-block')).toHaveStyle({
      backgroundColor: theme.palette.instrument['warning-subtle'],
      borderTopColor: theme.palette.instrument['warning-border'],
    });
  });

  it('renders a non-sensitive value with the neutral divider border', () => {
    const { getByTestId } = renderBlock(<CodeBlock value="https://op.test" />);
    expect(getByTestId('code-block')).toHaveStyle({ borderTopColor: theme.palette.divider });
  });

  it('sets the value in the code font', () => {
    const { getByTestId } = renderBlock(<CodeBlock value="opaque-token" />);
    expect(getByTestId('code-block')).toHaveStyle({ fontFamily: theme.typography.fontFamilyMono });
  });
});
