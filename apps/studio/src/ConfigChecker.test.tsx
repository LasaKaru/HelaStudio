import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ConfigChecker } from './ConfigChecker.js';

describe('ConfigChecker', () => {
  // TC-S00-STU-001 — proves the validation engine runs unchanged in a browser.
  it('reports a clean configuration as ready to build', () => {
    render(<ConfigChecker />);
    expect(screen.getByText(/ready to build/i)).toBeDefined();
  });

  it('shows the three cache keys for a valid configuration', () => {
    render(<ConfigChecker />);
    expect(screen.getByText('Code key')).toBeDefined();
    expect(screen.getByText('Asset key')).toBeDefined();
    expect(screen.getByText('Content key')).toBeDefined();
  });
});
