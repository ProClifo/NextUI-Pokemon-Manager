#!/usr/bin/env bash
# Builds the NextUI pak.
#
#   scripts/build-pak.sh            -> dist/PokemonManager.pak.zip                (pak contents at the zip root, for the Pak Store)
#                                      dist/PokemonManager-<platform>-sdcard.zip (Tools/<platform>/Pokemon Manager.pak/..., unzip onto the SD card)
#                                      for platform in tg5040, tg5050, my355, h700
#
# Needs the .NET 10 SDK, curl, zip, git and Python 3 with Pillow (box art, sprites, icons).
# The event gallery is bundled from projectpokemon/EventsGallery at a pinned commit.
# minui-list (patched) and the PC box viewer are built with Docker (scripts/build-native.sh), so Docker is required.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BUILD="$ROOT/build"
DIST="$ROOT/dist"
PAK="$BUILD/Pokemon Manager.pak"

MINUI_LIST_VERSION="${MINUI_LIST_VERSION:-0.15.4}"  # also pinned in native/Makefile
MINUI_PRESENTER_VERSION="${MINUI_PRESENTER_VERSION:-0.13.4}"
POKEEMERALD_COMMIT="${POKEEMERALD_COMMIT:-731ad5bfd6e6f265508d0efcca0ba42f9dcf5881}"
EVENTSGALLERY_COMMIT="${EVENTSGALLERY_COMMIT:-154d81be88453f6f78ec1d6d86e85fe0f2f5c240}"
NOTO_EMOJI_COMMIT="${NOTO_EMOJI_COMMIT:-e20cbc2bbec1926686be9f9bee7d1d2cfa1fea0e}"
# pret decompilations each game's box icons/sprites are built from (pokeemerald is the commit above)
POKERED_COMMIT="${POKERED_COMMIT:-af519899719f0754965776faac0e836a3b906e6d}"
POKEYELLOW_COMMIT="${POKEYELLOW_COMMIT:-e89ead154b9968aa50eed9328ff2b38b6c194382}"
POKEGOLD_COMMIT="${POKEGOLD_COMMIT:-ef0201d8daf47e8b3ea1518eacf890f37d4cd5e8}"
POKECRYSTAL_COMMIT="${POKECRYSTAL_COMMIT:-3bc8daa4173e96a7f4011dad3922eb6fa5dad5c6}"
POKERUBY_COMMIT="${POKERUBY_COMMIT:-5784633ce4ef7ade1a7f2d2d0c288e3d5e6cdd7f}"
POKEFIRERED_COMMIT="${POKEFIRERED_COMMIT:-037335f4c725d7c9aecdac87066f2002b4bd7e14}"
POKEHEARTGOLD_COMMIT="${POKEHEARTGOLD_COMMIT:-9d8b7591f09b65804da2fb2dfd56f320633e0d36}"
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

echo "==> Fetching minui-presenter $MINUI_PRESENTER_VERSION"
for platform in "${PLATFORMS[@]}"; do
    mkdir -p "$PAK/bin/$platform"
    fetch "https://github.com/josegonzalez/minui-presenter/releases/download/$MINUI_PRESENTER_VERSION/minui-presenter-$platform-nextui" "$PAK/bin/$platform/minui-presenter"
done

# Built from source: the PC box viewer, and minui-list $MINUI_LIST_VERSION patched (native/minui-list.patch) so its
# font options don't crash (the release binaries segfault on --font-large, which picking NextUI's Next/OG font uses) and
# so it can show the Legal/Illegal tag on the highlighted row only.
echo "==> PC box viewer and minui-list"
missing=()
for platform in "${PLATFORMS[@]}"; do
    [ -x "$ROOT/native/pkmgr-box-$platform-nextui" ] && [ -x "$ROOT/native/minui-list-$platform-nextui" ] || missing+=("$platform")
