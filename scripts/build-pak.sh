#!/usr/bin/env bash
# Builds the NextUI pak.
#
#   scripts/build-pak.sh            -> dist/PokemonManager.pak.zip                (pak contents at the zip root, for the Pak Store)
#                                      dist/PokemonManager-<platform>-sdcard.zip (Tools/<platform>/Pokemon Manager.pak/..., unzip onto the SD card)
#                                      for platform in tg5040, tg5050, my355, h700
#
# Needs the .NET 10 SDK, curl, zip, git and Python 3 with Pillow (for the PC box art).
# The PC box viewer is built with Docker (scripts/build-native.sh); without Docker the pak still
# works and shows Pokémon as lists.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BUILD="$ROOT/build"
DIST="$ROOT/dist"
PAK="$BUILD/Pokemon Manager.pak"

MINUI_LIST_VERSION="${MINUI_LIST_VERSION:-0.15.4}"
MINUI_PRESENTER_VERSION="${MINUI_PRESENTER_VERSION:-0.13.4}"
POKEEMERALD_COMMIT="${POKEEMERALD_COMMIT:-731ad5bfd6e6f265508d0efcca0ba42f9dcf5881}"
PLATFORMS=(tg5040 tg5050 my355 h700)

VERSION="$(sed -n 's/.*"version": *"\([^"]*\)".*/\1/p' "$ROOT/pak.json")"

rm -rf "$BUILD" "$DIST"
mkdir -p "$PAK/bin/arm64" "$DIST"

echo "==> Publishing pkmgr $VERSION for linux-arm64"
dotnet publish "$ROOT/src/PokemonManager/PokemonManager.csproj" \
    -c Release -r linux-arm64 --self-contained \
    -p:Version="$VERSION" \
    -p:PublishSingleFile=true \
    -p:PublishReadyToRun=true \
    -p:PublishTrimmed=true -p:TrimMode=partial \
    -p:DebugType=none \
    -o "$BUILD/publish"
cp "$BUILD/publish/pkmgr" "$PAK/bin/arm64/pkmgr"

fetch() {
    local url="$1" dest="$2"
    echo "    $url"
    curl -fsSL --retry 4 -o "$dest" "$url"
    chmod +x "$dest"
}

echo "==> Fetching minui-list $MINUI_LIST_VERSION and minui-presenter $MINUI_PRESENTER_VERSION"
for platform in "${PLATFORMS[@]}"; do
    mkdir -p "$PAK/bin/$platform"
    fetch "https://github.com/josegonzalez/minui-list/releases/download/$MINUI_LIST_VERSION/minui-list-$platform-nextui" "$PAK/bin/$platform/minui-list"
    fetch "https://github.com/josegonzalez/minui-presenter/releases/download/$MINUI_PRESENTER_VERSION/minui-presenter-$platform-nextui" "$PAK/bin/$platform/minui-presenter"
done

echo "==> PC box viewer"
missing=()
for platform in "${PLATFORMS[@]}"; do
    [ -x "$ROOT/native/pkmgr-box-$platform-nextui" ] || missing+=("$platform")
done
if [ ${#missing[@]} -ne 0 ]; then
    if command -v docker >/dev/null 2>&1; then
        if ! "$ROOT/scripts/build-native.sh" "${missing[@]}"; then
            [ -n "${REQUIRE_BOX_VIEWER:-}" ] && exit 1
            echo "!! some viewer builds failed; those platforms will use the list view" >&2
        fi
    else
        [ -n "${REQUIRE_BOX_VIEWER:-}" ] && { echo "docker is required to build the PC box viewer" >&2; exit 1; }
        echo "!! docker not found: no PC box viewer for ${missing[*]} (the app falls back to lists)" >&2
    fi
fi
for platform in "${PLATFORMS[@]}"; do
    if [ -x "$ROOT/native/pkmgr-box-$platform-nextui" ]; then
        cp "$ROOT/native/pkmgr-box-$platform-nextui" "$PAK/bin/$platform/pkmgr-box"
    fi
done

echo "==> PC box art (generated from pret/pokeemerald ${POKEEMERALD_COMMIT:0:7})"
EMERALD="$BUILD/pokeemerald"
git clone -q --filter=blob:none --sparse https://github.com/pret/pokeemerald.git "$EMERALD"
git -C "$EMERALD" checkout -q "$POKEEMERALD_COMMIT"
git -C "$EMERALD" sparse-checkout set --no-cone     '/graphics/pokemon_storage/' '/graphics/pokemon/' '/graphics_file_rules.mk'     '/src/pokemon_icon.c' '/include/constants/species.h' '/include/constants/pokedex.h'
python3 "$ROOT/scripts/build-box-assets.py" "$EMERALD" "$PAK/res/box"

cp "$ROOT/launch.sh" "$ROOT/pak.json" "$ROOT/LICENSE" "$PAK/"
cp "$ROOT/README.md" "$PAK/README.md"
chmod +x "$PAK/launch.sh"

echo "==> Zipping"
(cd "$PAK" && zip -qr9 "$DIST/PokemonManager.pak.zip" .)
# One ready-to-unzip SD card layout per platform, so the pak lands in the right Tools folder.
for platform in "${PLATFORMS[@]}"; do
    sdcard="$BUILD/sdcard-$platform"
    mkdir -p "$sdcard/Tools/$platform" "$sdcard/PokemonManager/Gifts" "$sdcard/PokemonManager/Import"
    cp -a "$PAK" "$sdcard/Tools/$platform/"
    (cd "$sdcard" && zip -qr9 "$DIST/PokemonManager-$platform-sdcard.zip" .)
done

ls -la "$DIST"
