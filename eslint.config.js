// Standards enforcement: 01_ENGINEERING_STANDARDS.md §3.
// strict-type-checked + import layering. No `any`, no default exports outside pages.
import js from '@eslint/js';
import tseslint from 'typescript-eslint';
import importPlugin from 'eslint-plugin-import';

export default tseslint.config(
  {
    ignores: [
      '**/dist/**',
      '**/coverage/**',
      '**/node_modules/**',
      '**/.turbo/**',
      '**/src/generated/**',
      'eslint.config.js',
      'commitlint.config.js',
    ],
  },
  js.configs.recommended,
  ...tseslint.configs.strictTypeChecked,
  ...tseslint.configs.stylisticTypeChecked,
  {
    languageOptions: {
      parserOptions: { projectService: true, tsconfigRootDir: import.meta.dirname },
    },
    plugins: { import: importPlugin },
    rules: {
      '@typescript-eslint/no-explicit-any': 'error',
      '@typescript-eslint/explicit-module-boundary-types': 'error',
      '@typescript-eslint/consistent-type-imports': ['error', { prefer: 'type-imports' }],
      '@typescript-eslint/no-unnecessary-condition': 'error',
      // §4: never swallow an error.
      'no-empty': ['error', { allowEmptyCatch: false }],
      // The codebase enables noUncheckedIndexedAccess deliberately. This rule
      // pushes `x[i] as T` toward `x[i]!`, which is the same assertion in fewer
      // characters and reads as if the check were unnecessary. Prefer the
      // explicit cast, or better, real narrowing.
      '@typescript-eslint/non-nullable-type-assertion-style': 'off',
      // A leading underscore marks a binding destructured only to omit it.
      '@typescript-eslint/no-unused-vars': [
        'error',
        { argsIgnorePattern: '^_', varsIgnorePattern: '^_', caughtErrors: 'none' },
      ],
      // §3: max function length 60, max cyclomatic complexity 10.
      'max-lines-per-function': ['error', { max: 60, skipBlankLines: true, skipComments: true }],
      complexity: ['error', 10],
      'no-console': ['error', { allow: ['warn', 'error'] }],
      'import/no-cycle': 'error',
      'import/no-default-export': 'error',
      eqeqeq: ['error', 'always'],
    },
  },
  {
    // Tests describe behaviour; length and complexity caps get in the way there.
    files: ['**/*.test.ts', '**/*.spec.ts', '**/*.bench.ts', '**/test/**/*.ts'],
    rules: {
      'max-lines-per-function': 'off',
      complexity: 'off',
      '@typescript-eslint/no-non-null-assertion': 'off',
      '@typescript-eslint/no-unsafe-assignment': 'off',
      '@typescript-eslint/no-unsafe-member-access': 'off',
    },
  },
  {
    files: ['**/vitest.config.ts', '**/vite.config.ts', '**/*.config.ts', '**/scripts/**/*.ts'],
    rules: { 'import/no-default-export': 'off', 'no-console': 'off' },
  },
);
