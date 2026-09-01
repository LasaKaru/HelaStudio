#!/usr/bin/env bash
# Runs what CI runs, the way CI runs it.
#
# ⚠️ This script exists because "I ran the checks locally" was true four times
# while CI still went red. Each time the tool was right and the invocation was
# wrong:
#
#   - gitleaks was run with --no-git, which scans the working tree. CI scans
#     git *history*, where a path that moved yesterday still exists.
#   - vitest was run from the repository root, which silently drops the studio's
#     own config, loses jsdom, and fails its tests with `document is not
#     defined` — a failure in the invocation that looks exactly like a
#     regression.
#   - commitlint was not run at all, because it only fires on pull requests.
#
# Adding a check here is cheaper than another red pipeline. If you find a way
# for CI to disagree with this script, that is a bug in this script.
set -uo pipefail

failed=0
skipped=()

step() {
	printf '\n\033[1m▸ %s\033[0m\n' "$1"
}

check() {
	local name="$1"
	shift

	if "$@"; then
		printf '  \033[32m✓ %s\033[0m\n' "$name"
	else
		printf '  \033[31m✗ %s\033[0m\n' "$name"
		failed=1
	fi
}

# A missing toolchain is reported, never silently passed over. A skip you did
# not notice is how a broken push happens.
have() {
	if command -v "$1" >/dev/null 2>&1; then
		return 0
	fi

	skipped+=("$2")
	return 1
}

step "Formatting and lint"
check "prettier"  pnpm format:check
check "eslint"    pnpm lint
check "typecheck" pnpm typecheck

step "Commit messages"
# ⚠️ The range CI checks is base..head, so a subject that broke the convention
# three commits ago still fails the build today. Checking only the tip is how
# that gets missed.
if have npx commitlint; then
	base=$(git merge-base origin/main HEAD 2>/dev/null || echo "")

	if [ -n "$base" ]; then
		bad=0

		for commit in $(git log --format="%H" "$base"..HEAD); do
			if ! git log -1 --format="%B" "$commit" | npx --no-install commitlint >/dev/null 2>&1; then
				printf '  \033[31m✗ %s\033[0m\n' "$(git log -1 --format='%s' "$commit")"
				bad=1
			fi
		done

		[ "$bad" -eq 0 ] && printf '  \033[32m✓ commitlint (%s commits)\033[0m\n' \
			"$(git rev-list --count "$base"..HEAD)" || failed=1
	else
		skipped+=("commitlint — no merge base with origin/main")
	fi
fi

step "Tests"
# ⚠️ Through turbo, never `vitest` from the root: the studio's tests need the
# jsdom environment its own vitest.config.ts sets.
check "node suites" pnpm test

if have dotnet ".NET — dotnet not on PATH"; then
	check "dotnet format" dotnet format --verify-no-changes
	# Release, not Debug: analyser rules and package licence gates only bite here.
	check "dotnet build"  dotnet build -c Release
	check "dotnet test"   dotnet test -c Release --no-build
fi

step "Secrets"
# ⚠️ No --no-git. CI scans history, and an allowlist pinned to today's paths
# stops matching yesterday's commits.
if have gitleaks "gitleaks — binary not installed"; then
	check "gitleaks (history)" gitleaks detect --source . --config .gitleaks.toml --no-banner
fi

step "Shells"
if [ -f shells/android/gradlew ] && have java "Android — java not on PATH"; then
	check "android" sh -c 'cd shells/android && ./gradlew testDebugUnitTest lintDebug --console=plain -q'
fi

if have swift "iOS ShellCore — swift not on PATH"; then
	check "swift" sh -c 'cd shells/ios && swift test --no-parallel'
fi

if [ ${#skipped[@]} -gt 0 ]; then
	step "Not checked here"
	for item in "${skipped[@]}"; do
		printf '  \033[33m• %s\033[0m\n' "$item"
	done
	printf '\n  These still run in CI. A local pass is only as complete as this list is short.\n'
fi

printf '\n'

if [ "$failed" -eq 0 ]; then
	printf '\033[32mAll checks passed.\033[0m\n'
else
	printf '\033[31mSomething failed. CI will agree.\033[0m\n'
fi

exit "$failed"
