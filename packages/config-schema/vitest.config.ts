import { existsSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { defineConfig, type Plugin } from 'vitest/config';

/**
 * TypeScript's NodeNext resolution requires `.js` in relative imports, but Vite
 * resolves paths literally. This maps `./foo.js` back to `./foo.ts` on disk so
 * one import style works for both the compiler and the test runner.
 */
function resolveTsFromJs(): Plugin {
  return {
    name: 'resolve-ts-from-js',
    enforce: 'pre',
    resolveId(source, importer) {
      if (importer === undefined || !source.startsWith('.') || !source.endsWith('.js')) return null;
      const candidate = resolve(dirname(importer), source.replace(/\.js$/, '.ts'));
      return existsSync(candidate) ? candidate : null;
    },
  };
}

export default defineConfig({
  plugins: [resolveTsFromJs()],
  test: {
    include: ['test/**/*.test.ts'],
    benchmark: { include: ['test/**/*.bench.ts'] },
    coverage: {
      provider: 'v8',
      reporter: ['text', 'cobertura', 'html'],
      include: ['src/**/*.ts'],
      exclude: [
        'src/generated/**',
        // Barrels and type-only modules: re-exports and interfaces compile to
        // nothing executable, so they only dilute the figure.
        'src/index.ts',
        'src/rules/index.ts',
        'src/rules/rule.ts',
      ],
      // 03_TEST_STRATEGY.md §6 — enforced from S01 for this package.
      thresholds: { lines: 95, branches: 90, functions: 95, statements: 95 },
    },
  },
});
