import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { UnavailableCellsAlert } from './UnavailableCellsAlert.js';

describe('UnavailableCellsAlert', () => {
  it('renders nothing when every cell answered', () => {
    const { container } = render(<UnavailableCellsAlert cells={[]} />);

    expect(container).toBeEmptyDOMElement();
  });

  it('names each cell that did not answer', () => {
    render(<UnavailableCellsAlert cells={['eu-1', 'us-1']} />);

    const alert = screen.getByTestId('unavailable-cells');
    expect(alert).toHaveTextContent('The cell eu-1 did not answer, so its tenants are not listed.');
    expect(alert).toHaveTextContent('The cell us-1 did not answer, so its tenants are not listed.');
  });
});
