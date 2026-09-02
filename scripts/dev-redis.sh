#!/usr/bin/env bash
# Starts a Redis for the build log pipeline's tests.
#
# The pipeline's interesting behaviour — batched XADD, approximate stream
# trimming, resuming from a stream id — is Redis behaviour. A fake would agree
# with whatever the tests claimed, so they run against a real server.
set -uo pipefail

port="${SHELLWRIGHT_REDIS_PORT:-56379}"
data="${SHELLWRIGHT_REDIS_DATA:-${TMPDIR:-/tmp}/shellwright-redis-$(id -u)}"

say() { printf '%s\n' "$1" >&2; }

if [[ "${1:-}" == "--stop" ]]; then
	redis-cli -p "$port" shutdown nosave 2>/dev/null && say "Stopped." || say "Not running."
	exit 0
fi

command -v redis-server >/dev/null || { say "redis-server is not installed."; exit 1; }

if redis-cli -p "$port" ping >/dev/null 2>&1; then
	echo "export SHELLWRIGHT_TEST_REDIS='127.0.0.1:$port'"
	say "Already running on port $port"
	exit 0
fi

mkdir -p "$data"

# ⚠️ No persistence. This is a cache for live log lines; the durable record is
# the archive on disk. Saving to disk would slow every test and protect nothing.
redis-server \
	--port "$port" \
	--bind 127.0.0.1 \
	--save '' \
	--appendonly no \
	--dir "$data" \
	--daemonize yes \
	--logfile "$data/redis.log"

for _ in $(seq 1 30); do
	redis-cli -p "$port" ping >/dev/null 2>&1 && break
	sleep 1
done

redis-cli -p "$port" ping >/dev/null 2>&1 || { say "Redis did not start; see $data/redis.log"; exit 1; }

echo "export SHELLWRIGHT_TEST_REDIS='127.0.0.1:$port'"
say "Ready on 127.0.0.1:$port"
