#!/usr/bin/env bash
# Builds the NextUI pak.
#
#   scripts/build-pak.sh            -> dist/PokemonManager.pak.zip                (pak contents at the zip root, for the Pak Store)
#                                      dist/PokemonManager-<platform>-sdcard.zip (Tools/<platform>/Pokemon Manager.pak/..., unzip onto the SD card)
#                                      for platform in tg5040, tg5050, my355, h700
#
# Needs the .NET 10 SDK, curl, zip, git and Python 3 with Pillow and fontTools (box art, game fonts).
# The event gallery is bundled from projectpokemon/EventsGallery at a pinned commit.
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
EVENTSGALLERY_COMMIT="${EVENTSGALLERY_COMMIT:-154d81be88453f6f78ec1d6d86e85fe0f2f5c240}"
# pret decompilations the game fonts and each game's box icons/sprites are built from (pokeemerald is the commit above)
POKERED_COMMIT="${POKERED_COMMIT:-af519899719f0754965776faac0e836a3b906e6d}"
POKEYELLOW_COMMIT="${POKEYELLOW_COMMIT:-e89ead154b9968aa50eed9328ff2b38b6c194382}"
POKEGOLD_COMMIT="${POKEGOLD_COMMIT:-ef0201d8daf47e8b3ea1518eacf890f37d4cd5e8}"
POKECRYSTAL_COMMIT="${POKECRYSTAL_COMMIT:-3bc8daa4173e96a7f4011dad3922eb6fa5dad5c6}"
POKERUBY_COMMIT="${POKERUBY_COMMIT:-5784633ce4ef7ade1a7f2d2d0c288e3d5e6cdd7f}"
POKEFIRERED_COMMIT="${POKEFIRERED_COMMIT:-037335f4c725d7c9aecdac87066f2002b4bd7e14}"
POKEPLATINUM_COMMIT="${POKEPLATINUM_COMMIT:-c248fb3f8cc9934ded800e489567c5c0eeee92eb}"
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

echo "==> Event gallery (projectpokemon/EventsGallery ${EVENTSGALLERY_COMMIT:0:7}, Gen 1-5)"
GALLERY="$BUILD/EventsGallery"
git clone -q --filter=blob:none --sparse https://github.com/projectpokemon/EventsGallery.git "$GALLERY"
git -C "$GALLERY" checkout -q "$EVENTSGALLERY_COMMIT"
git -C "$GALLERY" sparse-checkout set --no-cone \
    '/Released/Gen 1/' '/Released/Gen 2/' '/Released/Gen 3/' '/Released/Gen 4/' '/Released/Gen 5/' \
    '/Unreleased/Gen 1/' '/Unreleased/Gen 2/' '/Unreleased/Gen 3/' '/Unreleased/Gen 4/' '/Unreleased/Gen 5/' \
    '!*.png' '!*.raw' '!*.json' '!*.txt'
dotnet run --project "$ROOT/src/PokemonManager/PokemonManager.csproj" -c Release -- \
    gallery-build "$GALLERY" "$PAK/res/gallery.zip" 2>"$BUILD/gallery-skipped.log"

echo "==> pret decompilations (game fonts, PC box art)"
DECOMP="$BUILD/decomp"
mkdir -p "$DECOMP"
sparse_clone() {
    local repo="$1" commit="$2"; shift 2
    git clone -q --filter=blob:none --sparse "https://github.com/pret/$repo.git" "$DECOMP/$repo"
    git -C "$DECOMP/$repo" checkout -q "$commit"
    git -C "$DECOMP/$repo" sparse-checkout set --no-cone "$@"
}
GB_ART=('/gfx/pokemon/' '/gfx/icons/' '/gfx/stats/' '/gfx/sprites/' '/gfx/sprites.asm' '/data/pokemon/' '/data/sgb/'
    '/data/icon_pointers.asm' '/engine/gfx/mon_icons.asm')
sparse_clone pokered "$POKERED_COMMIT" '/gfx/font/' '/constants/charmap.asm' "${GB_ART[@]}"
sparse_clone pokeyellow "$POKEYELLOW_COMMIT" "${GB_ART[@]}"
sparse_clone pokegold "$POKEGOLD_COMMIT" '/gfx/font/' '/constants/charmap.asm' "${GB_ART[@]}"
sparse_clone pokecrystal "$POKECRYSTAL_COMMIT" '/gfx/font/' '/constants/charmap.asm' "${GB_ART[@]}"
sparse_clone pokeruby "$POKERUBY_COMMIT" '/graphics/pokemon/' '/src/pokemon_icon.c'
sparse_clone pokeemerald "$POKEEMERALD_COMMIT" '/graphics/pokemon_storage/' '/graphics/pokemon/' '/graphics_file_rules.mk' \
    '/src/pokemon_icon.c' '/graphics/fonts/' '/charmap.txt' '/src/fonts.c'
sparse_clone pokefirered "$POKEFIRERED_COMMIT" '/graphics/fonts/' '/charmap.txt' '/src/text.c' '/graphics/pokemon/' '/src/pokemon_icon.c'
sparse_clone pokeplatinum "$POKEPLATINUM_COMMIT" '/res/fonts/' '/tools/msgenc/charmap.txt' '/res/pokemon/' '/generated/species.txt'

echo "==> PC box art"
python3 "$ROOT/scripts/build-box-assets.py" "$DECOMP/pokeemerald" "$PAK/res/box"
python3 "$ROOT/scripts/build-box-art.py" "$DECOMP" "$PAK/res/box/art"

echo "==> Game fonts"
python3 "$ROOT/scripts/build-fonts.py" "$DECOMP" "$PAK/res/fonts"

echo "==> Per-game menu backgrounds"
python3 "$ROOT/scripts/build-backgrounds.py" "$ROOT/assets/backgrounds" "$PAK/res/backgrounds"

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
