#!/usr/bin/env bash
# Builds the pkmgr-box viewer and a patched minui-list (see native/Makefile) for each NextUI platform
# inside its toolchain container.
#
#   scripts/build-native.sh [platform...]     default: tg5040 tg5050 my355 h700 trimuismart
#
# Output: native/pkmgr-box-, minui-list- and minui-presenter-<platform>-nextui (native/minui-list-trimuismart for MinUI's
# TrimUI Smart). Needs Docker; on x86 hosts the arm64 toolchain
# images run through QEMU (binfmt). Sources are cloned on the host, so the containers need no network.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
NATIVE="$ROOT/native"
PLATFORMS=("$@")
[ ${#PLATFORMS[@]} -eq 0 ] && PLATFORMS=(tg5040 tg5050 my355 h700 trimuismart)

status=0
for platform in "${PLATFORMS[@]}"; do
    if [ "$platform" = trimuismart ]; then
        # MinUI's TrimUI Smart: just minui-list (patched), built by its own Makefile against MinUI in MinUI's
        # toolchain (savant/minui-toolchain, as upstream's releases are). No PC box viewer yet.
        echo "==> minui-list for $platform (MinUI)"
        make -s -C "$NATIVE" minui-list-src
        # cloned on the host (the toolchain container needs no network); CROSS_COMPILE only satisfies its guard here
        make -s -C "$NATIVE/minui-list-src" PLATFORM="$platform" CROSS_COMPILE=host- minui include/parson
        if docker run --rm --platform linux/arm64 -v "$NATIVE":/root/workspace/repo "savant/minui-toolchain:$platform" \
            bash -c "source /root/.bashrc && make -C /root/workspace/repo/minui-list-src PLATFORM=$platform setup && make -C /root/workspace/repo/minui-list-src PLATFORM=$platform"; then
            cp "$NATIVE/minui-list-src/minui-list-$platform" "$NATIVE/minui-list-$platform"
        else
            echo "!! build failed for $platform" >&2
            status=1
        fi
        continue
    fi
    target="$platform-nextui"
    echo "==> pkmgr-box, minui-list, minui-presenter and the screen host for $platform"
    make -s -C "$NATIVE" PLATFORM="$target" nextui include/parson minui-list-src minui-presenter-src
    # PKMGR_KEEP_FRAME: no black frames between the pak's screens (scripts/patch-nextui-keep-frame.py)
    python3 "$ROOT/scripts/patch-nextui-keep-frame.py" "$NATIVE/nextui-$platform"
    if ! docker run --rm --platform linux/arm64 -v "$NATIVE":/root/workspace/repo "savant/minui-toolchain:$target" \
        bash -c "source /root/.bashrc && make -C /root/workspace/repo PLATFORM=$target device minui-list minui-presenter pkmgr-host hosted"; then
        echo "!! build failed for $platform" >&2
        status=1
    fi
done
exit $status
