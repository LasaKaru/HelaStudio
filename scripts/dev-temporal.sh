#!/usr/bin/env bash
# Installs the Temporal CLI, which the orchestrator's tests start a dev server
# from.
#
# ⚠️ Installed rather than downloaded on demand. The .NET SDK will fetch its own
# copy — about 40 MB — the first time a test asks for a local server, which
# means every CI run pays for it, the suite cannot run offline, and a bad day at
# the download host presents as a test failure. TemporalFixture prefers a binary
# already on PATH.
set -euo pipefail

version="${TEMPORAL_CLI_VERSION:-latest}"
destination="${TEMPORAL_CLI_DESTINATION:-/usr/local/bin}"

if command -v temporal >/dev/null 2>&1; then
	echo "temporal is already installed: $(temporal --version)"
	exit 0
fi

case "$(uname -m)" in
	x86_64) arch=amd64 ;;
	aarch64 | arm64) arch=arm64 ;;
	*) echo "Unsupported architecture: $(uname -m)"; exit 1 ;;
esac

case "$(uname -s)" in
	Linux) platform=linux ;;
	Darwin) platform=darwin ;;
	*) echo "Unsupported platform: $(uname -s)"; exit 1 ;;
esac

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

curl -fsSL -o "$work/temporal.tar.gz" \
	"https://temporal.download/cli/archive/${version}?platform=${platform}&arch=${arch}"

tar -xzf "$work/temporal.tar.gz" -C "$work" temporal
install -m 0755 "$work/temporal" "$destination/temporal"

echo "Installed $(temporal --version)"
