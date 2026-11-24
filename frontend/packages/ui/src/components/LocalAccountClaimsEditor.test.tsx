import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { LocalAccountClaimsEditor } from './LocalAccountClaimsEditor.js';

const claims = [
  { type: 'locale', value: 'en-US' },
  { type: 'zoneinfo', value: 'UTC' },
];

describe('LocalAccountClaimsEditor', () => {
  it('disables save until the rows differ', () => {
    render(<LocalAccountClaimsEditor claims={claims} onSave={vi.fn()} />);
    expect(screen.getByTestId('claims-save')).toBeDisabled();
    fireEvent.change(screen.getByTestId('claim-value-0'), { target: { value: 'de-DE' } });
    expect(screen.getByTestId('claims-save')).toBeEnabled();
    fireEvent.change(screen.getByTestId('claim-value-0'), { target: { value: 'en-US' } });
    expect(screen.getByTestId('claims-save')).toBeDisabled();
  });

  it('adds a claim and saves the trimmed rows', () => {
    const onSave = vi.fn();
    render(<LocalAccountClaimsEditor claims={claims} onSave={onSave} />);
    fireEvent.click(screen.getByTestId('claim-add'));
    expect(screen.getByTestId('claims-save')).toBeDisabled();
    fireEvent.change(screen.getByTestId('claim-type-2'), { target: { value: ' nickname ' } });
    fireEvent.change(screen.getByTestId('claim-value-2'), { target: { value: ' Al ' } });
    fireEvent.click(screen.getByTestId('claims-save'));
    expect(onSave).toHaveBeenCalledWith([...claims, { type: 'nickname', value: 'Al' }]);
  });

  it('edits a claim', () => {
    const onSave = vi.fn();
    render(<LocalAccountClaimsEditor claims={claims} onSave={onSave} />);
    fireEvent.change(screen.getByTestId('claim-value-1'), { target: { value: 'Europe/Paris' } });
    fireEvent.click(screen.getByTestId('claims-save'));
    expect(onSave).toHaveBeenCalledWith([claims[0], { type: 'zoneinfo', value: 'Europe/Paris' }]);
  });

  it('removes a claim', () => {
    const onSave = vi.fn();
    render(<LocalAccountClaimsEditor claims={claims} onSave={onSave} />);
    fireEvent.click(screen.getByLabelText('Remove claim 1'));
    fireEvent.click(screen.getByTestId('claims-save'));
    expect(onSave).toHaveBeenCalledWith([claims[1]]);
  });

  it('refuses the name claim', () => {
    const onSave = vi.fn();
    render(<LocalAccountClaimsEditor claims={claims} onSave={onSave} />);
    fireEvent.change(screen.getByTestId('claim-type-0'), { target: { value: 'name' } });
    expect(
      screen.getByText('The display name is the name claim; edit it with the user.'),
    ).toBeInTheDocument();
    expect(screen.getByTestId('claims-save')).toBeDisabled();
    fireEvent.click(screen.getByTestId('claims-save'));
    expect(onSave).not.toHaveBeenCalled();
  });

  it('disables save while saving and surfaces a server error', () => {
    render(<LocalAccountClaimsEditor claims={claims} isSaving error="Conflict" onSave={vi.fn()} />);
    fireEvent.change(screen.getByTestId('claim-value-0'), { target: { value: 'de-DE' } });
    expect(screen.getByTestId('claims-save')).toBeDisabled();
    expect(screen.getByTestId('claims-error')).toHaveTextContent('Conflict');
  });

  it('reseeds when the claims change', () => {
    const { rerender } = render(<LocalAccountClaimsEditor claims={claims} onSave={vi.fn()} />);
    fireEvent.change(screen.getByTestId('claim-value-0'), { target: { value: 'de-DE' } });
    const saved = [{ type: 'locale', value: 'de-DE' }];
    rerender(<LocalAccountClaimsEditor claims={saved} onSave={vi.fn()} />);
    expect(screen.queryByTestId('claim-type-1')).not.toBeInTheDocument();
    expect(screen.getByTestId('claim-value-0')).toHaveValue('de-DE');
    expect(screen.getByTestId('claims-save')).toBeDisabled();
  });
});
