#!/usr/bin/env bash
# Brings up a Postgres suitable for running the API's integration tests, and
# prints the two connection strings they need.
#
# The control plane's isolation guarantee is a *database* guarantee: row-level
# security, a migration role that owns the schema, and an application role that
# owns nothing and cannot bypass a policy. None of that can be exercised against
# an in-memory provider or a mock, so the tests need a real server. This script
# is how you get one without installing anything global.
#
#   eval "$(bash scripts/dev-postgres.sh)"     # start, print exports
#   bash scripts/dev-postgres.sh --stop        # shut it down
#
# In CI, where Postgres is already running as a service container, set
# SHELLWRIGHT_PG_ADMIN to its superuser connection string and this script only
# creates the roles and database.
set -uo pipefail

port="${SHELLWRIGHT_PG_PORT:-55432}"
data="${SHELLWRIGHT_PG_DATA:-/var/lib/postgresql/shellwright-test}"
database="${SHELLWRIGHT_PG_DATABASE:-shellwright_test}"

say() { printf '%s\n' "$1" >&2; }

find_bindir() {
	if command -v initdb >/dev/null 2>&1; then
		dirname "$(command -v initdb)"
		return 0
	fi

	# Debian and Ubuntu keep the server binaries off PATH on purpose, so that
	# several major versions can be installed side by side.
	local candidate
	candidate=$(find /usr/lib/postgresql -maxdepth 3 -name initdb -type f 2>/dev/null | sort -V | tail -1)
	if [[ -n "$candidate" ]]; then
		dirname "$candidate"
		return 0
	fi

	return 1
}

# Postgres refuses to run as root, which is exactly the environment a container
# hands you. Run the server as the unprivileged `postgres` account when we have
# to, and as the caller otherwise.
run_pg() {
	if [[ "$(id -u)" -eq 0 ]]; then
		su postgres -s /bin/bash -c "$1"
	else
		bash -c "$1"
	fi
}

stop() {
	local bindir
	bindir=$(find_bindir) || { say "No Postgres server binaries found."; exit 1; }
	run_pg "$bindir/pg_ctl -D $data stop -m fast" >&2 2>/dev/null \
		&& say "Stopped." \
		|| say "Not running."
}

if [[ "${1:-}" == "--stop" ]]; then
	stop
	exit 0
fi

admin="${SHELLWRIGHT_PG_ADMIN:-}"

if [[ -z "$admin" ]]; then
	bindir=$(find_bindir) || {
		say "No Postgres server binaries found. Install postgresql-16 or set SHELLWRIGHT_PG_ADMIN."
		exit 1
	}

	if ! run_pg "$bindir/pg_ctl -D $data status" >/dev/null 2>&1; then
		if [[ ! -s "$data/PG_VERSION" ]]; then
			say "Initialising a cluster in $data"
			mkdir -p "$data"
			[[ "$(id -u)" -eq 0 ]] && chown postgres:postgres "$data"
			chmod 700 "$data"
			run_pg "$bindir/initdb -D $data -U postgres --auth=trust -E UTF8 --locale=C" >/dev/null 2>&1 || {
				say "initdb failed."
				exit 1
			}
		fi

		say "Starting Postgres on port $port"
		run_pg "$bindir/pg_ctl -D $data -o '-p $port -k /tmp -c listen_addresses=127.0.0.1' -l $data/server.log start" >/dev/null 2>&1 || {
			say "pg_ctl failed; see $data/server.log"
			exit 1
		}
	fi

	admin="Host=127.0.0.1;Port=$port;Database=postgres;Username=postgres"
fi

# psql wants a URI, the .NET side wants a keyword string. Parse the few fields
# we care about rather than depending on a converter.
field() { sed -n "s/.*[;^]\?$1=\([^;]*\).*/\1/Ip" <<<"$admin" | head -1; }
host=$(field Host); host="${host:-127.0.0.1}"
pgport=$(field Port); pgport="${pgport:-5432}"
adminuser=$(field Username); adminuser="${adminuser:-postgres}"
adminpass=$(field Password)

export PGPASSWORD="$adminpass"
psql_admin() { psql -h "$host" -p "$pgport" -U "$adminuser" -d postgres -v ON_ERROR_STOP=1 -qtA "$@"; }

for _ in $(seq 1 30); do
	psql_admin -c 'SELECT 1' >/dev/null 2>&1 && break
	sleep 1
done

if ! psql_admin -c 'SELECT 1' >/dev/null 2>&1; then
	say "Could not reach Postgres at $host:$pgport."
	exit 1
fi

# ⚠️ The two roles are the point of this script, not an incidental detail.
#
#   shellwright_migrator owns every table and applies every migration.
#   shellwright_app owns nothing, holds no BYPASSRLS, and is what the API runs
#   as. A deployment that collapses these two into one looks identical from the
#   outside and has no tenant isolation whatsoever, because a table's owner is
#   not subject to its own policies.
psql_admin <<SQL >/dev/null
DO \$\$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'shellwright_migrator') THEN
        CREATE ROLE shellwright_migrator LOGIN PASSWORD 'shellwright_migrator' NOBYPASSRLS;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'shellwright_app') THEN
        CREATE ROLE shellwright_app LOGIN PASSWORD 'shellwright_app' NOBYPASSRLS NOINHERIT;
    END IF;
END
\$\$;
SQL

if [[ "$(psql_admin -c "SELECT count(*) FROM pg_database WHERE datname = '$database'")" == "0" ]]; then
	psql_admin -c "CREATE DATABASE $database OWNER shellwright_migrator" >/dev/null
fi

# The migrator owns the schema; the application role may use it and nothing more.
# CREATE on the public schema is revoked so that a compromised application role
# cannot define a table of its own — one with no policy on it.
psql -h "$host" -p "$pgport" -U "$adminuser" -d "$database" -v ON_ERROR_STOP=1 -qtA >/dev/null <<SQL
ALTER SCHEMA public OWNER TO shellwright_migrator;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO shellwright_app;
SQL

base="Host=$host;Port=$pgport;Database=$database"
echo "export SHELLWRIGHT_TEST_PG_APP='$base;Username=shellwright_app;Password=shellwright_app'"
echo "export SHELLWRIGHT_TEST_PG_MIGRATOR='$base;Username=shellwright_migrator;Password=shellwright_migrator'"
echo "export SHELLWRIGHT_TEST_PG_ADMIN='Host=$host;Port=$pgport;Database=postgres;Username=$adminuser${adminpass:+;Password=$adminpass}'"
say "Ready: $database on $host:$pgport"
