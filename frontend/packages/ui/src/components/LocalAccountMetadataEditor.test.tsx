import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { LocalAccountMetadataEditor } from './LocalAccountMetadataEditor.js';

const profileJson = '{"plan":"pro"}';
const systemJson = '{"risk":3}';

describe('LocalAccountMetadataEditor', () => {
  it('disables save until a field differs', () => {
    render(
      <LocalAccountMetadataEditor
        profileJson={profileJson}
        systemJson={systemJson}
        onSave={vi.fn()}
      />,
    );
    expect(screen.getByTestId('metadata-save')).toBeDisabled();
    expect(screen.getByText('Server-controlled; never writable by the user.')).toBeInTheDocument();
    fireEvent.change(screen.getByTestId('metadata-profile'), { target: { value: '{}' } });
    expect(screen.getByTestId('metadata-save')).toBeEnabled();
  });

  it.each(['not json', '[]', 'null', '3'])('refuses %s as a profile', (text) => {
    render(
      <LocalAccountMetadataEditor
        profileJson={profileJson}
        systemJson={systemJson}
        onSave={vi.fn()}
      />,
    );
    fireEvent.change(screen.getByTestId('metadata-profile'), { target: { value: text } });
    expect(screen.getByText('Must be a JSON object.')).toBeInTheDocument();
    expect(screen.getByTestId('metadata-save')).toBeDisabled();
  });

  it('refuses a system document that is not an object', () => {
    render(
      <LocalAccountMetadataEditor
        profileJson={profileJson}
        systemJson={systemJson}
        onSave={vi.fn()}
      />,
    );
    fireEvent.change(screen.getByTestId('metadata-system'), { target: { value: '[1]' } });
    expect(screen.getByText('Must be a JSON object.')).toBeInTheDocument();
    expect(screen.getByTestId('metadata-save')).toBeDisabled();
  });

  it('saves both parsed objects', () => {
    const onSave = vi.fn();
    render(
      <LocalAccountMetadataEditor
        profileJson={profileJson}
        systemJson={systemJson}
        onSave={onSave}
      />,
    );
    fireEvent.change(screen.getByTestId('metadata-system'), { target: { value: '{"risk":9}' } });
    fireEvent.click(screen.getByTestId('metadata-save'));
    expect(onSave).toHaveBeenCalledWith({ plan: 'pro' }, { risk: 9 });
  });

  it('disables save while saving and surfaces a server error', () => {
    render(
      <LocalAccountMetadataEditor
        profileJson={profileJson}
        systemJson={systemJson}
        isSaving
        error="Conflict"
        onSave={vi.fn()}
      />,
    );
    fireEvent.change(screen.getByTestId('metadata-profile'), { target: { value: '{}' } });
    expect(screen.getByTestId('metadata-save')).toBeDisabled();
    expect(screen.getByTestId('metadata-error')).toHaveTextContent('Conflict');
  });

  it('reseeds when the props change', () => {
    const { rerender } = render(
      <LocalAccountMetadataEditor
        profileJson={profileJson}
        systemJson={systemJson}
        onSave={vi.fn()}
      />,
    );
    fireEvent.change(screen.getByTestId('metadata-profile'), { target: { value: '{}' } });
    rerender(
      <LocalAccountMetadataEditor profileJson="{}" systemJson='{"risk":4}' onSave={vi.fn()} />,
    );
    expect(screen.getByTestId('metadata-profile')).toHaveValue('{}');
    expect(screen.getByTestId('metadata-system')).toHaveValue('{"risk":4}');
    expect(screen.getByTestId('metadata-save')).toBeDisabled();
  });
});
