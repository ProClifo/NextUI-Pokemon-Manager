#!/bin/sh
# Pokémon Manager for NextUI - launcher.
# NextUI provides PLATFORM, SDCARD_PATH, USERDATA_PATH and LOGS_PATH.
PAK_DIR="$(cd "$(dirname "$0")" && pwd)"
PAK_NAME="$(basename "$PAK_DIR")"
PAK_NAME="${PAK_NAME%.*}"

: "${SDCARD_PATH:=/mnt/SDCARD}"
: "${USERDATA_PATH:=$SDCARD_PATH/.userdata/$PLATFORM}"
: "${LOGS_PATH:=$USERDATA_PATH/logs}"
mkdir -p "$LOGS_PATH" "$USERDATA_PATH/$PAK_NAME"

rm -f "$LOGS_PATH/$PAK_NAME.txt"
exec >>"$LOGS_PATH/$PAK_NAME.txt" 2>&1
set -x
echo "$0" "$@"

cd "$PAK_DIR" || exit 1

export PATH="$PAK_DIR/bin/$PLATFORM:$PAK_DIR/bin/arm64:$PATH"
export LD_LIBRARY_PATH="$PAK_DIR/lib/$PLATFORM:$PAK_DIR/lib/arm64:$LD_LIBRARY_PATH"

# .NET runtime settings: no ICU on these devices, keep any extracted files on the SD card,
# and use the workstation GC (lower memory use).
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
export DOTNET_BUNDLE_EXTRACT_BASE_DIR="$USERDATA_PATH/$PAK_NAME/dotnet"
export DOTNET_gcServer=0
export TMPDIR="${TMPDIR:-/tmp}"

# Zip extraction on some PCs drops the executable bit.
chmod +x "$PAK_DIR/bin/arm64/pkmgr" "$PAK_DIR/bin/$PLATFORM/"* 2>/dev/null

minui-presenter --message "Loading Pokémon Manager..." --timeout -1 &

"$PAK_DIR/bin/arm64/pkmgr" --sd "$SDCARD_PATH" ui
status=$?

killall minui-presenter >/dev/null 2>&1 || true
if [ "$status" -ne 0 ]; then
    minui-presenter --message "Pokémon Manager stopped unexpectedly (code $status).\nSee .userdata/$PLATFORM/logs/$PAK_NAME.txt" --timeout 8
fi
exit 0
