# The Android build runner.
#
# ⚠️ Everything a build needs is baked in, and nothing is installed at build
# time. An image that downloads its own SDK on first use turns every cold build
# into a several-minute download, makes builds fail when a mirror is down, and
# — the part that matters — means the toolchain a customer's app was compiled
# with is whatever the network happened to serve that day. A pinned image is
# what makes "the same config produces the same artifact" true across weeks.
#
# arm64 because the Oracle Always Free host is Ampere. Verified in Sprint 00 and
# recorded in docs/ops/arm64-compat.md.
FROM eclipse-temurin:21-jdk-jammy

# Pinned. A floating "latest" here would silently change the compiler that
# builds customers' apps.
ARG ANDROID_CMDLINE_TOOLS_VERSION=11076708
ARG ANDROID_PLATFORM=android-36
ARG ANDROID_BUILD_TOOLS=36.0.0

ENV ANDROID_HOME=/opt/android-sdk \
    ANDROID_SDK_ROOT=/opt/android-sdk \
    GRADLE_USER_HOME=/home/builder/.gradle \
    JAVA_TOOL_OPTIONS="-Dfile.encoding=UTF-8"

RUN set -eux; \
    apt-get update; \
    apt-get install -y --no-install-recommends unzip ca-certificates; \
    rm -rf /var/lib/apt/lists/*

RUN set -eux; \
    mkdir -p "${ANDROID_HOME}/cmdline-tools"; \
    curl -fsSL -o /tmp/tools.zip \
      "https://dl.google.com/android/repository/commandlinetools-linux-${ANDROID_CMDLINE_TOOLS_VERSION}_latest.zip"; \
    unzip -q /tmp/tools.zip -d "${ANDROID_HOME}/cmdline-tools"; \
    mv "${ANDROID_HOME}/cmdline-tools/cmdline-tools" "${ANDROID_HOME}/cmdline-tools/latest"; \
    rm /tmp/tools.zip

ENV PATH="${ANDROID_HOME}/cmdline-tools/latest/bin:${ANDROID_HOME}/platform-tools:${PATH}"

RUN set -eux; \
    yes | sdkmanager --licenses > /dev/null; \
    sdkmanager --install "platform-tools" "platforms;${ANDROID_PLATFORM}" "build-tools;${ANDROID_BUILD_TOOLS}"

# ⚠️ A throwaway build, so the Gradle distribution and the dependencies every
# generated project shares are already in the image. Without it the first build
# on a cold container spends minutes downloading Gradle itself — which is the
# single largest component of a cold build time, and the one entirely within
# our control.
COPY warmup /tmp/warmup
RUN set -eux; \
    cd /tmp/warmup; \
    ./gradlew --no-daemon assembleDebug || true; \
    rm -rf /tmp/warmup

# ⚠️ A high, fixed id with no matching account. Nothing inside the container
# maps to a real user on the host, so a mount that escapes lands as nobody.
RUN set -eux; \
    groupadd --gid 10001 builder; \
    useradd --uid 10001 --gid 10001 --create-home builder; \
    chown -R 10001:10001 /home/builder

USER 10001:10001
WORKDIR /workspace

# No entrypoint. The orchestrator passes the command as an argument array, and
# an entrypoint that wrapped it in a shell would undo the reason for that.
