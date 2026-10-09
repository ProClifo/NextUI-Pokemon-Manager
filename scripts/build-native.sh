#!/usr/bin/env bash
# Builds the pkmgr-box viewer and a patched minui-list (see native/Makefile) for each NextUI platform
# inside its toolchain container.
#
#   scripts/build-native.sh [platform...]     default: tg5040 tg5050 my355 h700
#
# Output: native/pkmgr-box-<platform>-nextui and native/minui-list-<platform>-nextui. Needs Docker; on x86 hosts the arm64 toolchain
# images run through QEMU (binfmt). Sources are cloned on the host, so the containers need no network.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
NATIVE="$ROOT/native"
PLATFORMS=("$@")
[ ${#PLATFORMS[@]} -eq 0 ] && PLATFORMS=(tg5040 tg5050 my355 h700)

status=0
for platform in "${PLATFORMS[@]}"; do
    target="$platform-nextui"
    echo "==> pkmgr-box and minui-list for $platform"
    make -s -C "$NATIVE" PLATFORM="$target" nextui include/parson minui-list-src
    if ! docker run --rm --platform linux/arm64 -v "$NATIVE":/root/workspace/repo "savant/minui-toolchain:$target" \
        bash -c "source /root/.bashrc && make -C /root/workspace/repo PLATFORM=$target device minui-list"; then
        echo "!! build failed for $platform" >&2
        status=1
    fi
done
exit $status
