/** Conventional Commits — drives the changelog and semver. See 00_MASTER_SPRINT_PLAN §9. */
export default {
  extends: ['@commitlint/config-conventional'],
  rules: {
    'type-enum': [
      2,
      'always',
      ['feat', 'fix', 'chore', 'perf', 'test', 'docs', 'refactor', 'build', 'ci', 'revert'],
    ],
    'scope-case': [2, 'always', 'kebab-case'],
    'subject-max-length': [2, 'always', 100],
  },
};
