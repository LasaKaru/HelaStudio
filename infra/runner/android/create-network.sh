#!/usr/bin/env bash
# Creates the egress-restricted network build containers attach to.
#
# ⚠️ Docker's own networking has no host allowlist, so this is enforced with
# iptables against the bridge. The rule order matters: established connections
# are allowed back, the five package hosts are allowed out, everything else is
# rejected — and the reject rule is last, because a default-deny that sits
# before the allows blocks everything and a default-deny that is missing blocks
# nothing.
set -euo pipefail

network="${SHELLWRIGHT_BUILD_NETWORK:-shellwright-build}"
subnet="${SHELLWRIGHT_BUILD_SUBNET:-172.31.240.0/24}"

allowed=(
	repo.maven.apache.org
	dl.google.com
	maven.google.com
	plugins.gradle.org
	services.gradle.org
)

if ! docker network inspect "$network" >/dev/null 2>&1; then
	docker network create --subnet "$subnet" "$network"
fi

bridge="br-$(docker network inspect -f '{{.Id}}' "$network" | cut -c1-12)"

# Start from a clean chain so re-running is idempotent rather than additive.
iptables -F SHELLWRIGHT_EGRESS 2>/dev/null || iptables -N SHELLWRIGHT_EGRESS
iptables -C DOCKER-USER -i "$bridge" -j SHELLWRIGHT_EGRESS 2>/dev/null \
	|| iptables -I DOCKER-USER -i "$bridge" -j SHELLWRIGHT_EGRESS

iptables -A SHELLWRIGHT_EGRESS -m conntrack --ctstate ESTABLISHED,RELATED -j ACCEPT

# DNS, or nothing resolves and every build fails on the first dependency.
iptables -A SHELLWRIGHT_EGRESS -p udp --dport 53 -j ACCEPT
iptables -A SHELLWRIGHT_EGRESS -p tcp --dport 53 -j ACCEPT

for host in "${allowed[@]}"; do
	# ⚠️ Resolved once, at network creation. A CDN address that changes later
	# breaks builds loudly rather than silently widening the allowlist, which is
	# the failure direction to prefer. Re-run this script after a change.
	for address in $(getent ahostsv4 "$host" | awk '{print $1}' | sort -u); do
		iptables -A SHELLWRIGHT_EGRESS -d "$address" -p tcp --dport 443 -j ACCEPT
	done
done

# ⚠️ Last, and REJECT rather than DROP. A dropped packet makes a build hang for
# its full timeout; a rejected one fails in a second with a message naming the
# host, which is the difference between a five-minute mystery and a one-line
# diagnosis.
iptables -A SHELLWRIGHT_EGRESS -j REJECT --reject-with icmp-admin-prohibited

echo "Network $network is up on $bridge with ${#allowed[@]} allowed hosts."
