# arm64 compatibility

The Oracle Always Free host is ARM. Every container in the stack must have an
arm64 image, and a gap changes the architecture — so it is found in Sprint 00
rather than in Sprint 07.

Fill this in while working T-00.5 step 4. Verify by running the image and
executing the command, not by reading a registry page: a manifest listing
`linux/arm64` is not proof the image works.

| Image                                  | Expected | Verify with         | Result | Date |
| -------------------------------------- | -------- | ------------------- | ------ | ---- |
| `mcr.microsoft.com/dotnet/aspnet:10.0` | yes      | `dotnet --version`  | ☐      |      |
| `postgres:17`                          | yes      | `psql --version`    | ☐      |      |
| `valkey/valkey:8`                      | yes      | `valkey-cli ping`   | ☐      |      |
| `temporalio/auto-setup`                | yes      | web UI loads        | ☐      |      |
| `caddy:2`                              | yes      | `caddy version`     | ☐      |      |
| Android SDK cmdline-tools + JDK 21     | yes      | `sdkmanager --list` | ☐      |      |
| `ghcr.io/gitleaks/gitleaks`            | yes      | `gitleaks version`  | ☐      |      |

## If something is missing

In order of preference: substitute an equivalent component; build the image for
arm64 yourself; or move that one service to an x86 host and record the cost in
`COSTS.md`. Do not silently drop the dependency — note the decision here.

## Known notes

_(Record anything surprising as you find it. This section is worth more than
the table above, because it is the part nobody else has written down.)_
