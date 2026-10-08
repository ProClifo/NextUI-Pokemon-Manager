#!/usr/bin/env bash
# Builds the NextUI pak.
#
#   scripts/build-pak.sh            -> dist/PokemonManager.pak.zip      (pak contents at the zip root, for the Pak Store)
#                                      dist/PokemonManager-tg5040-sdcard.zip (Tools/tg5040/Pokemon Manager.pak/..., unzip onto the SD card)
#
# Needs the .NET 10 SDK, curl and zip.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BUILD="$ROOT/build"
DIST="$ROOT/dist"
PAK="$BUILD/Pokemon Manager.pak"

MINUI_LIST_VERSION="${MINUI_LIST_VERSION:-0.15.4}"
MINUI_PRESENTER_VERSION="${MINUI_PRESENTER_VERSION:-0.13.4}"
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

cp "$ROOT/launch.sh" "$ROOT/pak.json" "$ROOT/LICENSE" "$PAK/"
cp "$ROOT/README.md" "$PAK/README.md"
chmod +x "$PAK/launch.sh"

echo "==> Zipping"
(cd "$PAK" && zip -qr9 "$DIST/PokemonManager.pak.zip" .)
mkdir -p "$BUILD/sdcard/Tools/tg5040"
cp -a "$PAK" "$BUILD/sdcard/Tools/tg5040/"
mkdir -p "$BUILD/sdcard/PokemonManager/Gifts" "$BUILD/sdcard/PokemonManager/Import"
(cd "$BUILD/sdcard" && zip -qr9 "$DIST/PokemonManager-tg5040-sdcard.zip" .)

ls -la "$DIST"
