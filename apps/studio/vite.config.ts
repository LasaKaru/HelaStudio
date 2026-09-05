import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  build: {
    // 03_TEST_STRATEGY.md §12 budgets the initial bundle at 200 KB gzipped.
    // Reporting the real number on every build keeps that honest.
    reportCompressedSize: true,
    target: 'es2022',
  },
  test: {
    environment: 'jsdom',
    // Testing Library registers its between-test DOM cleanup on the global
    // afterEach; without globals the previous test's markup stays mounted and
    // queries match twice.
    globals: true,
    include: ['src/**/*.test.tsx', 'src/**/*.test.ts'],
    coverage: {
      provider: 'v8',
      reporter: ['text', 'cobertura'],
      include: ['src/**/*.{ts,tsx}'],
      // 03_TEST_STRATEGY.md §6 raises this to 70% at S12, when the studio has
      // real screens. Until then the gate exists but does not pretend.
      thresholds: { lines: 60, functions: 60, statements: 60 },
    },
  },
});