done
if [ ${#missing[@]} -ne 0 ]; then
    command -v docker >/dev/null 2>&1 || { echo "docker is required to build minui-list and the PC box viewer for ${missing[*]}" >&2; exit 1; }
    "$ROOT/scripts/build-native.sh" "${missing[@]}"
fi
for platform in "${PLATFORMS[@]}"; do
    cp "$ROOT/native/minui-list-$platform-nextui" "$PAK/bin/$platform/minui-list"
    cp "$ROOT/native/pkmgr-box-$platform-nextui" "$PAK/bin/$platform/pkmgr-box"
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

echo "==> pret decompilations (PC box art, player sprites)"
DECOMP="$BUILD/decomp"
mkdir -p "$DECOMP"
sparse_clone() {
    local repo="$1" commit="$2"; shift 2
    git clone -q --filter=blob:none --sparse "https://github.com/pret/$repo.git" "$DECOMP/$repo"
    git -C "$DECOMP/$repo" checkout -q "$commit"
    git -C "$DECOMP/$repo" sparse-checkout set --no-cone "$@"
}
GB_ART=('/gfx/pokemon/' '/gfx/icons/' '/gfx/stats/' '/gfx/sprites/' '/gfx/overworld/' '/gfx/sprites.asm' '/data/pokemon/' '/data/sgb/'
    '/data/icon_pointers.asm' '/engine/gfx/mon_icons.asm'
    '/gfx/font/' '/gfx/frames/' '/gfx/pc/' '/gfx/battle/')  # the PC and stats screens' tiles (scripts/gb_ui)
sparse_clone pokered "$POKERED_COMMIT" "${GB_ART[@]}"
sparse_clone pokeyellow "$POKEYELLOW_COMMIT" "${GB_ART[@]}"
sparse_clone pokegold "$POKEGOLD_COMMIT" "${GB_ART[@]}"
sparse_clone pokecrystal "$POKECRYSTAL_COMMIT" "${GB_ART[@]}"
# PC and summary screen skins (scripts/pc_ui) read each GBA game's interface art, fonts and text tables.
GBA_UI=('/charmap.txt' '/graphics/fonts/' '/graphics/interface/' '/graphics/pokemon_storage/' '/graphics/summary_screen/'
    '/graphics/text_window/')
sparse_clone pokeruby "$POKERUBY_COMMIT" '/graphics/pokemon/' '/src/pokemon_icon.c' "${GBA_UI[@]}" '/graphics/misc/' \
    '/graphics/types/' '/misc.mk' '/src/data/text/font3_widths.h' '/src/data/text/font4_widths.h' '/src/data/text/type1_map.h'
sparse_clone pokeemerald "$POKEEMERALD_COMMIT" '/graphics/pokemon_storage/' '/graphics/pokemon/' '/graphics_file_rules.mk' \
    '/src/pokemon_icon.c' '/graphics/object_events/pics/people/' '/src/data/text/abilities.h' '/include/constants/abilities.h' \
    "${GBA_UI[@]}" '/graphics/balls/' '/graphics/battle_interface/' '/graphics/types/' '/src/fonts.c'
sparse_clone pokefirered "$POKEFIRERED_COMMIT" '/graphics/pokemon/' '/src/pokemon_icon.c' \
    '/graphics/object_events/pics/people/' "${GBA_UI[@]}" '/graphics/misc/' '/src/text.c'
sparse_clone pokeplatinum "$POKEPLATINUM_COMMIT" '/res/pokemon/' '/generated/species.txt' \
    '/res/graphics/field_sprites/player/'
# HeartGold's player sprites (MMODEL_HERO, MMODEL_HEROINE)
sparse_clone pokeheartgold "$POKEHEARTGOLD_COMMIT" '/files/data/mmodel/mmodel/mmodel_00000069.NSBTX' '/files/data/mmodel/mmodel/mmodel_00000070.NSBTX'

echo "==> PC box art"
python3 "$ROOT/scripts/build-box-assets.py" "$DECOMP/pokeemerald" "$PAK/res/box"
python3 "$ROOT/scripts/build-box-art.py" "$DECOMP" "$PAK/res/box/art"
python3 "$ROOT/scripts/pc_ui/build.py" "$DECOMP" "$PAK/res/box/ui"
python3 "$ROOT/scripts/gb_ui/build.py" "$DECOMP" "$PAK/res/box/ui"

echo "==> Players' overworld sprites (main menu)"
python3 "$ROOT/scripts/build-trainers.py" "$DECOMP" "$PAK/res/trainers"

echo "==> Legal/Illegal icons (googlefonts/noto-emoji ${NOTO_EMOJI_COMMIT:0:7})"
NOTO="$BUILD/noto-emoji"
git clone -q --filter=blob:none --sparse https://github.com/googlefonts/noto-emoji.git "$NOTO"
git -C "$NOTO" checkout -q "$NOTO_EMOJI_COMMIT"
git -C "$NOTO" sparse-checkout set --no-cone '/2D/png/128/emoji_u2705.png' '/2D/png/128/emoji_u2620.png'
python3 "$ROOT/scripts/build-icons.py" "$NOTO" "$PAK/res/icons"

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
